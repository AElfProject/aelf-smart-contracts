using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Acs0;
using Acs3;
using InitializeInput = Acs0.InitializeInput;

namespace AElf.Contracts.Genesis
{
    public partial class BasicContractZero : BasicContractZeroContainer.BasicContractZeroBase
    {
        #region Views

        public override UInt64Value CurrentContractSerialNumber(Empty input)
        {
            return new UInt64Value() {Value = State.ContractSerialNumber.Value};
        }

        public override ContractInfo GetContractInfo(Address input)
        {
            var info = State.ContractInfos[input];
            if (info == null)
            {
                return new ContractInfo();
            }

            return info;
        }

        public override Address GetContractAuthor(Address input)
        {
            var info = State.ContractInfos[input];
            return info?.Author;
        }

        public override Hash GetContractHash(Address input)
        {
            var info = State.ContractInfos[input];
            return info?.CodeHash;
        }

        public override Address GetContractAddressByName(Hash input)
        {
            var address = State.NameAddressMapping[input];
            return address;
        }

        public override SmartContractRegistration GetSmartContractRegistrationByAddress(Address input)
        {
            var info = State.ContractInfos[input];
            if (info == null)
            {
                return null;
            }

            return State.SmartContractRegistrations[info.CodeHash];
        }

        public override Empty ValidateSystemContractAddress(ValidateSystemContractAddressInput input)
        {
            var actualAddress = GetContractAddressByName(input.SystemContractHashName); 
            Assert(actualAddress == input.Address, "Address not expected.");
            return new Empty();
        }

        public override AddressList GetDeployedContractAddressList(Empty input)
        {
            return State.DeployedContractAddressList.Value;
        }

        #endregion Views

        #region Actions

        public override Address DeploySystemSmartContract(SystemContractDeploymentInput input)
        {
            RequireSenderAuthority(State.GenesisOwner?.Value);
            var name = input.Name;
            var category = input.Category;
            var code = input.Code.ToByteArray();
            var transactionMethodCallList = input.TransactionMethodCallList;
            var address = PrivateDeploySystemSmartContract(name, category, code, true);

            if (transactionMethodCallList != null)
            {
                foreach (var methodCall in transactionMethodCallList.Value)
                {
                    Context.SendInline(address, methodCall.MethodName, methodCall.Params);
                }
            }

            return address;
        }

        private Address PrivateDeploySystemSmartContract(Hash name, int category, byte[] code, bool isSystemContract)
        {
            if (name != null)
                Assert(State.NameAddressMapping[name] == null, "contract name already been registered");

            var serialNumber = State.ContractSerialNumber.Value;
            // Increment
            State.ContractSerialNumber.Value = serialNumber + 1;
            var contractAddress = AddressHelper.BuildContractAddress(Context.ChainId, serialNumber);

            var codeHash = Hash.FromRawBytes(code);

            var info = new ContractInfo
            {
                SerialNumber = serialNumber,
                Author = DecideContractAuthor(),
                Category = category,
                CodeHash = codeHash,
                IsSystemContract = isSystemContract
            };
            State.ContractInfos[contractAddress] = info;

            var reg = new SmartContractRegistration
            {
                Category = category,
                Code = ByteString.CopyFrom(code),
                CodeHash = codeHash
            };

            State.SmartContractRegistrations[reg.CodeHash] = reg;

            Context.DeployContract(contractAddress, reg, name);

            Context.Fire(new ContractDeployed
            {
                CodeHash = codeHash,
                Address = contractAddress,
                Creator = Context.Origin
            });

            var deployedContractAddressList = State.DeployedContractAddressList.Value;
            if (deployedContractAddressList == null)
            {
                State.DeployedContractAddressList.Value = new AddressList {Value = {contractAddress}};
            }
            else
            {
                deployedContractAddressList.Value.Add(contractAddress);
                State.DeployedContractAddressList.Value = deployedContractAddressList;
            }

            Context.LogDebug(() => "BasicContractZero - Deployment ContractHash: " + codeHash.ToHex());
            Context.LogDebug(() => "BasicContractZero - Deployment success: " + contractAddress.GetFormatted());

            if (name != null)
                State.NameAddressMapping[name] = contractAddress;

            return contractAddress;
        }
        
        public override Hash ProposeNewContract(ContractDeploymentInput input)
        {
            RequireSenderAuthority();
            AssertProposerAuthority(Context.Origin);
            var proposedContractInputHash = CalculateHashFromInput(input);
            Assert(State.ContractProposingInfoMap[proposedContractInputHash] == null, "Already proposed.");
            State.ContractProposingInfoMap[proposedContractInputHash] = new ContractProposingInfo
            {
                Proposer = Context.Origin
            };
            
            RequireParliamentAuthAddressSet();
            
            // Create proposal for deployment
            State.ParliamentAuthContract.CreateProposal.Send(new CreateProposalInput
            {
                ToAddress = Context.Self,
                ContractMethodName = nameof(BasicContractZeroContainer.BasicContractZeroBase.DeploySmartContract),
                Params = input.ToByteString(),
                OrganizationAddress = State.GenesisOwner.Value,
                ExpiredTime = Context.CurrentBlockTime.AddMinutes(10) // Maybe, get the interval from configuration
            });
            
            // Fire event to trigger BPs checking contract code
            Context.Fire(new CodeCheckRequired
            {
                Code = input.Code,
                ProposedContractInputHash = proposedContractInputHash
            });
    
            return proposedContractInputHash;
        }
        
