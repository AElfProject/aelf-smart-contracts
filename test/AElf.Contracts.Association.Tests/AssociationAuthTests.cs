using System.Threading;
using System.Threading.Tasks;
using Acs3;
using AElf.Contracts.MultiToken;
using AElf.Contracts.TestKit;
using AElf.Cryptography.ECDSA;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Shouldly;
using Xunit;
using ApproveInput = Acs3.ApproveInput;

namespace AElf.Contracts.Association
{
    public class AssociationAuthTests : AssociationAuthContractTestBase
    {
        public AssociationAuthTests()
        {
            DeployContracts();
        }

        [Fact]
        public async Task Get_Organization_Test()
        {
            //failed case
            {
                var organization =
                    await AssociationAuthContractStub.GetOrganization.CallAsync(SampleAddress.AddressList[0]);
                organization.ShouldBe(new Organization());
            }

            //normal case
            {
                var reviewer1 = new Reviewer {Address = Reviewer1, Weight = 1};
                var reviewer2 = new Reviewer {Address = Reviewer2, Weight = 2};
                var reviewer3 = new Reviewer {Address = Reviewer3, Weight = 3};

                var createOrganizationInput = new CreateOrganizationInput
                {
                    Reviewers = {reviewer1, reviewer2, reviewer3},
                    ReleaseThreshold = 2,
                    ProposerThreshold = 2
                };
                var transactionResult =
                    await AssociationAuthContractStub.CreateOrganization.SendAsync(createOrganizationInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
                var organizationAddress = transactionResult.Output;
                var getOrganization = await AssociationAuthContractStub.GetOrganization.CallAsync(organizationAddress);

                getOrganization.OrganizationAddress.ShouldBe(organizationAddress);
                getOrganization.Reviewers[0].Address.ShouldBe(Reviewer1);
                getOrganization.Reviewers[0].Weight.ShouldBe(1);
                getOrganization.ProposerThreshold.ShouldBe(2);
                getOrganization.ReleaseThreshold.ShouldBe(2);
                getOrganization.OrganizationHash.ShouldBe(Hash.FromTwoHashes(
                    Hash.FromMessage(AssociationAuthContractAddress), Hash.FromMessage(createOrganizationInput)));
            }
        }

        [Fact]
        public async Task Get_Proposal_Test()
        {
            //failed case
            {
                var proposal = await AssociationAuthContractStub.GetProposal.CallAsync(Hash.FromString("Test"));
                proposal.ShouldBe(new ProposalOutput());
            }
            
            //normal case
            {
                var organizationAddress = await CreateOrganizationAsync();
                var transferInput = new TransferInput()
                {
                    Symbol = "ELF",
                    Amount = 100,
                    To = Reviewer1,
                    Memo = "Transfer"
                };
                var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
                var getProposal = await AssociationAuthContractStub.GetProposal.SendAsync(proposalId);

                getProposal.Output.Proposer.ShouldBe(Reviewer2);
                getProposal.Output.ContractMethodName.ShouldBe(nameof(TokenContract.Transfer));
                getProposal.Output.ProposalId.ShouldBe(proposalId);
                getProposal.Output.OrganizationAddress.ShouldBe(organizationAddress);
                getProposal.Output.ToAddress.ShouldBe(TokenContractAddress);
                getProposal.Output.Params.ShouldBe(transferInput.ToByteString());
            }
        }

        [Fact]
        public async Task Create_Organization_Failed_Test()
        {
            var reviewer1 = new Reviewer {Address = Reviewer1, Weight = 1};
            var reviewer2 = new Reviewer {Address = Reviewer2, Weight = 2};
            var reviewer3 = new Reviewer {Address = Reviewer3, Weight = 3};

            var createOrganizationInput = new CreateOrganizationInput
            {
                Reviewers = {reviewer1, reviewer2, reviewer3},
                ReleaseThreshold = 2,
                ProposerThreshold = 2
            };
            //isValidWeight
            {
                createOrganizationInput.Reviewers[0].Weight = -1;
                var transactionResult =
                    await AssociationAuthContractStub.CreateOrganization.SendWithExceptionAsync(createOrganizationInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Invalid organization.").ShouldBeTrue();
            }
            //canBeProposed
            {
                createOrganizationInput.Reviewers[0].Weight = 1;
                createOrganizationInput.ProposerThreshold = 10;
                var transactionResult =
                    await AssociationAuthContractStub.CreateOrganization.SendWithExceptionAsync(createOrganizationInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Invalid organization.").ShouldBeTrue();
            }
            //canBeReleased
            {
                createOrganizationInput.ProposerThreshold = 2;
                createOrganizationInput.ReleaseThreshold = 10;
                var transactionResult =
                    await AssociationAuthContractStub.CreateOrganization.SendWithExceptionAsync(createOrganizationInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Invalid organization.").ShouldBeTrue();
            }
        }

        [Fact]
        public async Task Create_Proposal_Failed_Test()
        {
            var organizationAddress = await CreateOrganizationAsync();
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
            var blockTime = BlockTimeProvider.GetBlockTime();
            var createProposalInput = new CreateProposalInput
            {
                ToAddress = SampleAddress.AddressList[0],
                Params = ByteString.CopyFromUtf8("Test"),
                ExpiredTime = blockTime.AddDays(1),
                OrganizationAddress = organizationAddress
            };
            //"Invalid proposal."
            //ContractMethodName is null or white space
            {
                var transactionResult = await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Invalid proposal.").ShouldBeTrue();
            }
            //ToAddress is null
            {
                createProposalInput.ContractMethodName = "Test";
                createProposalInput.ToAddress = null;

                var transactionResult = await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Invalid proposal.").ShouldBeTrue();
            }
            //ExpiredTime is null
            {
                createProposalInput.ExpiredTime = null;
                createProposalInput.ToAddress = SampleAddress.AddressList[0];

                var transactionResult = await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Invalid proposal.").ShouldBeTrue();
            }
            //"Expired proposal."
            {
                createProposalInput.ExpiredTime = blockTime.AddMilliseconds(5);
                Thread.Sleep(10);

                var transactionResult = await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            }
            //"No registered organization."
            {
                createProposalInput.ExpiredTime = BlockTimeProvider.GetBlockTime().AddDays(1);
                createProposalInput.OrganizationAddress = SampleAddress.AddressList[1];

                var transactionResult = await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("No registered organization.").ShouldBeTrue();
            }
            //"Unable to propose."
            //Sender is not reviewer
            {
                createProposalInput.OrganizationAddress = organizationAddress;
                AssociationAuthContractStub = GetAssociationAuthContractTester(DefaultSenderKeyPair);

                var transactionResult =
                    await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            }
            //Reviewer is not permission to propose
            {
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer1KeyPair);
                var transactionResult = await AssociationAuthContractStub.CreateProposal.SendWithExceptionAsync(createProposalInput);
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                transactionResult.TransactionResult.Error.Contains("Unable to propose.").ShouldBeTrue();
            }
            //"Proposal with same input."
            {
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
                var transactionResult1 =
                    await AssociationAuthContractStub.CreateProposal.SendAsync(createProposalInput);
                transactionResult1.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

                var transactionResult2 =
                    await AssociationAuthContractStub.CreateProposal.SendAsync(createProposalInput);
                transactionResult2.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            }
        }

        [Fact]
        public async Task Approve_Proposal_NotFoundProposal_Test()
        {
            var transactionResult = await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput
            {
                ProposalId = Hash.FromString("Test")
            });
            transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
        }

        [Fact]
        public async Task Approve_Proposal_NotAuthorizedApproval_Test()
        {
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            AssociationAuthContractStub = GetAssociationAuthContractTester(DefaultSenderKeyPair);
            var transactionResult = await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput
            {
                ProposalId = proposalId
            });
            transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
        }

        [Fact]
        public async Task Approve_Proposal_ExpiredTime_Test()
        {
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer1KeyPair);
            BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddDays(5));
            var error = await AssociationAuthContractStub.Approve.CallWithExceptionAsync(new ApproveInput
            {
                ProposalId = proposalId
            });
            error.Value.ShouldContain("Invalid proposal.");
        }

