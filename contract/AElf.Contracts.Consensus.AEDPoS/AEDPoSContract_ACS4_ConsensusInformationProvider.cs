using System.Linq;
using Acs4;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Consensus.AEDPoS
{
    // ReSharper disable once InconsistentNaming
    public partial class AEDPoSContract
    {
        /// <summary>
        /// In this method, `Context.CurrentBlockTime` is the time one miner start request his next consensus command.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override ConsensusCommand GetConsensusCommand(BytesValue input)
        {
            _processingBlockMinerPubkey = input.Value.ToHex();

            if (Context.CurrentHeight < 2) return ConsensusCommandProvider.InvalidConsensusCommand;

            if (!TryToGetCurrentRoundInformation(out var currentRound))
                return ConsensusCommandProvider.InvalidConsensusCommand;

            if (!currentRound.IsInMinerList(_processingBlockMinerPubkey))
                return ConsensusCommandProvider.InvalidConsensusCommand;

            if (currentRound.RealTimeMinersInformation.Count != 1 &&
                currentRound.RoundNumber > 2 &&
                State.LatestPubkeyToTinyBlocksCount.Value != null &&
                State.LatestPubkeyToTinyBlocksCount.Value.Pubkey == _processingBlockMinerPubkey &&
                State.LatestPubkeyToTinyBlocksCount.Value.BlocksCount < 0)
                return GetConsensusCommand(AElfConsensusBehaviour.NextRound, currentRound, _processingBlockMinerPubkey,
                    Context.CurrentBlockTime);

            var blockchainStartTimestamp = GetBlockchainStartTimestamp();

            var behaviour = IsMainChain
                ? new MainChainConsensusBehaviourProvider(currentRound, _processingBlockMinerPubkey,
                        GetMaximumBlocksCount(),
                        Context.CurrentBlockTime, blockchainStartTimestamp, State.PeriodMinutes.Value)
                    .GetConsensusBehaviour()
                : new SideChainConsensusBehaviourProvider(currentRound, _processingBlockMinerPubkey,
                    GetMaximumBlocksCount(),
                    Context.CurrentBlockTime).GetConsensusBehaviour();

            Context.LogDebug(() =>
                $"{currentRound.ToString(_processingBlockMinerPubkey)}\nArranged behaviour: {behaviour.ToString()}");

            return behaviour == AElfConsensusBehaviour.Nothing
                ? ConsensusCommandProvider.InvalidConsensusCommand
                : GetConsensusCommand(behaviour, currentRound, _processingBlockMinerPubkey, Context.CurrentBlockTime);
        }

        public override BytesValue GetConsensusExtraData(BytesValue input)
        {
            return GetConsensusBlockExtraData(input);
        }

        public override TransactionList GenerateConsensusTransactions(BytesValue input)
        {
            var triggerInformation = new AElfConsensusTriggerInformation();
            triggerInformation.MergeFrom(input.Value);
            // Some basic checks.
            Assert(triggerInformation.Pubkey.Any(),
                "Data to request consensus information should contain pubkey.");

            var pubkey = triggerInformation.Pubkey;
            var consensusInformation = new AElfConsensusHeaderInformation();
            consensusInformation.MergeFrom(GetConsensusBlockExtraData(input, true).Value);
            var transactionList = GenerateTransactionListByExtraData(consensusInformation, pubkey);
            return transactionList;
        }

        public override ValidationResult ValidateConsensusBeforeExecution(BytesValue input)
        {
            var extraData = AElfConsensusHeaderInformation.Parser.ParseFrom(input.Value.ToByteArray());
            return ValidateBeforeExecution(extraData);
        }

        public override ValidationResult ValidateConsensusAfterExecution(BytesValue input)
        {
            var headerInformation = new AElfConsensusHeaderInformation();
            headerInformation.MergeFrom(input.Value);
            if (TryToGetCurrentRoundInformation(out var currentRound))
            {
                if (headerInformation.Behaviour == AElfConsensusBehaviour.UpdateValue)
                {
                    headerInformation.Round =
                        currentRound.RecoverFromUpdateValue(headerInformation.Round,
                            headerInformation.SenderPubkey.ToHex());
                }

                if (headerInformation.Behaviour == AElfConsensusBehaviour.TinyBlock)
                {
                    headerInformation.Round =
                        currentRound.RecoverFromTinyBlock(headerInformation.Round,
                            headerInformation.SenderPubkey.ToHex());
                }

                var isContainPreviousInValue = !currentRound.IsMinerListJustChanged;
                if (headerInformation.Round.GetHash(isContainPreviousInValue) !=
                    currentRound.GetHash(isContainPreviousInValue))
                {
                    Context.LogDebug(() => $"Round information of block header:\n{headerInformation.Round}");
                    Context.LogDebug(() => $"Round information of executing result:\n{currentRound}");
                    return new ValidationResult
                    {
                        Success = false, Message = "Current round information is different with consensus extra data."
                    };
                }
            }

            return new ValidationResult {Success = true};
        }

        private TransactionList GenerateTransactionListByExtraData(AElfConsensusHeaderInformation consensusInformation,
            ByteString pubkey)
        {
            var round = consensusInformation.Round;
            var behaviour = consensusInformation.Behaviour;
            switch (behaviour)
            {
                case AElfConsensusBehaviour.UpdateValue:
                    Context.LogDebug(() => $"Previous in value in extra data:{round.RealTimeMinersInformation[pubkey.ToHex()].PreviousInValue}");
                    return new TransactionList
                    {
                        Transactions =
                        {
                            GenerateTransaction(nameof(UpdateValue),
                                round.ExtractInformationToUpdateConsensus(pubkey.ToHex()))
                        }
                    };
                case AElfConsensusBehaviour.TinyBlock:
                    var minerInRound = round.RealTimeMinersInformation[pubkey.ToHex()];
                    return new TransactionList
                    {
                        Transactions =
                        {
                            GenerateTransaction(nameof(UpdateTinyBlockInformation),
                                new TinyBlockInput
                                {
                                    ActualMiningTime = minerInRound.ActualMiningTimes.Last(),
                                    ProducedBlocks = minerInRound.ProducedBlocks,
                                    RoundId = round.RoundIdForValidation
                                })
                        }
                    };
                case AElfConsensusBehaviour.NextRound:
                    return new TransactionList
                    {
                        Transactions =
                        {
                            GenerateTransaction(nameof(NextRound), round)
                        }
                    };
                case AElfConsensusBehaviour.NextTerm:
                    return new TransactionList
                    {
                        Transactions =
                        {
                            GenerateTransaction(nameof(NextTerm), round)
                        }
                    };
                default:
                    return new TransactionList();
            }
        }
    }
}