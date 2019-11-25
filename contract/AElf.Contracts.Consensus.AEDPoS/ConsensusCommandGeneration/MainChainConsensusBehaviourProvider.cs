using System.Linq;
using AElf.Sdk.CSharp;
using Google.Protobuf.WellKnownTypes;

// ReSharper disable once CheckNamespace
namespace AElf.Contracts.Consensus.AEDPoS
{
    // ReSharper disable once InconsistentNaming
    public partial class AEDPoSContract
    {
        public class MainChainConsensusBehaviourProvider : ConsensusBehaviourProviderBase
        {
            private readonly Timestamp _blockchainStartTimestamp;
            private readonly long _timeEachTerm;

            public MainChainConsensusBehaviourProvider(Round currentRound, string pubkey, int maximumBlocksCount,
                Timestamp currentBlockTime, Timestamp blockchainStartTimestamp, long timeEachTerm) : base(currentRound,
                pubkey, maximumBlocksCount, currentBlockTime)
            {
                _blockchainStartTimestamp = blockchainStartTimestamp;
                _timeEachTerm = timeEachTerm;
            }

            /// <summary>
            /// The blockchain start timestamp is incorrect during the first round,
            /// don't worry, we can return NextRound without hesitation.
            /// Besides, return only NextRound for single node running.
            /// </summary>
            /// <returns></returns>
            protected override AElfConsensusBehaviour GetConsensusBehaviourToTerminateCurrentRound() =>
                CurrentRound.RoundNumber == 1 || !CurrentRound.NeedToChangeTerm(_blockchainStartTimestamp,
                    CurrentRound.TermNumber, _timeEachTerm) || CurrentRound.RealTimeMinersInformation.Keys.Count == 1
                    ? AElfConsensusBehaviour.NextRound
                    : AElfConsensusBehaviour.NextTerm;
        }
    }
}