        [Fact]
        public async Task Approve_Proposal_ApprovalAlreadyExists_Test()
        {
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer1KeyPair);

            var transactionResult1 =
                await AssociationAuthContractStub.Approve.SendAsync(new ApproveInput {ProposalId = proposalId});
            transactionResult1.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            transactionResult1.Output.Value.ShouldBe(true);

            var transactionResult2 =
                await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput {ProposalId = proposalId});
            transactionResult2.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
        }

        [Fact]
        public async Task Approve_Proposal_Failed_Test()
        {
            //not found
            {
                var transactionResult = await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput
                {
                    ProposalId = Hash.FromString("Test")
                });
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            }
            
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            
            //not authorize
            {
                AssociationAuthContractStub = GetAssociationAuthContractTester(DefaultSenderKeyPair);
                var transactionResult = await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput
                {
                    ProposalId = proposalId
                });
                transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            }
            
            //expired time
            {
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer1KeyPair);
                BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddDays(5));
                var error = await AssociationAuthContractStub.Approve.CallWithExceptionAsync(new ApproveInput
                {
                    ProposalId = proposalId
                });
                error.Value.ShouldContain("Invalid proposal.");
            }
            
            //already exist
            {
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer1KeyPair);
                BlockTimeProvider.SetBlockTime(BlockTimeProvider.GetBlockTime().AddDays(-5));
                var transactionResult1 =
                    await AssociationAuthContractStub.Approve.SendAsync(new ApproveInput {ProposalId = proposalId});
                transactionResult1.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
                transactionResult1.Output.Value.ShouldBe(true);

                var transactionResult2 =
                    await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput {ProposalId = proposalId});
                transactionResult2.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            }
        }
        
        [Fact]
        public async Task Release_Proposal_Failed_Test()
        {
            //not found
            {
                var fakeId = Hash.FromString("test");
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
                var result = await AssociationAuthContractStub.Release.SendWithExceptionAsync(fakeId);
                //Proposal not found
                result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                result.TransactionResult.Error.Contains("Proposal not found.").ShouldBeTrue();
            }
            
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            await TransferToOrganizationAddressAsync(organizationAddress);
            
            //not enough weight
            {
                await ApproveAsync(Reviewer1KeyPair,proposalId);
                
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
                var result = await AssociationAuthContractStub.Release.SendWithExceptionAsync(proposalId);
                //Reviewer weight < ReleaseThreshold, release failed
                result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                result.TransactionResult.Error.Contains("Not approved.").ShouldBeTrue();
            }
            
            //wrong sender
            {
                await ApproveAsync(Reviewer3KeyPair,proposalId);
  
                AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer1KeyPair);
                var result = await AssociationAuthContractStub.Release.SendWithExceptionAsync(proposalId);
                result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
                result.TransactionResult.Error.Contains("Unable to release this proposal.").ShouldBeTrue();
            }
        }
        
        [Fact]
        public async Task Release_Proposal_Success_Test()
        {
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            await TransferToOrganizationAddressAsync(organizationAddress);
            await ApproveAsync(Reviewer3KeyPair,proposalId);
  
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
            var result = await AssociationAuthContractStub.Release.SendAsync(proposalId);
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            // Check inline transaction result
            var getBalance = TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Symbol = "ELF",
                Owner = Reviewer1
            }).Result.Balance;
            getBalance.ShouldBe(100);
        }

        [Fact]
        public async Task Release_Proposal_AlreadyReleased_Test()
        {
            var organizationAddress = await CreateOrganizationAsync();
            var proposalId = await CreateProposalAsync(Reviewer2KeyPair,organizationAddress);
            await TransferToOrganizationAddressAsync(organizationAddress);
            await ApproveAsync(Reviewer3KeyPair,proposalId);
  
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
            var result = await AssociationAuthContractStub.Release.SendAsync(proposalId);
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            var proposalReleased = ProposalReleased.Parser.ParseFrom(result.TransactionResult.Logs[0].NonIndexed)
                .ProposalId;
            proposalReleased.ShouldBe(proposalId);
            
            //After release,the proposal will be deleted
            var getProposal = await AssociationAuthContractStub.GetProposal.CallAsync(proposalId);
            getProposal.ShouldBe(new ProposalOutput());
            
            //approve the same proposal again
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer3KeyPair);
            var transactionResult =
                await AssociationAuthContractStub.Approve.SendWithExceptionAsync(new ApproveInput {ProposalId = proposalId});
            transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            transactionResult.TransactionResult.Error.ShouldContain("Invalid proposal");
            
            //release the same proposal again
            AssociationAuthContractStub = GetAssociationAuthContractTester(Reviewer2KeyPair);
            var transactionResult2 = await AssociationAuthContractStub.Release.SendWithExceptionAsync(proposalId);
            transactionResult2.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            transactionResult2.TransactionResult.Error.ShouldContain("Proposal not found.");
        }

        private async Task<Hash> CreateProposalAsync(ECKeyPair proposalKeyPair,Address organizationAddress)
        {
            var transferInput = new TransferInput()
            {
                Symbol = "ELF",
                Amount = 100,
                To = Reviewer1,
                Memo = "Transfer"
            };
            AssociationAuthContractStub = GetAssociationAuthContractTester(proposalKeyPair);
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = nameof(TokenContract.Transfer),
                ToAddress = TokenContractAddress,
                Params = transferInput.ToByteString(),
                ExpiredTime = BlockTimeProvider.GetBlockTime().AddDays(2),
                OrganizationAddress = organizationAddress
            };
            var proposal = await AssociationAuthContractStub.CreateProposal.SendAsync(createProposalInput);
            var proposalCreated = ProposalCreated.Parser.ParseFrom(proposal.TransactionResult.Logs[0].NonIndexed)
                .ProposalId;
            proposal.Output.ShouldBe(proposalCreated);
            
            return proposal.Output;
        }

        private async Task<Address> CreateOrganizationAsync()
        {
            var reviewer1 = new Reviewer {Address = Reviewer1, Weight = 1};
            var reviewer2 = new Reviewer {Address = Reviewer2, Weight = 2};
            var reviewer3 = new Reviewer {Address = Reviewer3, Weight = 3};

            var createOrganizationInput = new CreateOrganizationInput
            {
                Reviewers = {reviewer1, reviewer2, reviewer3},
                ReleaseThreshold = 2,
                ProposerThreshold = 2
            };
            var transactionResult =
                await AssociationAuthContractStub.CreateOrganization.SendAsync(createOrganizationInput);
            transactionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            return transactionResult.Output;
        }

        private async Task TransferToOrganizationAddressAsync(Address organizationAddress)
        {
            await TokenContractStub.Transfer.SendAsync(new TransferInput
            {
                Symbol = "ELF",
                Amount = 200,
                To = organizationAddress,
                Memo = "transfer organization address"
            });
        }
        
        private async Task ApproveAsync(ECKeyPair reviewer, Hash proposalId )
        {
            AssociationAuthContractStub = GetAssociationAuthContractTester(reviewer);

            var transactionResult1 =
                await AssociationAuthContractStub.Approve.SendAsync(new ApproveInput {ProposalId = proposalId});
            transactionResult1.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            transactionResult1.Output.Value.ShouldBe(true);
        }
    }
}