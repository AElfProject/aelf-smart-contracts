using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.CrossChain;
using AElf.Contracts.MultiToken;
using AElf.Contracts.MultiToken.Messages;
using AElf.Contracts.ParliamentAuth;
using AElf.Contracts.TestBase;
using AElf.CrossChain;
using AElf.Cryptography.ECDSA;
using AElf.Kernel;
using AElf.Kernel.Consensus;
using AElf.Kernel.Token;
using Google.Protobuf;
using Shouldly;
using Volo.Abp.Threading;
using InitializeInput = AElf.Contracts.CrossChain.InitializeInput;

namespace AElf.Contract.CrossChain.Tests
{
    public class CrossChainContractTestBase : ContractTestBase<CrossChainContractTestAElfModule>
    {
        protected Address CrossChainContractAddress;
        protected Address TokenContractAddress;
        protected Address ConsensusContractAddress;
        protected Address ParliamentAddress;

        protected long _totalSupply;
        protected long _balanceOfStarter;
        public CrossChainContractTestBase()
        {
            AsyncHelper.RunSync(() =>
                Tester.InitialChainAsync(Tester.GetDefaultContractTypes(Tester.GetCallOwnerAddress(), out _totalSupply, out _,
                    out _balanceOfStarter)));
            CrossChainContractAddress = Tester.GetContractAddress(CrossChainSmartContractAddressNameProvider.Name);
            TokenContractAddress = Tester.GetContractAddress(TokenSmartContractAddressNameProvider.Name);
            ConsensusContractAddress = Tester.GetContractAddress(ConsensusSmartContractAddressNameProvider.Name);
            ParliamentAddress = Tester.GetContractAddress(ParliamentAuthContractAddressNameProvider.Name);
        }

        protected async Task ApproveBalance(long amount)
        {
            var callOwner = Address.FromPublicKey(Tester.KeyPair.PublicKey);

            var approveResult = await Tester.ExecuteContractWithMiningAsync(TokenContractAddress,
                nameof(TokenContract.Approve), new ApproveInput
                {
                    Spender = CrossChainContractAddress,
                    Symbol = "ELF",
                    Amount = amount
                });
            approveResult.Status.ShouldBe(TransactionResultStatus.Mined);
            await Tester.CallContractMethodAsync(TokenContractAddress, nameof(TokenContract.GetAllowance),
                new GetAllowanceInput
                {
                    Symbol = "ELF",
                    Owner = callOwner,
                    Spender = CrossChainContractAddress
                });
        }

        protected async Task InitializeCrossChainContract(int parentChainId = 0)
        {
            var crossChainInitializationTransaction = await Tester.GenerateTransactionAsync(CrossChainContractAddress,
                nameof(CrossChainContract.Initialize), new InitializeInput
                {
                    ConsensusContractSystemName = ConsensusSmartContractAddressNameProvider.Name,
                    TokenContractSystemName = TokenSmartContractAddressNameProvider.Name,
                    ParentChainId = parentChainId == 0 ? ChainHelpers.ConvertBase58ToChainId("AELF") : parentChainId,
                    ParliamentContractSystemName = ParliamentAuthContractAddressNameProvider.Name
                });
            var parliamentTransaction = await Tester.GenerateTransactionAsync(ParliamentAddress,
                nameof(ParliamentAuthContract.CreateOrganization), new CreateOrganizationInput
                {
                    ReleaseThresholdInFraction = 2d / 3
                });
            await Tester.MineAsync(new List<Transaction> {crossChainInitializationTransaction, parliamentTransaction});

        }

        protected async Task<int> InitAndCreateSideChain(int parentChainId = 0, long lockedTokenAmount = 10)
        {
            await InitializeCrossChainContract(parentChainId);

            await ApproveBalance(lockedTokenAmount);
            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.Empty);
            var requestTxResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
                nameof(CrossChainContract.RequestChainCreation),
                sideChainCreationRequest);
            await ApproveWithMiners(RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ProposalId);
            var chainId = ChainHelpers.GetChainId(1);
            
            return chainId;
        }

        protected async Task<Block> MineAsync(List<Transaction> txs)
        {
            return await Tester.MineAsync(txs);
        }
        
        protected  async Task<TransactionResult> ExecuteContractWithMiningAsync(Address contractAddress, string methodName, IMessage input)
        {
            return await Tester.ExecuteContractWithMiningAsync(contractAddress, methodName, input);
        }

        protected async Task<Transaction> GenerateTransactionAsync(Address contractAddress, string methodName, ECKeyPair ecKeyPair, IMessage input)
        {
            return ecKeyPair == null
                ? await Tester.GenerateTransactionAsync(contractAddress, methodName, input)
                : await Tester.GenerateTransactionAsync(contractAddress, methodName, ecKeyPair, input);
        }

        protected async Task<TransactionResult> GetTransactionResult(Hash txId)
        {
            return await Tester.GetTransactionResultAsync(txId);
        }

        protected async Task<ByteString> CallContractMethodAsync(Address contractAddress, string methodName,
            IMessage input)
        {
            return await Tester.CallContractMethodAsync(contractAddress, methodName, input);
        }

        protected byte[] GetFriendlyBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes.Skip(Array.FindIndex(bytes, Convert.ToBoolean)).ToArray();
        }

        protected SideChainCreationRequest CreateSideChainCreationRequest(long indexingPrice, long lockedTokenAmount, ByteString contractCode, IEnumerable<ResourceTypeBalancePair> resourceTypeBalancePairs = null)
        {
            var res = new SideChainCreationRequest
            {
                ContractCode = contractCode,
                IndexingPrice = indexingPrice,
                LockedTokenAmount = lockedTokenAmount
            };
            if(resourceTypeBalancePairs != null)
                res.ResourceBalances.AddRange(resourceTypeBalancePairs);
            return res;
        }

        protected async Task ApproveWithMiners(Hash proposalId)
        {
            var approveTransaction1 = await GenerateTransactionAsync(ParliamentAddress,
                nameof(ParliamentAuthContract.Approve), Tester.InitialMinerList[0], new Acs3.ApproveInput
                {
                    ProposalId = proposalId
                });
            var approveTransaction2 = await GenerateTransactionAsync(ParliamentAddress,
                nameof(ParliamentAuthContract.Approve), Tester.InitialMinerList[1], new Acs3.ApproveInput
                {
                    ProposalId = proposalId
                });
            await Tester.MineAsync(new List<Transaction> {approveTransaction1, approveTransaction2});
        }
    }
}