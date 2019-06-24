using System.Collections.Generic;
using System.Threading.Tasks;
using Acs7;
using AElf.Contracts.CrossChain;
using AElf.Contracts.MultiToken.Messages;
using AElf.Contracts.ParliamentAuth;
using AElf.Cryptography;
using AElf.Types;
using Google.Protobuf;
using Xunit;

namespace AElf.Contract.CrossChain.Tests
{
    /*
     * Todo:
     * Side chain creation proposal is disable.
     * Lock resource is disable.
     * Token amount to check.
    */
//    public class SideChainLifeTimeManagementTest : CrossChainContractTestBase
//    {
//        [Fact]
//        public async Task Request_SideChain_Creation()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var balanceResult = GetBalanceOutput.Parser.ParseFrom(await Tester.CallContractMethodAsync(TokenContractAddress, 
//                nameof(TokenContractContainer.TokenContractStub.GetBalance),
//                new GetBalanceInput
//                {
//                    Owner = Address.FromPublicKey(Tester.KeyPair.PublicKey),
//                    Symbol = "ELF"
//                }));
//            Assert.Equal(_balanceOfStarter, balanceResult.Balance);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var txResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Mined);
//            var expectedChainId = ChainHelpers.GetChainId(1);
//            Assert.Equal(expectedChainId, RequestChainCreationOutput.Parser.ParseFrom(txResult.ReturnValue).ChainId);
//        }
//
//        [Fact]
//        public async Task Request_SideChain_CreationFailed()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10; 
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            {
//                sideChainCreationRequest.LockedTokenAmount = 0;
//                var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                    sideChainCreationRequest);
//                var status = txResult.Status;
//                Assert.True(status == TransactionResultStatus.Failed);
//                var checkErrorMessage = txResult.Error.Contains("Invalid chain creation request.");
//                Assert.True(checkErrorMessage);
//            }
//            {
//                sideChainCreationRequest.LockedTokenAmount = 1;
//                sideChainCreationRequest.IndexingPrice = 2;
//                var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                    sideChainCreationRequest);
//                var status = txResult.Status;
//                Assert.True(status == TransactionResultStatus.Failed);
//                var checkErrorMessage = txResult.Error.Contains("Invalid chain creation request.");
//                Assert.True(checkErrorMessage);
//            }
//            {
//                sideChainCreationRequest.LockedTokenAmount = 10;
//                sideChainCreationRequest.IndexingPrice = 1;
//                sideChainCreationRequest.ContractCode = ByteString.Empty;
//                var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                    sideChainCreationRequest);
//                var status = txResult.Status;
//                Assert.True(status == TransactionResultStatus.Failed);
//                var checkErrorMessage = txResult.Error.Contains("Invalid chain creation request.");
//                Assert.True(checkErrorMessage);
//            }
//        }
//
//        [Fact]
//        public async Task Request_SideChain_Creation_WithoutApprove()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//        }
//        
//        [Fact]
//        public async Task Request_SideChain_Creation_WithoutEnoughToken()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 1000_000L;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//        }
//        
//        [Fact]
//        public async Task Request_SideChain_Creation_Twice()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 20;
//            await ApproveBalanceAsync(lockedTokenAmount * 2);
//
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var tx = await GenerateTransactionAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                null,
//                sideChainCreationRequest);
//            await MineAsync(new List<Transaction> {tx});
//            
//            var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var expectedChainId = ChainHelpers.GetChainId(2);
//            Assert.Equal(expectedChainId, RequestChainCreationOutput.Parser.ParseFrom(txResult.ReturnValue).ChainId);
//        }
//
//        [Fact]
//        public async Task Withdraw_ChainCreation()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            
//            var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(txResult.ReturnValue).ChainId;
//            
//            var transactionResult = await ExecuteContractWithMiningAsync(
//                    CrossChainContractAddress,
//                    nameof(CrossChainContractContainer.CrossChainContractStub.WithdrawRequest),
//                    new SInt32Value()
//                    {
//                        Value = chainId
//                    });
//            var status = transactionResult.Status;
//            Assert.True(status == TransactionResultStatus.Mined);
//            
//            var chainStatus =SInt32Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetChainStatus),
//                new SInt32Value()
//                {
//                    Value = chainId
//                })).Value;
//            Assert.Equal(4, chainStatus);
//        }
//        
//        [Fact]
//        public async Task Withdraw_ChainCreation_WithWrongSender()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            var tx =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(tx.ReturnValue).ChainId;
//            
//            var ecKeyPair = CryptoHelpers.GenerateKeyPair();
//            var other = Tester.CreateNewContractTester(ecKeyPair);
//            var txResult =
//                await other.ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                    nameof(CrossChainContractContainer.CrossChainContractStub.WithdrawRequest),
//                    new SInt32Value() {Value = chainId});
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//            var checkErrorMessage = txResult.Error.Contains("Authentication failed.");
//            Assert.True(checkErrorMessage);
//        }
//        
//        [Fact]
//        public async Task Withdraw_ChainCreation_ChainNotExist()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            
//            var tx = await GenerateTransactionAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation), null,
//                sideChainCreationRequest);
//            await MineAsync(new List<Transaction> {tx});
//            var notExistChainId = ChainHelpers.GetChainId(5);
//            var txResult =
//                await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                    nameof(CrossChainContractContainer.CrossChainContractStub.WithdrawRequest),
//                    new SInt32Value()
//                    {
//                        Value = notExistChainId
//                    });
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//        }
//        
//        [Fact]
//        public async Task Withdraw_ChainCreation_WrongStatus()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            var requestTxResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            await ApproveWithMinersAsync(RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ProposalId);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ChainId;
//            
//            var txResult =
//                await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                    nameof(CrossChainContractContainer.CrossChainContractStub.WithdrawRequest),
//                    new SInt32Value()
//                    {
//                        Value = chainId
//                    });
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//        }
//
//        [Fact]
//        public async Task Create_SideChain()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            
//            var requestTxResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ChainId;
//            var approveTransaction1 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[0], new Acs3.ApproveInput
//                {
//                    ProposalId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ProposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction1});
//            var tx1Result = await GetTransactionResultAsync(approveTransaction1.GetHash());
//            Assert.True(tx1Result.Status == TransactionResultStatus.Mined);
//            var approveTransaction2 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[1], new Acs3.ApproveInput
//                {
//                    ProposalId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ProposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction2});
//            var tx2Result = await GetTransactionResultAsync(approveTransaction2.GetHash());
//            Assert.True(tx2Result.Status == TransactionResultStatus.Mined);
//            
//            var chainStatus = SInt32Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetChainStatus), new SInt32Value{Value = chainId})).Value;
//            Assert.True(chainStatus == (int) SideChainStatus.Active);
//        }
//        
//        [Fact]
//        public async Task CheckLockedBalance()
//        {
//            await InitializeCrossChainContractAsync();
//            long lockedTokenAmount = 10;
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var requestTransaction = await GenerateTransactionAsync(CrossChainContractAddress, nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),null,
//                sideChainCreationRequest);
//            await MineAsync(new List<Transaction> {requestTransaction});
//            var chainId = ChainHelpers.GetChainId(1);
//            var creationTransaction = await GenerateTransactionAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.CreateSideChain),
//                null, new SInt32Value()
//                {
//                    Value = chainId
//                });
//            await MineAsync(new List<Transaction> {creationTransaction});
//            var balance = SInt64Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.LockedBalance),
//                new SInt32Value()
//                {
//                    Value = chainId
//                })).Value;
//            Assert.Equal(10, balance);
//        }
//        
//        [Fact]
//        public async Task Create_SideChain_NotAuthorized()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var txResult = await Tester.ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation), sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(txResult.ReturnValue).ChainId;
//            var transactionResult =
//                await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                    nameof(CrossChainContractContainer.CrossChainContractStub.CreateSideChain),
//                    new SInt32Value()
//                {
//                    Value = chainId
//                });
//            var status = transactionResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//            var checkErrorMessage = transactionResult.Error.Contains("Not authorized to do this.");
//            Assert.True(checkErrorMessage);
//        }
//
//        [Fact]
//        public async Task Create_SideChain_ChainNotExit()
//        {
//            await InitializeCrossChainContractAsync();
//            //create proposal  
//            var chainId = ChainHelpers.GetChainId(5);
//            var proposalId = await CreateProposalAsync(chainId, "CreateSideChain");
//            var approveTransaction1 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[0], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction1});
//            var tx1Result = await GetTransactionResultAsync(approveTransaction1.GetHash());
//            Assert.True(tx1Result.Status == TransactionResultStatus.Mined);
//            var approveTransaction2 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[1], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction2});
//            var tx2Result = await GetTransactionResultAsync(approveTransaction2.GetHash());
//            Assert.True(tx2Result.Status == TransactionResultStatus.Failed);
//            var checkErrorMessage = tx2Result.Error.Contains("Side chain creation request not found.");
//            Assert.True(checkErrorMessage);
//        }
//        
//        [Fact]
//        public async Task Create_SideChain_WrongStatus()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var txResult = await Tester.ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation), sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(txResult.ReturnValue).ChainId;
//            var proposalId1 = RequestChainCreationOutput.Parser.ParseFrom(txResult.ReturnValue).ProposalId;
//            await ApproveWithMinersAsync(proposalId1);
//       
//            var proposalId2 = await CreateProposalAsync(chainId, "CreateSideChain");
//            var approveTransaction1 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[0], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId2
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction1});
//            var tx1Result = await GetTransactionResultAsync(approveTransaction1.GetHash());
//            Assert.True(tx1Result.Status == TransactionResultStatus.Mined);
//            var approveTransaction2 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[1], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId2
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction2});
//            var tx2Result = await GetTransactionResultAsync(approveTransaction2.GetHash());
//            Assert.True(tx2Result.Status == TransactionResultStatus.Failed);
//            var checkErrorMessage = tx2Result.Error.Contains("Side chain creation request not found.");
//            Assert.True(checkErrorMessage);
//        }
//        
//        [Fact]
//        public async Task Request_SideChain_Disposal()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            var requestTxResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            await ApproveWithMinersAsync(RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ProposalId);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ChainId;
//            
//            var txResult =
//                await ExecuteContractWithMiningAsync(CrossChainContractAddress, 
//                    nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainDisposal), 
//                    new SInt32Value()
//                    {
//                        Value = chainId
//                    });
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Mined);
//        }
//
//        [Fact]
//        public async Task Request_SideChain_Disposal_NotAuthorized()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            var requestTxResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            await ApproveWithMinersAsync(RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ProposalId);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ChainId;
//            
//            var ecKeyPair = CryptoHelpers.GenerateKeyPair();
//            var other = Tester.CreateNewContractTester(ecKeyPair);
//            var txResult =
//                await other.ExecuteContractWithMiningAsync(CrossChainContractAddress, 
//                    nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainDisposal), 
//                    new SInt32Value()
//                    {
//                        Value = chainId
//                    });
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//            var checkErrorMessage = txResult.Error.Contains("Not authorized to dispose.");
//            Assert.True(checkErrorMessage);
//        }
//
//        [Fact]
//        public async Task Request_SideChain_Disposal_NotFound()
//        {
//            await InitializeCrossChainContractAsync();
//            var txResult =
//                await ExecuteContractWithMiningAsync(CrossChainContractAddress, 
//                    nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainDisposal), 
//                    new SInt32Value()
//                    {
//                        Value = ChainHelpers.GetChainId(5)
//                    });
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//            var checkErrorMessage = txResult.Error.Contains("Side chain not found");
//            Assert.True(checkErrorMessage);
//        }
//
//        [Fact]
//        public async Task Request_SideChain_Disposal_WrongStatus()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//            var requestTxResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(requestTxResult.ReturnValue).ChainId;
//            
//            var txResult =
//                await ExecuteContractWithMiningAsync(CrossChainContractAddress, 
//                    nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainDisposal), 
//                    new SInt32Value()
//                    {
//                        Value = chainId
//                    });
//            var status = txResult.Status;
//            Assert.True(status == TransactionResultStatus.Failed);
//            var checkErrorMessage = txResult.Error.Contains("Side chain not found");
//            Assert.True(checkErrorMessage);
//        }
//        
//        [Fact]
//        public async Task Dispose_SideChain()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var requestChainCreationResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            await ApproveWithMinersAsync(RequestChainCreationOutput.Parser.ParseFrom(requestChainCreationResult.ReturnValue).ProposalId);
//            var chainId = ChainHelpers.GetChainId(1);
//            
//            var requestChainDisposalResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainDisposal), 
//                new SInt32Value()
//                {
//                    Value = chainId
//                });
//            await ApproveWithMinersAsync(Hash.Parser.ParseFrom(requestChainDisposalResult.ReturnValue));
//            var chainStatus = SInt32Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetChainStatus), new SInt32Value{Value = chainId})).Value;
//            Assert.True(chainStatus == (int) SideChainStatus.Terminated);
//            
//            var status =SInt32Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetChainStatus),
//                new SInt32Value()
//                {
//                    Value = chainId
//                })).Value;
//            Assert.Equal(4, status);
//        }
//
//        [Fact]
//        public async Task Dispose_SideChain_NotAuthorized()
//        {
//            await InitializeCrossChainContractAsync();
//            var txResult = await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.DisposeSideChain),new SInt32Value{Value = ChainHelpers.GetChainId(1)});
//            Assert.True(txResult.Status == TransactionResultStatus.Failed);
//            var checkErrorMessage = txResult.Error.Contains("Not authorized to do this.");
//            Assert.True(checkErrorMessage);
//        }
//
//        [Fact]
//        public async Task Dispose_SideChain_NotExistedChain()
//        {
//            await InitializeCrossChainContractAsync();
//            //create proposal
//            var chainId = ChainHelpers.GetChainId(5);
//            var proposalId = await CreateProposalAsync(chainId, "DisposeSideChain");
//            
//            var approveTransaction1 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[0], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction1});
//            var tx1Result = await GetTransactionResultAsync(approveTransaction1.GetHash());
//            Assert.True(tx1Result.Status == TransactionResultStatus.Mined);
//            var approveTransaction2 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[1], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction2});
//            var tx2Result = await GetTransactionResultAsync(approveTransaction2.GetHash());
//            Assert.True(tx2Result.Status == TransactionResultStatus.Failed);
//            var checkErrorMessage = tx2Result.Error.Contains("Not existed side chain.");
//            Assert.True(checkErrorMessage);
//        }
//
//        [Fact]
//        public async Task Dispose_SideChain_WrongStatus()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var requestChainCreationResult =await ExecuteContractWithMiningAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),
//                sideChainCreationRequest);
//            var chainId = RequestChainCreationOutput.Parser.ParseFrom(requestChainCreationResult.ReturnValue).ChainId;
//            var proposalId = await CreateProposalAsync(chainId, "DisposeSideChain");
//            
//            var approveTransaction1 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[0], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction1});
//            var tx1Result = await GetTransactionResultAsync(approveTransaction1.GetHash());
//            Assert.True(tx1Result.Status == TransactionResultStatus.Mined);
//            var approveTransaction2 = await GenerateTransactionAsync(ParliamentAddress,
//                nameof(ParliamentAuthContractContainer.ParliamentAuthContractStub.Approve), Tester.InitialMinerList[1], new Acs3.ApproveInput
//                {
//                    ProposalId = proposalId
//                });
//            await Tester.MineAsync(new List<Transaction> {approveTransaction2});
//            var tx2Result = await GetTransactionResultAsync(approveTransaction2.GetHash());
//            Assert.True(tx2Result.Status == TransactionResultStatus.Failed);
//            var checkErrorMessage = tx2Result.Error.Contains("Unable to dispose this side chain.");
//            Assert.True(checkErrorMessage);
//        }
//
//        [Fact]
//        public async Task GetChainStatus_Review()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var tx1 = await GenerateTransactionAsync(CrossChainContractAddress,
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation),null, sideChainCreationRequest);
//            await MineAsync(new List<Transaction> {tx1});
//            var chainId = ChainHelpers.GetChainId(1);
//            
//            var status =SInt32Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetChainStatus),
//                new SInt32Value()
//                {
//                    Value = chainId
//                })).Value;
//            Assert.Equal(1, status);
//        }
//       
//        [Fact]
//        public async Task GetChainStatus_ChainNotExist()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            
//            var chainId = ChainHelpers.GetChainId(1);
//            var status = await CallContractMethodAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetChainStatus), 
//                new SInt32Value()
//                {
//                    Value = chainId
//                });
//            Assert.Equal(ByteString.Empty, status);
//        }
//
//        [Fact]
//        public async Task Get_SideChain_Height()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var sideChainCreationRequest = CreateSideChainCreationRequest(1, lockedTokenAmount, ByteString.CopyFromUtf8("Test"));
//
//            var tx1 = await GenerateTransactionAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.RequestChainCreation), null, sideChainCreationRequest);
//            await MineAsync(new List<Transaction> {tx1});
//            var chainId = ChainHelpers.GetChainId(1);
//            await ExecuteContractWithMiningAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.CreateSideChain),
//                new SInt32Value()
//                {
//                    Value = chainId
//                });
//            var height =SInt64Value.Parser.ParseFrom(await CallContractMethodAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetSideChainHeight),
//                new SInt32Value()
//                {
//                    Value = chainId
//                })).Value;
//            Assert.True(height == 0);
//        }
//        
//        [Fact]
//        public async Task Get_SideChain_Height_ChainNotExist()
//        {
//            long lockedTokenAmount = 10;           
//            await InitializeCrossChainContractAsync();
//            await ApproveBalanceAsync(lockedTokenAmount);
//            var chainId = ChainHelpers.GetChainId(1);
//            var height = await CallContractMethodAsync(CrossChainContractAddress, 
//                nameof(CrossChainContractContainer.CrossChainContractStub.GetSideChainHeight),
//                new SInt32Value()
//                {
//                    Value = chainId
//                });
//            Assert.Equal(ByteString.Empty, height);
//        }
//    }
}