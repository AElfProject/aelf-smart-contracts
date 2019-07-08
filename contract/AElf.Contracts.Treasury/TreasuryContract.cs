using System.Collections.Generic;
using System.Linq;
using AElf.Contracts.Election;
using AElf.Contracts.MultiToken.Messages;
using AElf.Contracts.Profit;
using AElf.Contracts.TokenConverter;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Treasury
{
    /// <summary>
    /// The Treasury is the largest profit item in AElf main chain.
    /// Actually the Treasury is our Dividends Pool.
    /// Income of the Treasury is mining rewards
    /// (AEDPoS Contract will:
    /// 1. transfer ELF tokens to general ledger of Treasury every time we change term (7 days),
    /// the amount of ELF should be based on blocks produced during last term. 1,000,000 * 1250000 ELF,
    /// then release the Treasury;
    /// 2. Release Treasury)
    /// 3 sub profit items:
    /// (Mining Reward for Miners) - 3
    /// (Subsidy for Candidates / Backups) - 1
    /// (Welfare for Electors / Voters / Citizens) - 1
    ///
    /// 3 sub profit items for Mining Rewards:
    /// (Basic Rewards) - 4
    /// (Miner's Votes Weight) - 1
    /// (Re-Election Rewards) - 1
    ///
    /// 3 incomes:
    /// 1. 20% total supply of elf, from consensus contract
    /// 2. tx fees.
    /// 3. resource consumption of developer's contracts.
    /// </summary>
    public class TreasuryContract : TreasuryContractContainer.TreasuryContractBase
    {
        public override Empty InitialTreasuryContract(Empty input)
        {
            Assert(!State.Initialized.Value, "Already initialized.");

            State.ProfitContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ProfitContractSystemName);

            // Create profit items: `Treasury`, `CitizenWelfare`, `BackupSubsidy`, `MinerReward`,
            // `MinerBasicReward`, `MinerVotesWeightReward`, `ReElectedMinerReward`
            var profitItemNameList = new List<string>
            {
                "Treasury", "MinerReward", "Subsidy", "Welfare", "Basic Reward", "Votes Weight Reward",
                "Re-Election Reward"
            };
            for (var i = 0; i < 7; i++)
            {
                Context.LogDebug(() => profitItemNameList[i]);
                State.ProfitContract.CreateProfitItem.Send(new CreateProfitItemInput
                {
                    IsReleaseAllBalanceEveryTimeByDefault = true
                });
            }

            State.Initialized.Value = true;

            return new Empty();
        }

        public override Empty InitialMiningRewardProfitItem(Empty input)
        {
            var createdProfitIds = State.ProfitContract.GetCreatedProfitIds.Call(new GetCreatedProfitIdsInput
            {
                Creator = Context.Self
            }).ProfitIds;

            Assert(createdProfitIds.Count == 7, "Incorrect profit items count.");

            State.TreasuryHash.Value = createdProfitIds[0];
            State.RewardHash.Value = createdProfitIds[1];
            State.SubsidyHash.Value = createdProfitIds[2];
            State.WelfareHash.Value = createdProfitIds[3];
            State.BasicRewardHash.Value = createdProfitIds[4];
            State.VotesWeightRewardHash.Value = createdProfitIds[5];
            State.ReElectionRewardHash.Value = createdProfitIds[6];

            BuildTreasury();

            var treasuryVirtualAddress = Address.FromPublicKey(State.ProfitContract.Value.Value.Concat(
                createdProfitIds[0].Value.ToByteArray().CalculateHash()).ToArray());
            State.TreasuryVirtualAddress.Value = treasuryVirtualAddress;

            return new Empty();
        }

        public override Empty Release(ReleaseInput input)
        {
            if (State.AEDPoSContract.Value == null)
            {
                State.AEDPoSContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ConsensusContractSystemName);
            }

            Assert(
                Context.Sender == State.AEDPoSContract.Value,
                "Only AElf Consensus Contract can release profits from Treasury.");

            var releasingPeriodNumber = input.TermNumber;
            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.TreasuryHash.Value,
                Period = releasingPeriodNumber,
                Symbol = Context.Variables.NativeSymbol
            });

            ReleaseTreasurySubProfitItems(releasingPeriodNumber);
            UpdateTreasurySubItemsWeights(input.TermNumber);

            Context.LogDebug(() => "Leaving Release.");
            return new Empty();
        }

        public override Empty Donate(DonateInput input)
        {
            if (State.TokenContract.Value == null)
            {
                State.TokenContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            }

            var isNativeSymbol = input.Symbol == Context.Variables.NativeSymbol;

            State.TokenContract.TransferFrom.Send(new TransferFromInput
            {
                From = Context.Sender,
                To = isNativeSymbol
                    ? State.TreasuryVirtualAddress.Value
                    : Context.Self,
                Symbol = input.Symbol,
                Amount = input.Amount,
                Memo = "Donate to treasury."
            });

            if (input.Symbol != Context.Variables.NativeSymbol)
            {
                ConvertToNativeToken(input.Symbol, input.Amount);
            }

            return new Empty();
        }

        public override Empty DonateAll(DonateAllInput input)
        {
            if (State.TokenContract.Value == null)
            {
                State.TokenContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            }

            var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput
            {
                Symbol = input.Symbol,
                Owner = Context.Sender
            }).Balance;

            Donate(new DonateInput
            {
                Symbol = input.Symbol,
                Amount = balance
            });

            return new Empty();
        }

        private void ConvertToNativeToken(string symbol, long amount)
        {
            if (State.TokenConverterContract.Value == null)
            {
                State.TokenConverterContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.TokenConverterContractSystemName);
            }

            State.TokenConverterContract.Sell.Send(new SellInput
            {
                Symbol = symbol,
                Amount = amount
            });
            Context.SendInline(Context.Self, nameof(DonateAll), new DonateAllInput
            {
                Symbol = Context.Variables.NativeSymbol
            });
        }

        #region Private methods

        private void BuildTreasury()
        {
            // Register `CitizenWelfare` to `Treasury`
            State.ProfitContract.RegisterSubProfitItem.Send(new RegisterSubProfitItemInput
            {
                ProfitId = State.TreasuryHash.Value,
                SubProfitId = State.WelfareHash.Value,
                SubItemWeight = TreasuryContractConstants.CitizenWelfareWeight
            });

            // Register `BackupSubsidy` to `Treasury`
            State.ProfitContract.RegisterSubProfitItem.Send(new RegisterSubProfitItemInput
            {
                ProfitId = State.TreasuryHash.Value,
                SubProfitId = State.SubsidyHash.Value,
                SubItemWeight = TreasuryContractConstants.BackupSubsidyWeight
            });

            // Register `MinerReward` to `Treasury`
            State.ProfitContract.RegisterSubProfitItem.Send(new RegisterSubProfitItemInput
            {
                ProfitId = State.TreasuryHash.Value,
                SubProfitId = State.RewardHash.Value,
                SubItemWeight = TreasuryContractConstants.MinerRewardWeight
            });

            // Register `MinerBasicReward` to `MinerReward`
            State.ProfitContract.RegisterSubProfitItem.Send(new RegisterSubProfitItemInput
            {
                ProfitId = State.RewardHash.Value,
                SubProfitId = State.BasicRewardHash.Value,
                SubItemWeight = TreasuryContractConstants.BasicMinerRewardWeight
            });

            // Register `MinerVotesWeightReward` to `MinerReward`
            State.ProfitContract.RegisterSubProfitItem.Send(new RegisterSubProfitItemInput
            {
                ProfitId = State.RewardHash.Value,
                SubProfitId = State.VotesWeightRewardHash.Value,
                SubItemWeight = TreasuryContractConstants.VotesWeightRewardWeight
            });

            // Register `ReElectionMinerReward` to `MinerReward`
            State.ProfitContract.RegisterSubProfitItem.Send(new RegisterSubProfitItemInput
            {
                ProfitId = State.RewardHash.Value,
                SubProfitId = State.ReElectionRewardHash.Value,
                SubItemWeight = TreasuryContractConstants.ReElectionRewardWeight
            });
        }

        private void ReleaseTreasurySubProfitItems(long termNumber)
        {
            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.RewardHash.Value,
                Period = termNumber,
                Symbol = Context.Variables.NativeSymbol
            });

            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.SubsidyHash.Value,
                Period = termNumber,
                Symbol = Context.Variables.NativeSymbol
            });

            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.BasicRewardHash.Value,
                Period = termNumber,
                Symbol = Context.Variables.NativeSymbol
            });

            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.VotesWeightRewardHash.Value,
                Period = termNumber,
                Symbol = Context.Variables.NativeSymbol
            });

            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.ReElectionRewardHash.Value,
                Period = termNumber,
                Symbol = Context.Variables.NativeSymbol
            });

            // Citizen Welfare release should delay one term.
            // Voter voted during term x, can profit after term (x + 1).
            State.ProfitContract.ReleaseProfit.Send(new ReleaseProfitInput
            {
                ProfitId = State.WelfareHash.Value,
                Period = termNumber > 1 ? termNumber - 1 : -1,
                TotalWeight = State.CachedWelfareWeight.Value,
                Symbol = Context.Variables.NativeSymbol
            });

            State.CachedWelfareWeight.Value =
                State.ProfitContract.GetProfitItem.Call(State.WelfareHash.Value).TotalWeight;
        }

        private void UpdateTreasurySubItemsWeights(long termNumber)
        {
            var endPeriod = termNumber.Add(1);

            var treasuryVirtualAddress = State.TreasuryVirtualAddress.Value;

            if (State.ElectionContract.Value == null)
            {
                State.ElectionContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ElectionContractSystemName);
            }

            var victories = State.ElectionContract.GetVictories.Call(new Empty()).Value.Select(bs => bs.ToHex())
                .ToList();

            UpdateBasicMinerRewardWeights(endPeriod, victories);

            UpdateReElectionRewardWeights(endPeriod, treasuryVirtualAddress);

            UpdateVotesWeightRewardWeights(endPeriod, victories, treasuryVirtualAddress);
        }

        private void UpdateReElectionRewardWeights(long endPeriod, Address treasuryVirtualAddress)
        {
            var previousMiners = State.AEDPoSContract.GetPreviousRoundInformation.Call(new Empty())
                .RealTimeMinersInformation.Keys.ToList();

            var reElectionProfitAddWeights = new AddWeightsInput
            {
                ProfitId = State.ReElectionRewardHash.Value,
                EndPeriod = endPeriod
            };
            foreach (var previousMiner in previousMiners)
            {
                var continualAppointmentCount =
                    State.ElectionContract.GetCandidateInformation.Call(new StringInput {Value = previousMiner})
                        .ContinualAppointmentCount;
                var minerAddress = Address.FromPublicKey(ByteArrayHelper.FromHexString(previousMiner));
                reElectionProfitAddWeights.Weights.Add(new WeightMap
                {
                    Receiver = minerAddress,
                    Weight = continualAppointmentCount
                });
            }

            if (!reElectionProfitAddWeights.Weights.Any())
            {
                // Give this part of reward back to Treasury Virtual Address.
                reElectionProfitAddWeights.Weights.Add(new WeightMap
                {
                    Receiver = treasuryVirtualAddress,
                    Weight = 1
                });
            }

            State.ProfitContract.AddWeights.Send(reElectionProfitAddWeights);
        }

        private void UpdateVotesWeightRewardWeights(long endPeriod, IEnumerable<string> victories,
            Address treasuryVirtualAddress)
        {
            var votesWeightRewardProfitAddWeights = new AddWeightsInput
            {
                ProfitId = State.VotesWeightRewardHash.Value,
                EndPeriod = endPeriod
            };

            foreach (var victory in victories)
            {
                var obtainedVotes =
                    State.ElectionContract.GetCandidateVote.Call(new StringInput {Value = victory})
                        .ObtainedActiveVotedVotesAmount;
                var minerAddress = Address.FromPublicKey(ByteArrayHelper.FromHexString(victory));
                votesWeightRewardProfitAddWeights.Weights.Add(new WeightMap
                {
                    Receiver = minerAddress,
                    Weight = obtainedVotes
                });
            }

            if (!votesWeightRewardProfitAddWeights.Weights.Any())
            {
                // Give this part of reward back to Treasury Virtual Address.
                votesWeightRewardProfitAddWeights.Weights.Add(new WeightMap
                {
                    Receiver = treasuryVirtualAddress,
                    Weight = 1
                });
            }

            State.ProfitContract.AddWeights.Send(votesWeightRewardProfitAddWeights);
        }

        private void UpdateBasicMinerRewardWeights(long endPeriod, IEnumerable<string> victories)
        {
            var basicRewardProfitAddWeights = new AddWeightsInput
            {
                ProfitId = State.BasicRewardHash.Value,
                EndPeriod = endPeriod
            };
            var newMinerWeights = victories.Select(k => Address.FromPublicKey(k.ToByteString().ToByteArray()))
                .Select(a => new WeightMap {Receiver = a, Weight = 1});
            basicRewardProfitAddWeights.Weights.AddRange(newMinerWeights);
            // Manage weights of `MinerBasicReward`
            State.ProfitContract.AddWeights.Send(basicRewardProfitAddWeights);
        }

        #endregion

        public override GetWelfareRewardAmountSampleOutput GetWelfareRewardAmountSample(
            GetWelfareRewardAmountSampleInput input)
        {
            const long sampleAmount = 10000;
            var welfareHash = State.WelfareHash.Value;
            var output = new GetWelfareRewardAmountSampleOutput();
            var welfareItem = State.ProfitContract.GetProfitItem.Call(welfareHash);
            var releasedInformation = State.ProfitContract.GetReleasedProfitsInformation.Call(
                new GetReleasedProfitsInformationInput
                {
                    ProfitId = welfareHash,
                    Period = welfareItem.CurrentPeriod.Sub(1)
                });
            var totalWeight = releasedInformation.TotalWeight;
            var totalAmount = releasedInformation.ProfitsAmount;
            foreach (var lockTime in input.Value)
            {
                var weight = GetVotesWeight(sampleAmount, lockTime);
                output.Value.Add(totalAmount[Context.Variables.NativeSymbol].Mul(weight).Div(totalWeight));
            }

            return output;
        }

        public override SInt64Value GetCurrentWelfareReward(Empty input)
        {
            var welfareVirtualAddress = Context.ConvertVirtualAddressToContractAddress(State.WelfareHash.Value);
            return new SInt64Value
            {
                Value = State.TokenContract.GetBalance.Call(new GetBalanceInput
                {
                    Owner = welfareVirtualAddress,
                    Symbol = Context.Variables.NativeSymbol
                }).Balance
            };
        }

        public override SInt64Value GetCurrentTreasuryBalance(Empty input)
        {
            return new SInt64Value
            {
                Value = State.TokenContract.GetBalance.Call(new GetBalanceInput
                {
                    Owner = State.TreasuryVirtualAddress.Value,
                    Symbol = Context.Variables.NativeSymbol
                }).Balance
            };
        }

        private long GetVotesWeight(long votesAmount, long lockTime)
        {
            return lockTime.Div(86400).Div(270).Mul(votesAmount).Add(votesAmount.Mul(2).Div(3));
        }
    }
}