        public override Empty ReleaseApprovedContract(ReleaseApprovedContractInput input)
        {
            var contractProposingInfo = State.ContractProposingInfoMap[input.ProposedContractInputHash];
            Assert(contractProposingInfo != null && contractProposingInfo.Proposer == Context.Origin,
                "Contract proposing info not found or inconsistent with proposer.");
            contractProposingInfo.IsReleased = true;
            State.ContractProposingInfoMap[input.ProposedContractInputHash] = contractProposingInfo;
            State.ParliamentAuthContract.Release.Send(input.ProposalId);
            return new Empty();
        }

        public override Hash ProposeUpdateContract(ContractUpdateInput input)
        {
            RequireSenderAuthority();
            var proposedContractInputHash = CalculateHashFromInput(input);
            Assert(State.ContractProposingInfoMap[proposedContractInputHash] == null, "Already proposed.");
            State.ContractProposingInfoMap[proposedContractInputHash] = new ContractProposingInfo
            {
                Proposer = Context.Origin
            };
            
            var contractAddress = input.Address;
            var info = State.ContractInfos[contractAddress];
            Assert(info != null, "Contract does not exist.");
            AssertAuthorByContractInfo(info);

            // Create proposal for deployment
            RequireParliamentAuthAddressSet();
            State.ParliamentAuthContract.CreateProposal.Send(new CreateProposalInput
            {
                ToAddress = Context.Self,
                ContractMethodName = nameof(BasicContractZeroContainer.BasicContractZeroBase.UpdateSmartContract),
                Params = input.ToByteString(),
                OrganizationAddress = State.GenesisOwner.Value,
                ExpiredTime = Context.CurrentBlockTime.AddMinutes(10) // Maybe, get the interval from configuration
            });

            // Fire event to trigger BPs checking contract code
            Context.Fire(new CodeCheckRequired
            {
                Code = input.Code,
                ProposedContractInputHash = proposedContractInputHash
            });
    
            return proposedContractInputHash;
        }

        public override Address DeploySmartContract(ContractDeploymentInput input)
        {
            RequireSenderAuthority();
            AssertProposerAuthority(Context.Origin);
            var inputHash = CalculateHashFromInput(input);
            RemoveContractProposingInfo(inputHash);
            var address = PrivateDeploySystemSmartContract(null, input.Category, input.Code.ToByteArray(), false);
            return address;
        }

        public override Address UpdateSmartContract(ContractUpdateInput input)
        {
            var contractAddress = input.Address;
            var code = input.Code.ToByteArray();
            var info = State.ContractInfos[contractAddress];
            Assert(info != null, "Contract not found.");

            RequireSenderAuthority(info.IsSystemContract ? State.GenesisOwner.Value : info.Author);
            AssertAuthorByContractInfo(info);

            var inputHash = CalculateHashFromInput(input);
            RemoveContractProposingInfo(inputHash);

            var oldCodeHash = info.CodeHash;
            var newCodeHash = Hash.FromRawBytes(code);
            Assert(!oldCodeHash.Equals(newCodeHash), "Code is not changed.");

            info.CodeHash = newCodeHash;
            State.ContractInfos[contractAddress] = info;

            var reg = new SmartContractRegistration
            {
                Category = info.Category,
                Code = ByteString.CopyFrom(code),
                CodeHash = newCodeHash
            };

            State.SmartContractRegistrations[reg.CodeHash] = reg;

            Context.UpdateContract(contractAddress, reg, null);

            Context.Fire(new CodeUpdated()
            {
                Address = contractAddress,
                OldCodeHash = oldCodeHash,
                NewCodeHash = newCodeHash
            });

            Context.LogDebug(() => "BasicContractZero - update success: " + contractAddress.GetFormatted());
            return contractAddress;
        }

        public override Empty ChangeContractAuthor(ChangeContractAuthorInput input)
        {
            var contractAddress = input.ContractAddress;
            var info = State.ContractInfos[contractAddress];
            Assert(info != null, "Contract not found.");
            AssertAuthorByContractInfo(info);
            var oldAuthor = info.Author;
            info.Author = input.NewAuthor;
            State.ContractInfos[contractAddress] = info;
            var newAuthor = input.NewAuthor;
            Context.Fire(new AuthorChanged
            {
                Address = contractAddress,
                OldAuthor = oldAuthor,
                NewAuthor = newAuthor
            });
            return new Empty();
        }

        public override Empty Initialize(InitializeInput input)
        {
            Assert(!State.Initialized.Value, "Contract zero already initialized.");
            Assert(Context.Sender == Context.Self, "Unable to initialize.");
            State.ContractDeploymentAuthorityRequired.Value = input.ContractDeploymentAuthorityRequired;
            State.Initialized.Value = true;
            return new Empty();
        }

        public override Empty ChangeGenesisOwner(Address newOwnerAddress)
        {
            if (State.GenesisOwner.Value == null)
                InitializeGenesisOwner(newOwnerAddress);
            else
            {
                AssertSenderAddressWith(State.GenesisOwner.Value);
                RequireParliamentAuthAddressSet();
                var organizationExist =
                    State.ParliamentAuthContract.ValidateOrganizationExist.Call(newOwnerAddress).Value;
                Assert(organizationExist, "Invalid genesis owner address.");
                State.GenesisOwner.Value = newOwnerAddress;
            }

            return new Empty();
        }

        #endregion Actions
    }
}