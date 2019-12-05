using System.Collections.Generic;
using System.Linq;
using Acs3;
using Acs7;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp.State;
using AElf.CSharp.Core.Utils;
using AElf.Types;
using AElf.Sdk.CSharp;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.CrossChain
{
    public partial class CrossChainContract
    {
        private const string ConsensusExtraDataName = "Consensus";
        
        /// <summary>
        /// Bind parent chain height together with self height.
        /// </summary>
        /// <param name="childHeight"></param>
        /// <param name="parentHeight"></param>
        private void BindParentChainHeight(long childHeight, long parentHeight)
        {
            Assert(State.ChildHeightToParentChainHeight[childHeight] == 0,
                $"Already bound at height {childHeight} with parent chain");
            State.ChildHeightToParentChainHeight[childHeight] = parentHeight;
        }

        private Hash ComputeRootWithTransactionStatusMerklePath(Hash txId, MerklePath path)
        {
            var txResultStatusRawBytes =
                EncodingHelper.GetBytesFromUtf8String(TransactionResultStatus.Mined.ToString());
            var hash = Hash.FromRawBytes(txId.ToByteArray().Concat(txResultStatusRawBytes).ToArray());
            return path.ComputeRootWithLeafNode(hash);
        }

        private Hash ComputeRootWithMultiHash(IEnumerable<Hash> nodes)
        {
            return BinaryMerkleTree.FromLeafNodes(nodes).Root;
        }
        
        /// <summary>
        /// Record merkle path of self chain block, which is from parent chain. 
        /// </summary>
        /// <param name="height"></param>
        /// <param name="path"></param>
        private void AddIndexedTxRootMerklePathInParentChain(long height, MerklePath path)
        {
            var existing = State.TxRootMerklePathInParentChain[height];
            Assert(existing == null,
                $"Merkle path already bound at height {height}.");
            State.TxRootMerklePathInParentChain[height] = path;
        }

        private void CreateSideChainToken(SideChainCreationRequest sideChainCreationRequest, 
            SideChainTokenInfo sideChainTokenInfo, int chainId)
        {
            TransferFrom(new TransferFromInput
            {
                From = Context.Origin,
                To = Context.Self,
                Amount = sideChainCreationRequest.LockedTokenAmount,
                Symbol = Context.Variables.NativeSymbol
            });
            State.IndexingBalance[chainId] = sideChainCreationRequest.LockedTokenAmount;
            
            CreateSideChainToken(sideChainTokenInfo, chainId);
        }

        private void UnlockTokenAndResource(SideChainInfo sideChainInfo)
        {
            // unlock token
            var chainId = sideChainInfo.SideChainId;
            var balance = State.IndexingBalance[chainId];
            if (balance != 0)
                Transfer(new TransferInput
                {
                    To = sideChainInfo.Proposer,
                    Amount = balance,
                    Symbol = Context.Variables.NativeSymbol
                });
            State.IndexingBalance[chainId] = 0;
        }
        
        private void AssertSideChainTokenInfo(SideChainTokenInfo sideChainTokenInfo)
        {
            Assert(
                !string.IsNullOrEmpty(sideChainTokenInfo.Symbol) 
                && !string.IsNullOrEmpty(sideChainTokenInfo.TokenName),
                "Invalid side chain token name,");
            Assert(sideChainTokenInfo.TotalSupply > 0, "Invalid side chain token supply.");
        }

        private void SetContractStateRequired(ContractReferenceState state, Hash contractSystemName)
        {
            if (state.Value != null)
                return;
            state.Value = Context.GetContractAddressByName(contractSystemName);
        }

        private void Transfer(TransferInput input)
        {
            SetContractStateRequired(State.TokenContract, SmartContractConstants.TokenContractSystemName);
            State.TokenContract.Transfer.Send(input);
        }

        private void TransferFrom(TransferFromInput input)
        {
            SetContractStateRequired(State.TokenContract, SmartContractConstants.TokenContractSystemName);
            State.TokenContract.TransferFrom.Send(input);
        }

        private void CreateSideChainToken(SideChainTokenInfo sideChainTokenInfo, int chainId)
        {
            SetContractStateRequired(State.TokenContract, SmartContractConstants.TokenContractSystemName);
            State.TokenContract.Create.Send(new CreateInput
            {
                TokenName = sideChainTokenInfo.TokenName,
                Decimals = sideChainTokenInfo.Decimals,
                IsBurnable = sideChainTokenInfo.IsBurnable,
                Issuer = Context.Origin,
                IssueChainId = chainId,
                IsTransferDisabled = false,
                Symbol = sideChainTokenInfo.Symbol,
                TotalSupply = sideChainTokenInfo.TotalSupply
            });
        }

        private TokenInfo GetNativeTokenInfo()
        {
            SetContractStateRequired(State.TokenContract, SmartContractConstants.TokenContractSystemName);
            return State.TokenContract.GetNativeTokenInfo.Call(new Empty());
        }

        private TokenInfo GetTokenInfo(string symbol)
        {
            SetContractStateRequired(State.TokenContract, SmartContractConstants.TokenContractSystemName);
            return State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
            {
                Symbol = symbol
            });
        }

        private TokenInfoList GetResourceTokenInfo()
        {
            SetContractStateRequired(State.TokenContract, SmartContractConstants.TokenContractSystemName);
            return State.TokenContract.GetResourceTokenInfo.Call(new Empty());
        }

        private MinerListWithRoundNumber GetCurrentMiners()
        {
            SetContractStateRequired(State.ConsensusContract, SmartContractConstants.ConsensusContractSystemName);
            var miners = State.ConsensusContract.GetCurrentMinerListWithRoundNumber.Call(new Empty());
            return miners;
        }

        // only for side chain
        private void UpdateCurrentMiners(ByteString bytes)
        {
            SetContractStateRequired(State.ConsensusContract, SmartContractConstants.ConsensusContractSystemName);
            State.ConsensusContract.UpdateConsensusInformation.Send(new ConsensusInformation {Value = bytes});
        }

        private Hash GetParentChainMerkleTreeRoot(long parentChainHeight)
        {
            return State.ParentChainTransactionStatusMerkleTreeRoot[parentChainHeight];
        }
        
        private Hash GetSideChainMerkleTreeRoot(long parentChainHeight)
        {
            var indexedSideChainData = State.IndexedSideChainBlockData[parentChainHeight];
            return ComputeRootWithMultiHash(
                indexedSideChainData.SideChainBlockData.Select(d => d.TransactionStatusMerkleTreeRoot));
        }
        
        private Hash GetCousinChainMerkleTreeRoot(long parentChainHeight)
        {
            return State.TransactionMerkleTreeRootRecordedInParentChain[parentChainHeight];
        }

        private Hash GetMerkleTreeRoot(int chainId, long parentChainHeight)
        {
            if (chainId == State.ParentChainId.Value)
            {
                // it is parent chain
                return GetParentChainMerkleTreeRoot(parentChainHeight);
            }

            if (State.SideChainInfo[chainId] != null)
            {
                // it is child chain
                return GetSideChainMerkleTreeRoot(parentChainHeight);
            }

            return GetCousinChainMerkleTreeRoot(parentChainHeight);
        }

        private Address GetOwnerAddress()
        {
            if (State.Owner.Value != null) 
                return State.Owner.Value;
            SetContractStateRequired(State.ParliamentAuthContract, SmartContractConstants.ParliamentAuthContractSystemName);
            Address organizationAddress = State.ParliamentAuthContract.GetDefaultOrganizationAddress.Call(new Empty());
            State.Owner.Value = organizationAddress;

            return State.Owner.Value;
        }
        
        private void AssertOwnerAuthority(Address address)
        {
            var owner = GetOwnerAddress();
            Assert(owner.Equals(address), "Not authorized to do this.");
        }

        private void AssertAddressIsParliamentContract(Address address)
        {
            SetContractStateRequired(State.ParliamentAuthContract,
                SmartContractConstants.ParliamentAuthContractSystemName);
            Assert(State.ParliamentAuthContract.Value == address, "Unauthorized behavior.");
        }
        
        private void AssertAddressIsCurrentMiner(Address address)
        {
            SetContractStateRequired(State.ConsensusContract, SmartContractConstants.ConsensusContractSystemName);
            var isCurrentMiner = State.ConsensusContract.IsCurrentMiner.Call(address).Value;
            Assert(isCurrentMiner, "No permission.");
        }
        

        private void AssertParentChainBlock(int parentChainId, long currentRecordedHeight, ParentChainBlockData parentChainBlockData)
        {
            Assert(parentChainId == parentChainBlockData.ChainId, "Wrong parent chain id.");
            Assert(currentRecordedHeight + 1 == parentChainBlockData.Height,
                $"Parent chain block info at height {currentRecordedHeight + 1} is needed, not {parentChainBlockData.Height}");
            Assert(parentChainBlockData.TransactionStatusMerkleTreeRoot != null,
                "Parent chain transaction status merkle tree root needed.");
        }

        private int GetChainId(long serialNumber)
        {
            return ChainHelper.GetChainId(serialNumber + Context.ChainId);
        }

        private void ProposeCrossChainBlockData(CrossChainBlockData crossChainBlockData, Address proposer)
        {
            SetContractStateRequired(State.ParliamentAuthContract,
                SmartContractConstants.ParliamentAuthContractSystemName);
            State.ParliamentAuthContract.CreateProposal.Send(new CreateProposalInput
            {
                Params = new RecordCrossChainDataInput
                {
                    ProposedCrossChainData = crossChainBlockData,
                    Proposer = proposer
                }.ToByteString(),
                ContractMethodName = nameof(RecordCrossChainData),
                ExpiredTime = Context.CurrentBlockTime.AddSeconds(CrossChainIndexingProposalExpirationTimeLimit),
                OrganizationAddress = State.Owner.Value,
                ToAddress = Context.Self,
                ProposalIdFeedbackMethod = nameof(FeedbackCrossChainIndexingProposalId)
            });
            
            State.PendingCrossChainIndexingProposal.Value = new CrossChainIndexingProposal
            {
                IndexingProposalFeedbackNeeded = true,
                Proposer = proposer
            };
            
            Context.Fire(new CrossChainIndexingProposed
            {
                ProposedCrossChainData = crossChainBlockData
            });
        }

        private ProposalOutput GetProposal(Hash proposalId)
        {
            SetContractStateRequired(State.ParliamentAuthContract,
                SmartContractConstants.ParliamentAuthContractSystemName);
            var proposal = State.ParliamentAuthContract.GetProposal.Call(proposalId);
            return proposal;
        }
        
        private void HandleIndexingProposal(Hash proposalId, Address proposer)
        {
            var proposal = GetProposal(proposalId);
            if (proposal.ToBeReleased)
                State.ParliamentAuthContract.Release.Send(proposal.ProposalId); //release if ready
            else if (proposal.ExpiredTime > Context.CurrentBlockTime)
                return;
            else
                BanCrossChainIndexingFromAddress(proposer); // ban the proposer if expired

            State.PendingCrossChainIndexingProposal.Value = new CrossChainIndexingProposal();
        }

        private void BanCrossChainIndexingFromAddress(Address address)
        {
            State.BannedMinerHeight[address] = Context.CurrentHeight;
        }
        
        private void AssertValidCrossChainIndexingProposer(Address proposer)
        {
            AssertAddressIsCurrentMiner(proposer);
            var bannedHeight = State.BannedMinerHeight[proposer];
            var permitted = bannedHeight == 0 ||
                            bannedHeight + CrossChainIndexingBannedBlockHeightInterval > Context.CurrentHeight;
            Assert(permitted, $"Cross chain indexing is banned for address {proposer}");
            if (bannedHeight != 0)
                State.BannedMinerHeight.Remove(proposer); // lift the ban
        }

        private void AssertValidCrossChainDataBeforeIndexing(CrossChainBlockData crossChainBlockData)
        {
            Assert(
                crossChainBlockData.ParentChainBlockData.Count > 0 || crossChainBlockData.SideChainBlockData.Count > 0,
                "Empty cross chain data proposed.");
            Assert(ValidateSideChainBlockData(crossChainBlockData.SideChainBlockData)
                   && ValidateParentChainBlockData(crossChainBlockData.ParentChainBlockData),
                "Invalid cross chain data to be indexed.");
        }

        private bool ValidateSideChainBlockData(IEnumerable<SideChainBlockData> sideChainBlockData)
        {
            var groupResult = sideChainBlockData.GroupBy(data => data.ChainId, data => data);
            
            foreach (var group in groupResult)
            {
                var chainId = group.Key;
                var info = State.SideChainInfo[chainId];
                if (info == null || info.SideChainStatus != SideChainStatus.Active)
                    return false;
                var currentSideChainHeight = State.CurrentSideChainHeight[chainId];
                var target = currentSideChainHeight != 0
                    ? currentSideChainHeight + 1
                    : Constants.GenesisBlockHeight;
                // indexing fee
                var indexingPrice = info.SideChainCreationRequest.IndexingPrice;
                var lockedToken = State.IndexingBalance[chainId];
                foreach (var blockData in group)
                {
                    var sideChainHeight = blockData.Height;
                    if (target != sideChainHeight)
                        return false;
                    target++;
                }

                if (indexingPrice.Mul(group.Count()) > lockedToken)
                    return false;
            }

            return true;
        }

        private bool ValidateParentChainBlockData(IEnumerable<ParentChainBlockData> parentChainBlockData)
        {
            var parentChainId = State.ParentChainId.Value;
            var currentHeight = State.CurrentParentChainHeight.Value;
            foreach (var blockData in parentChainBlockData)
            {
                if (parentChainId != blockData.ChainId || currentHeight + 1 != blockData.Height ||
                    blockData.TransactionStatusMerkleTreeRoot == null)
                    return false;
                if (blockData.IndexedMerklePath.Any(indexedBlockInfo =>
                    State.ChildHeightToParentChainHeight[indexedBlockInfo.Key] != 0 ||
                    State.TxRootMerklePathInParentChain[indexedBlockInfo.Key] != null))
                    return false;
                
                currentHeight += 1;
            }

            return true;
        }
    }
}