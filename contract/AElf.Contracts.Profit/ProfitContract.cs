﻿using System;
using System.Linq;
using AElf.Contracts.MultiToken.Messages;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Profit
{
    public partial class ProfitContract : ProfitContractContainer.ProfitContractBase
    {
        /// <summary>
        /// Create a ProfitItem
        /// At the first time,the profitItem's id is unknown,it may create by transaction id and createdProfitIds;
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Hash CreateProfitItem(CreateProfitItemInput input)
        {
            if (State.TokenContract.Value == null)
            {
                State.TokenContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            }

            if (input.ProfitReceivingDuePeriodCount == 0)
            {
                input.ProfitReceivingDuePeriodCount = ProfitContractConsts.DefaultProfitReceivingDuePeriodCount;
            }

            var profitId = Context.TransactionId;
            var createdProfitIds = State.CreatedProfitIds[Context.Sender]?.ProfitIds;
            if (createdProfitIds != null && createdProfitIds.Contains(profitId))
            {
                profitId = Hash.FromTwoHashes(profitId, createdProfitIds.Last());
            }

            State.ProfitItemsMap[profitId] = new ProfitItem
            {
                ProfitId = profitId,
                VirtualAddress = Context.ConvertVirtualAddressToContractAddress(profitId),
                Creator = Context.Sender,
                ProfitReceivingDuePeriodCount = input.ProfitReceivingDuePeriodCount,
                CurrentPeriod = 1,
                IsReleaseAllBalanceEverytimeByDefault = input.IsReleaseAllBalanceEveryTimeByDefault
            };

            var createdProfitItems = State.CreatedProfitIds[Context.Sender];
            if (createdProfitItems == null)
            {
                createdProfitItems = new CreatedProfitIds
                {
                    ProfitIds = {profitId}
                };
            }
            else
            {
                createdProfitItems.ProfitIds.Add(profitId);
            }

            State.CreatedProfitIds[Context.Sender] = createdProfitItems;

            Context.LogDebug(() => $"Created profit item {State.ProfitItemsMap[profitId]}");
            return profitId;
        }

        public override Empty SetTreasuryProfitId(Hash input)
        {
            Assert(State.TreasuryProfitId.Value == null, "Treasury profit id already set.");
            State.TreasuryProfitId.Value = input;
            return new Empty();
        }

        /// <summary>
        /// Register a SubProfitItem,binding to a ProfitItem as child.
        /// Then add the father ProfitItem's weight.
        /// </summary>
        /// <param name="input">RegisterSubProfitItemInput</param>
        /// <returns></returns>
        public override Empty RegisterSubProfitItem(RegisterSubProfitItemInput input)
        {
            Assert(input.ProfitId != input.SubProfitId, "Two profit items cannot be same.");
            Assert(input.SubItemWeight > 0, "Weight of sub profit item should greater than 0.");

            var profitItem = State.ProfitItemsMap[input.ProfitId];
            Assert(profitItem != null, "Profit item not found.");

            if (profitItem == null)
            {
                return new Empty();
            }

            if (profitItem.IsTreasuryProfitItem)
            {
                Assert(
                    profitItem.TotalWeight.Add(input.SubItemWeight) <
                    ProfitContractConsts.TreasuryProfitItemMaximumWeight,
                    $"Treasury profit item weight cannot greater than {ProfitContractConsts.TreasuryProfitItemMaximumWeight}.");
            }

            Assert(Context.Sender == profitItem.Creator, "Only creator can do the registration.");

            var subProfitItemId = input.SubProfitId;
            var subProfitItem = State.ProfitItemsMap[subProfitItemId];
            Assert(subProfitItem != null, "Sub profit item not found.");

            if (subProfitItem == null)
            {
                return new Empty();
            }

            var subItemVirtualAddress = Context.ConvertVirtualAddressToContractAddress(subProfitItemId);
            AddWeight(new AddWeightInput
            {
                ProfitId = input.ProfitId,
                Weight = input.SubItemWeight,
                Receiver = subItemVirtualAddress,
                EndPeriod = long.MaxValue
            });


            // Add a sub profit item.
            profitItem.SubProfitItems.Add(new SubProfitItem
            {
                ProfitId = input.SubProfitId,
                Weight = input.SubItemWeight
            });
            profitItem.TotalWeight = profitItem.TotalWeight.Add(input.SubItemWeight);
            State.ProfitItemsMap[input.ProfitId] = profitItem;

            return new Empty();
        }

        public override Empty CancelSubProfitItem(CancelSubProfitItemInput input)
        {
            Assert(input.ProfitId != input.SubProfitId, "Two profit items cannot be same.");

            var profitItem = State.ProfitItemsMap[input.ProfitId];
            Assert(profitItem != null, "Profit item not found.");

            if (profitItem == null)
            {
                return new Empty();
            }

            Assert(input.SubItemCreator == profitItem.Creator, "Only creator can do the Cancel.");

            var subProfitItemId = input.SubProfitId;
            var subProfitItem = State.ProfitItemsMap[subProfitItemId];
            Assert(subProfitItem != null, "Sub profit item not found.");

            if (subProfitItem == null)
            {
                return new Empty();
            }

            var subItemVirtualAddress = Context.ConvertVirtualAddressToContractAddress(subProfitItemId);
            SubWeight(new SubWeightInput()
            {
                ProfitId = input.ProfitId,
                Receiver = subItemVirtualAddress
            });


            profitItem.SubProfitItems.Remove(profitItem.SubProfitItems.Single(d => d.ProfitId == input.SubProfitId));
            State.ProfitItemsMap[input.ProfitId] = profitItem;

            return new Empty();
        }

        public override Empty AddWeight(AddWeightInput input)
        {
            Assert(input.ProfitId != null, "Invalid profit id.");
            Assert(input.Receiver != null, "Invalid receiver address.");
            Assert(input.Weight >= 0, "Invalid weight.");

            if (input.EndPeriod == 0)
            {
                // Which means this profit receiver will never expired.
                input.EndPeriod = long.MaxValue;
            }

            var profitId = input.ProfitId;
            var profitItem = State.ProfitItemsMap[profitId];

            Assert(profitItem != null, "Profit item not found.");

            if (profitItem == null)
            {
                return new Empty();
            }

            Context.LogDebug(() =>
                $"Entered AddWeight. {input.ProfitId}, {input.EndPeriod}, current period {profitItem.ProfitId}");

            if (profitItem.IsTreasuryProfitItem)
            {
                Assert(
                    profitItem.TotalWeight.Add(input.Weight) <
                    ProfitContractConsts.TreasuryProfitItemMaximumWeight,
                    $"Treasury profit item weight cannot greater than {ProfitContractConsts.TreasuryProfitItemMaximumWeight}.");
            }

            Assert(input.EndPeriod >= profitItem.CurrentPeriod, "Invalid end period.");

            profitItem.TotalWeight = profitItem.TotalWeight.Add(input.Weight);

            State.ProfitItemsMap[profitId] = profitItem;

            var profitDetail = new ProfitDetail
            {
                StartPeriod = profitItem.CurrentPeriod,
                EndPeriod = input.EndPeriod,
                Weight = input.Weight,
            };

            var currentProfitDetails = State.ProfitDetailsMap[profitId][input.Receiver];
            if (currentProfitDetails == null)
            {
                currentProfitDetails = new ProfitDetails
                {
                    Details = {profitDetail}
                };
            }
            else
            {
                currentProfitDetails.Details.Add(profitDetail);
            }

            // Remove details too old.
            foreach (var detail in currentProfitDetails.Details.Where(
                d => d.EndPeriod != long.MaxValue && d.LastProfitPeriod >= d.EndPeriod &&
                     d.EndPeriod.Add(profitItem.ProfitReceivingDuePeriodCount) < profitItem.CurrentPeriod))
            {
                currentProfitDetails.Details.Remove(detail);
            }

            State.ProfitDetailsMap[profitId][input.Receiver] = currentProfitDetails;

            Context.LogDebug(() => $"Add {input.Weight} weights to profit item {input.ProfitId.ToHex()}");

            return new Empty();
        }

        public override Empty SubWeight(SubWeightInput input)
        {
            Assert(input.ProfitId != null, "Invalid profit id.");
            Assert(input.Receiver != null, "Invalid receiver address.");

            var profitItem = State.ProfitItemsMap[input.ProfitId];

            Assert(profitItem != null, "Profit item not found.");

            var currentDetail = State.ProfitDetailsMap[input.ProfitId][input.Receiver];

            if (profitItem == null || currentDetail == null)
            {
                return new Empty();
            }

            var expiryDetails = currentDetail.Details
                .Where(d => d.EndPeriod < profitItem.CurrentPeriod).ToList();

            if (!expiryDetails.Any())
            {
                return new Empty();
            }

            var weights = expiryDetails.Sum(d => d.Weight);
            foreach (var profitDetail in expiryDetails)
            {
                currentDetail.Details.Remove(profitDetail);
            }

            State.ProfitDetailsMap[input.ProfitId][input.Receiver] = currentDetail;

            // TODO: Recover this after key deletion in contract feature impled.
//            if (currentDetail.Details.Count != 0)
//            {
//                State.ProfitDetailsMap[input.ProfitId][input.Receiver] = currentDetail;
//            }
//            else
//            {
//                State.ProfitDetailsMap[input.ProfitId][input.Receiver] = null;
//            }

            profitItem.TotalWeight -= weights;
            State.ProfitItemsMap[input.ProfitId] = profitItem;

            return new Empty();
        }

        public override Empty AddWeights(AddWeightsInput input)
        {
            foreach (var map in input.Weights)
            {
                AddWeight(new AddWeightInput
                {
                    ProfitId = input.ProfitId,
                    Receiver = map.Receiver,
                    Weight = map.Weight,
                    EndPeriod = input.EndPeriod
                });
            }

            return new Empty();
        }

        public override Empty SubWeights(SubWeightsInput input)
        {
            foreach (var receiver in input.Receivers)
            {
                SubWeight(new SubWeightInput {ProfitId = input.ProfitId, Receiver = receiver});
            }

            return new Empty();
        }

        /// <summary>
        /// Will burn/destroy a certain amount of profits if `input.Period` is less than 0.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Empty ReleaseProfit(ReleaseProfitInput input)
        {
            Assert(input.Amount >= 0, $"Amount must be greater than or equal to 0");
            Context.LogDebug(() => $"Entered ReleaseProfit. {input.ProfitId}");

            if (State.TokenContract.Value == null)
            {
                State.TokenContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            }

            var profitItem = State.ProfitItemsMap[input.ProfitId];

            Assert(profitItem != null, "Profit item not found.");

            if (profitItem == null)
            {
                return new Empty();
            }

            // Normally `input.TotalWeigh` should be 0, except the situation releasing of profits delayed for some reason.
            var totalWeight = input.TotalWeight == 0 ? profitItem.TotalWeight : input.TotalWeight;

            Assert(Context.Sender == profitItem.Creator, "Only creator can release profits.");

            Context.LogDebug(() => $"Virtual Address: {profitItem.VirtualAddress}");
            var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput
            {
                Owner = profitItem.VirtualAddress,
                Symbol = Context.Variables.NativeSymbol
            }).Balance;

            Assert(input.Amount <= balance, "Insufficient profits amount.");

            if (profitItem.IsReleaseAllBalanceEverytimeByDefault && input.Amount == 0)
            {
                Context.LogDebug(() => $"Update releasing amount to {balance}");
                input.Amount = balance;
            }

            //if input.period == 0,the token will transfer to the totalAmount in the profitItem
            //opposed,the token will transfer to the corresponding address of input.period
            if (input.Period < 0 || totalWeight <= 0)
            {
                return BurnProfits(input, profitItem, profitItem.VirtualAddress);
            }

            // Update current_period.
            var releasingPeriod = profitItem.CurrentPeriod;

            Assert(input.Period == releasingPeriod,
                $"Invalid period. When release profit item {input.ProfitId.ToHex()} of period {input.Period}. Current period is {releasingPeriod}");

            //Compute the receivingVirtualAddress by profitVirtualAddress and releasingPeriod
            var profitsReceivingVirtualAddress =
                GetReleasedPeriodProfitsVirtualAddress(profitItem.VirtualAddress, releasingPeriod);

            Context.LogDebug(() => $"Receiving virtual address: {profitsReceivingVirtualAddress}");

            //ReleasedProfitsMap means the record that profit released at a period
            var releasedProfitInformation = State.ReleasedProfitsMap[profitsReceivingVirtualAddress];
            if (releasedProfitInformation == null)
            {
                releasedProfitInformation = new ReleasedProfitsInformation
                {
                    TotalWeight = totalWeight,
                    ProfitsAmount = input.Amount,
                    IsReleased = true
                };
            }
            else
            {
                releasedProfitInformation.TotalWeight = totalWeight;
                releasedProfitInformation.ProfitsAmount = releasedProfitInformation.ProfitsAmount.Add(input.Amount);
                releasedProfitInformation.IsReleased = true;
            }

            State.ReleasedProfitsMap[profitsReceivingVirtualAddress] = releasedProfitInformation;

            Context.LogDebug(() =>
                $"Released profit information of {input.ProfitId.ToHex()} in period {input.Period}, " +
                $"total weight {releasedProfitInformation.TotalWeight}, total amount {releasedProfitInformation.ProfitsAmount}");

            // Start releasing.

            var remainAmount = input.Amount;

            Context.LogDebug(() => $"Sub profit items count: {profitItem.SubProfitItems.Count}");
            foreach (var subProfitItem in profitItem.SubProfitItems)
            {
                Context.LogDebug(() => $"Releasing {subProfitItem.ProfitId}");
                var subItemVirtualAddress = Context.ConvertVirtualAddressToContractAddress(subProfitItem.ProfitId);

                var amount = subProfitItem.Weight.Mul(input.Amount).Div(totalWeight);
                if (amount != 0)
                {
                    State.TokenContract.TransferFrom.Send(new TransferFromInput
                    {
                        From = profitItem.VirtualAddress,
                        To = subItemVirtualAddress,
                        Amount = amount,
                        Symbol = Context.Variables.NativeSymbol
                    });
                }

                remainAmount -= amount;

                var subItem = State.ProfitItemsMap[subProfitItem.ProfitId];
                if (subItem.TotalAmounts.ContainsKey(Context.Variables.NativeSymbol))
                {
                    subItem.TotalAmounts[Context.Variables.NativeSymbol] =
                        subItem.TotalAmounts[Context.Variables.NativeSymbol].Add(amount);
                }
                else
                {
                    subItem.TotalAmounts.Add(Context.Variables.NativeSymbol, amount);
                }

                State.ProfitItemsMap[subProfitItem.ProfitId] = subItem;

                // Update current_period of detail of sub profit item.
                var subItemDetail = State.ProfitDetailsMap[input.ProfitId][subItemVirtualAddress];
                foreach (var detail in subItemDetail.Details)
                {
                    detail.LastProfitPeriod = profitItem.CurrentPeriod;
                }

                State.ProfitDetailsMap[input.ProfitId][subItemVirtualAddress] = subItemDetail;
            }

            Context.LogDebug(() => "Finished the releasing of sub profit items.");
            // Transfer remain amount to individuals' receiving profits address.
            if (remainAmount != 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = profitItem.VirtualAddress,
                    To = profitsReceivingVirtualAddress,
                    Amount = remainAmount,
                    Symbol = Context.Variables.NativeSymbol
                });
            }

            profitItem.CurrentPeriod = input.Period.Add(1);
            if (!profitItem.IsReleaseAllBalanceEverytimeByDefault)
            {
                profitItem.TotalAmounts[Context.Variables.NativeSymbol] =
                    profitItem.TotalAmounts[Context.Variables.NativeSymbol].Sub(input.Amount);
            }

            State.ProfitItemsMap[input.ProfitId] = profitItem;

            Context.LogDebug(() => "Leaving ReleaseProfit.");

            return new Empty();
        }

        private Empty BurnProfits(ReleaseProfitInput input, ProfitItem profitItem, Address profitVirtualAddress)
        {
            Context.LogDebug(() => "Entered BurnProfits.");
            profitItem.CurrentPeriod = input.Period > 0 ? input.Period.Add(1) : profitItem.CurrentPeriod;

            // Release to an address that no one can receive this amount of profits.
            if (input.Amount <= 0)
            {
                State.ProfitItemsMap[input.ProfitId] = profitItem;
                return new Empty();
            }

            if (input.Period >= 0)
            {
                input.Period = -1;
            }

            // Which means the creator gonna burn this amount of profits.
            var profitsBurningVirtualAddress =
                GetReleasedPeriodProfitsVirtualAddress(profitVirtualAddress, input.Period);
            State.TokenContract.TransferFrom.Send(new TransferFromInput
            {
                From = profitVirtualAddress,
                To = profitsBurningVirtualAddress,
                Amount = input.Amount,
                Symbol = Context.Variables.NativeSymbol
            });
            profitItem.TotalAmounts[Context.Variables.NativeSymbol] =
                profitItem.TotalAmounts[Context.Variables.NativeSymbol].Sub(input.Amount);
            State.ProfitItemsMap[input.ProfitId] = profitItem;
            return new Empty();
        }

        public override Empty AddProfits(AddProfitsInput input)
        {
            var tokenInfo = State.TokenContract.GetTokenInfo.Call(new GetTokenInfoInput
            {
                Symbol = input.TokenSymbol
            });
            Assert(input.TokenSymbol != null && input.TokenSymbol.Any() && tokenInfo.TotalSupply != 0,
                "Invalid token symbol.");
            if (input.TokenSymbol == null)
            {
                return new Empty();
            }

            var profitItem = State.ProfitItemsMap[input.ProfitId];
            Assert(profitItem != null, "Profit item not found.");

            if (profitItem == null)
            {
                return new Empty();
            }

            var virtualAddress = Context.ConvertVirtualAddressToContractAddress(input.ProfitId);

            //if input.period == 0,the token will transfer to the totalAmount in the profitItem
            //opposed,the token will transfer to the corresponding address of input.period
            if (input.Period == 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = virtualAddress,
                    Symbol = input.TokenSymbol,
                    Amount = input.Amount,
                    Memo = $"Add {input.Amount} dividends for {input.ProfitId}."
                });
                if (!profitItem.TotalAmounts.ContainsKey(input.TokenSymbol))
                {
                    profitItem.TotalAmounts.Add(input.TokenSymbol, input.Amount);
                }
                else
                {
                    profitItem.TotalAmounts[input.TokenSymbol] =
                        profitItem.TotalAmounts[input.TokenSymbol].Add(input.Amount);
                }

                State.ProfitItemsMap[input.ProfitId] = profitItem;
            }
            else
            {
                var releasedProfitsVirtualAddress =
                    GetReleasedPeriodProfitsVirtualAddress(virtualAddress, input.Period);

                var releasedProfitsInformation = State.ReleasedProfitsMap[releasedProfitsVirtualAddress];
                if (releasedProfitsInformation == null)
                {
                    releasedProfitsInformation = new ReleasedProfitsInformation
                    {
                        ProfitsAmount = input.Amount
                    };
                }
                else
                {
                    Assert(!releasedProfitsInformation.IsReleased,
                        $"Profit item of period {input.Period} already released.");
                    releasedProfitsInformation.ProfitsAmount =
                        releasedProfitsInformation.ProfitsAmount.Add(input.Amount);
                }

                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = releasedProfitsVirtualAddress,
                    Symbol = input.TokenSymbol,
                    Amount = input.Amount,
                    Memo = $"Add dividends for {input.ProfitId} (period {input.Period})."
                });

                State.ReleasedProfitsMap[releasedProfitsVirtualAddress] = releasedProfitsInformation;
            }

            return new Empty();
        }

        /// <summary>
        /// Gain the profit form profitId from Details.lastPeriod to profitItem.currentPeriod-1;
        /// </summary>
        /// <param name="input">ProfitInput</param>
        /// <returns></returns>
        public override Empty Profit(ProfitInput input)
        {
            Context.LogDebug(() => $"{Context.Sender} is trying to profit from {input.ProfitId.ToHex()}.");

            var profitItem = State.ProfitItemsMap[input.ProfitId];
            Assert(profitItem != null, "Profit item not found.");

            var profitDetails = State.ProfitDetailsMap[input.ProfitId][Context.Sender];

            Assert(profitDetails != null, "Profit details not found.");

            if (profitDetails == null || profitItem == null)
            {
                return new Empty();
            }

            var profitVirtualAddress = Context.ConvertVirtualAddressToContractAddress(input.ProfitId);

            var availableDetails = profitDetails.Details.Where(d => d.LastProfitPeriod != profitItem.CurrentPeriod)
                .ToList();

            // Only can get profit until profitItem.CurrentPeriod-1,because currentPeriod hasn't be released.
            for (var i = 0;
                i < Math.Min(ProfitContractConsts.ProfitReceivingLimitForEachTime, availableDetails.Count);
                i++)
            {
                var profitDetail = availableDetails[i];
                if (profitDetail.LastProfitPeriod == 0)
                {
                    profitDetail.LastProfitPeriod = profitDetail.StartPeriod;
                }

                var lastProfitPeriod = profitDetail.LastProfitPeriod;
                // Can only get profit until profitItem.CurrentPeriod - 1,because currentPeriod hasn't be released.
                for (var period = profitDetail.LastProfitPeriod;
                    period <= (profitDetail.EndPeriod == long.MaxValue
                        ? profitItem.CurrentPeriod - 1
                        : Math.Min(profitItem.CurrentPeriod - 1, profitDetail.EndPeriod));
                    period++)
                {
                    var releasedProfitsVirtualAddress =
                        GetReleasedPeriodProfitsVirtualAddress(profitVirtualAddress, period);
                    var releasedProfitsInformation = State.ReleasedProfitsMap[releasedProfitsVirtualAddress];
                    var amount = profitDetail.Weight.Mul(releasedProfitsInformation.ProfitsAmount)
                        .Div(releasedProfitsInformation.TotalWeight);
                    var period1 = period;
                    Context.LogDebug(() =>
                        $"{Context.Sender} is profiting {amount} tokens from {input.ProfitId.ToHex()} in period {period1}");
                    if (releasedProfitsInformation.IsReleased && amount > 0)
                    {
                        State.TokenContract.TransferFrom.Send(new TransferFromInput
                        {
                            From = releasedProfitsVirtualAddress,
                            To = Context.Sender,
                            Symbol = Context.Variables.NativeSymbol,
                            Amount = amount
                        });
                    }

                    lastProfitPeriod = period + 1;
                }

                profitDetail.LastProfitPeriod = lastProfitPeriod;
            }

            State.ProfitDetailsMap[input.ProfitId][Context.Sender] = profitDetails;

            return new Empty();
        }

        public override Hash GetTreasuryProfitId(Empty input)
        {
            return State.TreasuryProfitId.Value;
        }
    }
}