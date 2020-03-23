﻿using System;
using System.Collections.Generic;
using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Sdk.CSharp.State;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Profit
{
    /// <summary>
    /// Let's imagine a scenario:
    /// 1. Ean creates a profit scheme FOO: Ean calls CreateScheme. We call this profit scheme PI_FOO.
    /// 2. GL creates another profit scheme BAR: GL calls CreateScheme. We call this profit scheme PI_BAR.
    /// 3. Ean (as the creator of PI_FOO) register PI_BAR as a sub profit scheme as PI_FOO:
    /// Ean call RemoveSubScheme (SchemeId: PI_BAR's profit id, Shares : 1)
    /// 4. Anil has an account which address is ADDR_Anil.
    /// 5. Ean registers address ADDR_Anil as a profit Beneficiary of PI_FOO: Ean calls AddBeneficiary (Beneficiary: ADDR_Anil, Shares : 1)
    /// 6: Now PI_FOO is organized like this:
    ///         PI_FOO
    ///        /      \
    ///       1        1
    ///      /          \
    ///    PI_BAR     ADDR_Anil
    ///    (Total Shares is 2)
    /// 7. Ean adds some ELF tokens to PI_FOO: Ean calls DistributeProfits (Symbol: "ELF", Amount: 1000L, Period: 1)
    /// 8. Ean calls DistributeProfits: Balance of PI_BAR is 500L (PI_BAR's general ledger balance, also we can say balance of virtual address of PI_BAR is 500L),
    /// 9. Balance of PI_FOO's virtual address of first period is 500L.
    /// 10. Anil can only get his profits by calling Profit (SchemeId: PI_BAR's profit id, Symbol: "ELF")
    /// </summary>
    public partial class ProfitContract : ProfitContractContainer.ProfitContractBase
    {
        /// <summary>
        /// Create a Scheme of profit distribution.
        /// At the first time, the scheme's id is unknown,it may create by transaction id and createdSchemeIds;
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Hash CreateScheme(CreateSchemeInput input)
        {
            ValidateContractState(State.TokenContract, SmartContractConstants.TokenContractSystemName);

            if (input.ProfitReceivingDuePeriodCount == 0)
            {
                input.ProfitReceivingDuePeriodCount = ProfitContractConstants.DefaultProfitReceivingDuePeriodCount;
            }
            else
            {
                Assert(
                    input.ProfitReceivingDuePeriodCount > 0 &&
                    input.ProfitReceivingDuePeriodCount <= ProfitContractConstants.MaximumProfitReceivingDuePeriodCount,
                    "Invalid profit receiving due period count.");
            }

            var manager = input.Manager ?? Context.Sender;
            var schemeId = Context.TransactionId;
            // Why? Because one transaction may create many profit schemes via inline transactions.
            var createdSchemeIds = State.ManagingSchemeIds[manager]?.SchemeIds;
            if (createdSchemeIds != null && createdSchemeIds.Contains(schemeId))
            {
                // So we choose this way to avoid profit id conflicts in aforementioned situation.
                schemeId = Hash.FromTwoHashes(schemeId, createdSchemeIds.Last());
            }

            var scheme = GetNewScheme(input, schemeId, manager);
            State.SchemeInfos[schemeId] = scheme;

            var schemeIds = State.ManagingSchemeIds[scheme.Manager];
            if (schemeIds == null)
            {
                schemeIds = new CreatedSchemeIds
                {
                    SchemeIds = {schemeId}
                };
            }
            else
            {
                schemeIds.SchemeIds.Add(schemeId);
            }

            State.ManagingSchemeIds[scheme.Manager] = schemeIds;

            Context.LogDebug(() => $"Created scheme {State.SchemeInfos[schemeId]}");

            Context.Fire(new SchemeCreated
            {
                SchemeId = scheme.SchemeId,
                Manager = scheme.Manager,
                IsReleaseAllBalanceEveryTimeByDefault = scheme.IsReleaseAllBalanceEveryTimeByDefault,
                ProfitReceivingDuePeriodCount = scheme.ProfitReceivingDuePeriodCount,
                VirtualAddress = scheme.VirtualAddress
            });
            return schemeId;
        }

        /// <summary>
        /// Add a child to a existed scheme.
        /// </summary>
        /// <param name="input">AddSubSchemeInput</param>
        /// <returns></returns>
        public override Empty AddSubScheme(AddSubSchemeInput input)
        {
            Assert(input.SchemeId != input.SubSchemeId, "Two schemes cannot be same.");
            Assert(input.SubSchemeShares > 0, "Shares of sub scheme should greater than 0.");

            var scheme = State.SchemeInfos[input.SchemeId];
            Assert(scheme != null, "Scheme not found.");
            // ReSharper disable once PossibleNullReferenceException
            Assert(Context.Sender == scheme.Manager, "Only manager can add sub-scheme.");

            var subSchemeId = input.SubSchemeId;
            var subScheme = State.SchemeInfos[subSchemeId];
            Assert(subScheme != null, "Sub scheme not found.");

            var subSchemeVirtualAddress = Context.ConvertVirtualAddressToContractAddress(subSchemeId);
            // Add profit details and total shares of the father scheme.
            AddBeneficiary(new AddBeneficiaryInput
            {
                SchemeId = input.SchemeId,
                BeneficiaryShare = new BeneficiaryShare
                {
                    Beneficiary = subSchemeVirtualAddress,
                    Shares = input.SubSchemeShares
                },
                EndPeriod = long.MaxValue
            });

            // Add a sub profit scheme.
            scheme.SubSchemes.Add(new SchemeBeneficiaryShare
            {
                SchemeId = input.SubSchemeId,
                Shares = input.SubSchemeShares
            });
            State.SchemeInfos[input.SchemeId] = scheme;

            return new Empty();
        }

        public override Empty RemoveSubScheme(RemoveSubSchemeInput input)
        {
            Assert(input.SchemeId != input.SubSchemeId, "Two schemes cannot be same.");

            var scheme = State.SchemeInfos[input.SchemeId];
            Assert(scheme != null, "Scheme not found.");

            // ReSharper disable once PossibleNullReferenceException
            Assert(Context.Sender == scheme.Manager, "Only manager can remove sub-scheme.");

            var subSchemeId = input.SubSchemeId;
            var subScheme = State.SchemeInfos[subSchemeId];
            Assert(subScheme != null, "Sub scheme not found.");

            var subSchemeVirtualAddress = Context.ConvertVirtualAddressToContractAddress(subSchemeId);
            // Remove profit details
            State.ProfitDetailsMap[input.SchemeId][subSchemeVirtualAddress] = new ProfitDetails();

            var shares = scheme.SubSchemes.Single(d => d.SchemeId == input.SubSchemeId);
            scheme.SubSchemes.Remove(shares);
            scheme.TotalShares = scheme.TotalShares.Sub(shares.Shares);
            State.SchemeInfos[input.SchemeId] = scheme;

            return new Empty();
        }

        public override Empty AddBeneficiary(AddBeneficiaryInput input)
        {
            AssertValidInput(input);
            if (input.BeneficiaryShare == null) return new Empty();

            if (input.EndPeriod == 0)
            {
                // Which means this profit Beneficiary will never expired unless removed.
                input.EndPeriod = long.MaxValue;
            }

            var schemeId = input.SchemeId;
            var scheme = State.SchemeInfos[schemeId];

            Assert(scheme != null, "Scheme not found.");

            // ReSharper disable once PossibleNullReferenceException
            Assert(
                Context.Sender == scheme.Manager || Context.Sender ==
                Context.GetContractAddressByName(SmartContractConstants.TokenHolderContractSystemName),
                "Only manager can add beneficiary.");

            Context.LogDebug(() =>
                $"{input.SchemeId}.\n End Period: {input.EndPeriod}, Current Period: {scheme.CurrentPeriod}");

            Assert(input.EndPeriod >= scheme.CurrentPeriod, $"Invalid end period. End Period: {input.EndPeriod}, Current Period: {scheme.CurrentPeriod}");

            scheme.TotalShares = scheme.TotalShares.Add(input.BeneficiaryShare.Shares);

            State.SchemeInfos[schemeId] = scheme;

            var profitDetail = new ProfitDetail
            {
                StartPeriod = scheme.CurrentPeriod.Add(scheme.DelayDistributePeriodCount),
                EndPeriod = input.EndPeriod,
                Shares = input.BeneficiaryShare.Shares,
            };

            var currentProfitDetails = State.ProfitDetailsMap[schemeId][input.BeneficiaryShare.Beneficiary];
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
                     d.EndPeriod.Add(scheme.ProfitReceivingDuePeriodCount) < scheme.CurrentPeriod))
            {
                currentProfitDetails.Details.Remove(detail);
            }

            State.ProfitDetailsMap[schemeId][input.BeneficiaryShare.Beneficiary] = currentProfitDetails;

            Context.LogDebug(() =>
                $"Added {input.BeneficiaryShare.Shares} weights to scheme {input.SchemeId.ToHex()}: {profitDetail}");

            return new Empty();
        }

        private void AssertValidInput(AddBeneficiaryInput input)
        {
            Assert(input.SchemeId != null, "Invalid scheme id.");
            Assert(input.BeneficiaryShare?.Beneficiary != null, "Invalid beneficiary address.");
            Assert(input.BeneficiaryShare?.Shares >= 0, "Invalid share.");
        }

        public override Empty RemoveBeneficiary(RemoveBeneficiaryInput input)
        {
            Assert(input.SchemeId != null, "Invalid scheme id.");
            Assert(input.Beneficiary != null, "Invalid Beneficiary address.");

            var scheme = State.SchemeInfos[input.SchemeId];

            Assert(scheme != null, "Scheme not found.");

            var currentDetail = State.ProfitDetailsMap[input.SchemeId][input.Beneficiary];

            if (scheme == null || currentDetail == null) return new Empty();

            Assert(Context.Sender == scheme.Manager || Context.Sender ==
                   Context.GetContractAddressByName(SmartContractConstants.TokenHolderContractSystemName),
                "Only manager can remove beneficiary.");

            var expiryDetails = scheme.CanRemoveBeneficiaryDirectly
                ? currentDetail.Details.ToList()
                : currentDetail.Details
                    .Where(d => d.EndPeriod < scheme.CurrentPeriod && !d.IsWeightRemoved).ToList();

            if (!expiryDetails.Any()) return new Empty();

            var shares = expiryDetails.Sum(d => d.Shares);
            foreach (var expiryDetail in expiryDetails)
            {
                expiryDetail.IsWeightRemoved = true;
                if (expiryDetail.LastProfitPeriod >= scheme.CurrentPeriod)
                {
                    currentDetail.Details.Remove(expiryDetail);
                }
            }

            // Clear old profit details.
            if (currentDetail.Details.Count != 0)
            {
                State.ProfitDetailsMap[input.SchemeId][input.Beneficiary] = currentDetail;
            }
            else
            {
                State.ProfitDetailsMap[input.SchemeId].Remove(input.Beneficiary);
            }

            scheme.TotalShares = scheme.TotalShares.Sub(shares);
            State.SchemeInfos[input.SchemeId] = scheme;

            return new Empty();
        }

        public override Empty AddBeneficiaries(AddBeneficiariesInput input)
        {
            foreach (var beneficiaryShare in input.BeneficiaryShares)
            {
                AddBeneficiary(new AddBeneficiaryInput
                {
                    SchemeId = input.SchemeId,
                    BeneficiaryShare = beneficiaryShare,
                    EndPeriod = input.EndPeriod
                });
            }

            return new Empty();
        }

        public override Empty RemoveBeneficiaries(RemoveBeneficiariesInput input)
        {
            foreach (var beneficiary in input.Beneficiaries)
            {
                RemoveBeneficiary(new RemoveBeneficiaryInput
                {
                    SchemeId = input.SchemeId, Beneficiary = beneficiary
                });
            }

            return new Empty();
        }

        /// <summary>
        /// 
        /// Will burn/destroy a certain amount of profits if `input.Period` is less than 0.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Empty DistributeProfits(DistributeProfitsInput input)
        {
            Assert(input.Amount >= 0, "Amount must be greater than or equal to 0");

            var scheme = State.SchemeInfos[input.SchemeId];
            Assert(scheme != null, "Scheme not found.");

            // ReSharper disable once PossibleNullReferenceException
            Assert(Context.Sender == scheme.Manager || Context.Sender ==
                   Context.GetContractAddressByName(SmartContractConstants.TokenHolderContractSystemName),
                "Only manager can distribute profits.");

            ValidateContractState(State.TokenContract, SmartContractConstants.TokenContractSystemName);

            var profitsMap = new Dictionary<string, long>();
            if (input.Amount > 0)
            {
                // In this situation, manager only want to distribute one token at this time.
                Assert(input.Symbol != null && input.Symbol.Any(), "Invalid token symbol.");
                // ReSharper disable once AssignNullToNotNullAttribute
                profitsMap.Add(input.Symbol, input.Amount);
            }
            else
            {
                if (scheme.IsReleaseAllBalanceEveryTimeByDefault && scheme.ReceivedTokenSymbols.Any())
                {
                    // Distribute all from general ledger.
                    foreach (var symbol in scheme.ReceivedTokenSymbols)
                    {
                        var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput
                        {
                            Owner = scheme.VirtualAddress,
                            Symbol = symbol
                        }).Balance;
                        profitsMap.Add(symbol, balance);
                    }
                }
            }

            var totalShares = scheme.TotalShares;

            if (scheme.DelayDistributePeriodCount > 0)
            {
                scheme.CachedDelayTotalShares.Add(input.Period.Add(scheme.DelayDistributePeriodCount), totalShares);
                if (scheme.CachedDelayTotalShares.ContainsKey(input.Period))
                {
                    totalShares = scheme.CachedDelayTotalShares[input.Period];
                    scheme.CachedDelayTotalShares.Remove(input.Period);
                }
                else
                {
                    totalShares = 0;
                }
            }

            var releasingPeriod = scheme.CurrentPeriod;
            Assert(input.Period == releasingPeriod,
                $"Invalid period. When release scheme {input.SchemeId.ToHex()} of period {input.Period}. Current period is {releasingPeriod}");

            var profitsReceivingVirtualAddress =
                GetDistributedPeriodProfitsVirtualAddress(scheme.VirtualAddress, releasingPeriod);

            if (input.Period < 0 || totalShares <= 0)
            {
                return BurnProfits(input.Period, profitsMap, scheme, profitsReceivingVirtualAddress);
            }

            Context.LogDebug(() => $"Receiving virtual address: {profitsReceivingVirtualAddress}");

            UpdateDistributedProfits(profitsMap, profitsReceivingVirtualAddress, totalShares);
            
            PerformDistributeProfits(input, scheme, totalShares, profitsReceivingVirtualAddress);

            scheme.CurrentPeriod = input.Period.Add(1);

            State.SchemeInfos[input.SchemeId] = scheme;

            return new Empty();
        }

        /// <summary>
        /// Just burn balance in general ledger.
        /// </summary>
        /// <param name="period"></param>
        /// <param name="profitsMap"></param>
        /// <param name="scheme"></param>
        /// <param name="profitsReceivingVirtualAddress"></param>
        /// <returns></returns>
        private Empty BurnProfits(long period, Dictionary<string, long> profitsMap, Scheme scheme,
            Address profitsReceivingVirtualAddress)
        {
            Context.LogDebug(() => "Entered BurnProfits.");
            scheme.CurrentPeriod = period > 0 ? period.Add(1) : scheme.CurrentPeriod;

            var distributedProfitsInfo = new DistributedProfitsInfo
            {
                IsReleased = true
            };
            foreach (var profits in profitsMap)
            {
                var symbol = profits.Key;
                var amount = profits.Value;
                if (amount > 0)
                {
                    State.TokenContract.TransferFrom.Send(new TransferFromInput
                    {
                        From = scheme.VirtualAddress,
                        To = Context.Self,
                        Amount = amount,
                        Symbol = symbol
                    });
                    State.TokenContract.Burn.Send(new BurnInput
                    {
                        Amount = amount,
                        Symbol = symbol
                    });
                    distributedProfitsInfo.ProfitsAmount.Add(symbol, -amount);
                }
            }

            State.SchemeInfos[scheme.SchemeId] = scheme;
            State.DistributedProfitsMap[profitsReceivingVirtualAddress] = distributedProfitsInfo;
            return new Empty();
        }

        private void UpdateDistributedProfits(Dictionary<string, long> profitsMap,
            Address profitsReceivingVirtualAddress, long totalShares)
        {
            var distributedProfitsInformation = State.DistributedProfitsMap[profitsReceivingVirtualAddress];

            if (distributedProfitsInformation == null)
            {
                distributedProfitsInformation = new DistributedProfitsInfo
                {
                    TotalShares = totalShares,
                    IsReleased = true
                };
            }

            foreach (var profits in profitsMap)
            {
                var symbol = profits.Key;
                var amount = profits.Value;
                var balanceOfVirtualAddressForCurrentPeriod = State.TokenContract.GetBalance.Call(new GetBalanceInput
                {
                    Owner = profitsReceivingVirtualAddress,
                    Symbol = symbol
                }).Balance;
                distributedProfitsInformation.ProfitsAmount.Add(symbol,
                    amount.Add(balanceOfVirtualAddressForCurrentPeriod));
            }

            State.DistributedProfitsMap[profitsReceivingVirtualAddress] = distributedProfitsInformation;
        }

        private void PerformDistributeProfits(DistributeProfitsInput input, Scheme scheme, long totalShares,
            Address profitsReceivingVirtualAddress)
        {
            var remainAmount = input.Amount;

            remainAmount = DistributeProfitsForSubSchemes(input, scheme, totalShares, remainAmount);

            // Transfer remain amount to individuals' receiving profits address.
            if (remainAmount != 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = scheme.VirtualAddress,
                    To = profitsReceivingVirtualAddress,
                    Amount = remainAmount,
                    Symbol = input.Symbol
                });
            }
        }

        private long DistributeProfitsForSubSchemes(DistributeProfitsInput input, Scheme scheme, long totalShares,
            long remainAmount)
        {
            Context.LogDebug(() => $"Sub schemes count: {scheme.SubSchemes.Count}");
            foreach (var subScheme in scheme.SubSchemes)
            {
                Context.LogDebug(() => $"Releasing {subScheme.SchemeId}");

                // General ledger of this sub profit scheme.
                var subItemVirtualAddress = Context.ConvertVirtualAddressToContractAddress(subScheme.SchemeId);

                var amount = SafeCalculateProfits(subScheme.Shares, input.Amount, totalShares);
                if (amount != 0)
                {
                    State.TokenContract.TransferFrom.Send(new TransferFromInput
                    {
                        From = scheme.VirtualAddress,
                        To = subItemVirtualAddress,
                        Amount = amount,
                        Symbol = input.Symbol
                    });
                }

                remainAmount = remainAmount.Sub(amount);

                // Update current_period of detail of sub profit scheme.
                var subItemDetail = State.ProfitDetailsMap[input.SchemeId][subItemVirtualAddress];
                foreach (var detail in subItemDetail.Details)
                {
                    detail.LastProfitPeriod = scheme.CurrentPeriod;
                }

                State.ProfitDetailsMap[input.SchemeId][subItemVirtualAddress] = subItemDetail;
            }

            return remainAmount;
        }

        public override Empty ContributeProfits(ContributeProfitsInput input)
        {
            Assert(input.Symbol != null && input.Symbol.Any(), "Invalid token symbol.");
            Assert(input.Amount > 0, "Amount need to greater than 0.");

            var scheme = State.SchemeInfos[input.SchemeId];
            Assert(scheme != null, "Scheme not found.");

            // ReSharper disable once PossibleNullReferenceException
            var virtualAddress = scheme.VirtualAddress;

            if (input.Period == 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = virtualAddress,
                    Symbol = input.Symbol,
                    Amount = input.Amount,
                    Memo = $"Add {input.Amount} dividends."
                });

                State.SchemeInfos[input.SchemeId] = scheme;
            }
            else
            {
                Assert(input.Period >= scheme.CurrentPeriod, "Invalid contributing period.");
                var distributedPeriodProfitsVirtualAddress =
                    GetDistributedPeriodProfitsVirtualAddress(virtualAddress, input.Period);

                var distributedProfitsInformation = State.DistributedProfitsMap[distributedPeriodProfitsVirtualAddress];
                if (distributedProfitsInformation == null)
                {
                    distributedProfitsInformation = new DistributedProfitsInfo
                    {
                        ProfitsAmount = {{input.Symbol, input.Amount}}
                    };
                }
                else
                {
                    Assert(!distributedProfitsInformation.IsReleased,
                        $"Scheme of period {input.Period} already released.");
                    distributedProfitsInformation.ProfitsAmount[input.Symbol] =
                        distributedProfitsInformation.ProfitsAmount[input.Symbol].Add(input.Amount);
                }

                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = distributedPeriodProfitsVirtualAddress,
                    Symbol = input.Symbol,
                    Amount = input.Amount,
                });

                State.DistributedProfitsMap[distributedPeriodProfitsVirtualAddress] = distributedProfitsInformation;
            }

            return new Empty();
        }

        public override Empty ResetManager(ResetManagerInput input)
        {
            var scheme = State.SchemeInfos[input.SchemeId];
            Assert(scheme != null, "Scheme not found.");

            // ReSharper disable once PossibleNullReferenceException
            Assert(Context.Sender == scheme.Manager, "Only scheme manager can reset manager.");
            Assert(input.NewManager.Value.Any(), "Invalid new sponsor.");

            // Transfer managing scheme id.
            var oldManagerSchemeIds = State.ManagingSchemeIds[scheme.Manager];
            oldManagerSchemeIds.SchemeIds.Remove(input.SchemeId);
            State.ManagingSchemeIds[scheme.Manager] = oldManagerSchemeIds;
            var newManagerSchemeIds = State.ManagingSchemeIds[input.NewManager] ?? new CreatedSchemeIds();
            newManagerSchemeIds.SchemeIds.Add(input.SchemeId);
            State.ManagingSchemeIds[input.NewManager] = newManagerSchemeIds;

            scheme.Manager = input.NewManager;
            State.SchemeInfos[input.SchemeId] = scheme;
            return new Empty();
        }

        /// <summary>
        /// Gain the profit form SchemeId from Details.lastPeriod to scheme.currentPeriod - 1;
        /// </summary>
        /// <param name="input">ClaimProfitsInput</param>
        /// <returns></returns>
        public override Empty ClaimProfits(ClaimProfitsInput input)
        {
            Assert(input.Symbol != null && input.Symbol.Any(), "Invalid token symbol.");

            var scheme = State.SchemeInfos[input.SchemeId];
            Assert(scheme != null, "Scheme not found.");
            var beneficiary = input.Beneficiary ?? Context.Sender;
            var profitDetails = State.ProfitDetailsMap[input.SchemeId][beneficiary];
            Assert(profitDetails != null, "Profit details not found.");

            Context.LogDebug(
                () =>
                    $"{Context.Sender} is trying to profit {input.Symbol} from {input.SchemeId.ToHex()} for {beneficiary}.");

            var profitVirtualAddress = Context.ConvertVirtualAddressToContractAddress(input.SchemeId);

            // ReSharper disable once PossibleNullReferenceException
            var availableDetails = profitDetails.Details.Where(d => d.EndPeriod >= d.LastProfitPeriod).ToList();
            // ReSharper disable once PossibleNullReferenceException
            var profitableDetails = availableDetails.Where(d => d.LastProfitPeriod < scheme.CurrentPeriod).ToList();

            Context.LogDebug(() =>
                $"Profitable details: {profitableDetails.Aggregate("\n", (profit1, profit2) => profit1.ToString() + "\n" + profit2)}");

            // Only can get profit from last profit period to actual last period (profit.CurrentPeriod - 1),
            // because current period not released yet.
            for (var i = 0;
                i < Math.Min(ProfitContractConstants.ProfitReceivingLimitForEachTime, profitableDetails.Count);
                i++)
            {
                var profitDetail = profitableDetails[i];
                if (profitDetail.LastProfitPeriod == 0)
                {
                    // This detail never performed profit before.
                    profitDetail.LastProfitPeriod = profitDetail.StartPeriod;
                }

                ProfitAllPeriods(scheme, input.Symbol, profitDetail, profitVirtualAddress, beneficiary);
            }

            State.ProfitDetailsMap[input.SchemeId][beneficiary] = new ProfitDetails {Details = {availableDetails}};

            return new Empty();
        }

        private long ProfitAllPeriods(Scheme scheme, string symbol, ProfitDetail profitDetail,
            Address profitVirtualAddress, Address beneficiary, bool isView = false)
        {
            var totalAmount = 0L;
            var lastProfitPeriod = profitDetail.LastProfitPeriod;
            for (var period = profitDetail.LastProfitPeriod;
                period <= (profitDetail.EndPeriod == long.MaxValue
                    ? scheme.CurrentPeriod - 1
                    : Math.Min(scheme.CurrentPeriod - 1, profitDetail.EndPeriod));
                period++)
            {
                var periodToPrint = period;
                var detailToPrint = profitDetail;
                var distributedPeriodProfitsVirtualAddress =
                    GetDistributedPeriodProfitsVirtualAddress(profitVirtualAddress, period);
                var distributedProfitsInformation = State.DistributedProfitsMap[distributedPeriodProfitsVirtualAddress];
                if (distributedProfitsInformation == null || distributedProfitsInformation.TotalShares == 0)
                {
                    continue;
                }

                Context.LogDebug(() => $"Released profit information: {distributedProfitsInformation}");
                var amount = SafeCalculateProfits(profitDetail.Shares,
                    distributedProfitsInformation.ProfitsAmount[symbol], distributedProfitsInformation.TotalShares);

                if (!isView)
                {
                    Context.LogDebug(() =>
                        $"{beneficiary} is profiting {amount} {symbol} tokens from {scheme.SchemeId.ToHex()} in period {periodToPrint}." +
                        $"Sender's Shares: {detailToPrint.Shares}, total Shares: {distributedProfitsInformation.TotalShares}");
                    if (distributedProfitsInformation.IsReleased && amount > 0)
                    {
                        State.TokenContract.TransferFrom.Send(new TransferFromInput
                        {
                            From = distributedPeriodProfitsVirtualAddress,
                            To = beneficiary,
                            Symbol = symbol,
                            Amount = amount
                        });
                    }

                    lastProfitPeriod = period + 1;
                }

                totalAmount = totalAmount.Add(amount);
            }

            profitDetail.LastProfitPeriod = lastProfitPeriod;

            return totalAmount;
        }

        private void ValidateContractState(ContractReferenceState state, Hash contractSystemName)
        {
            if (state.Value != null)
                return;
            state.Value = Context.GetContractAddressByName(contractSystemName);
        }

        private Scheme GetNewScheme(CreateSchemeInput input, Hash schemeId, Address manager)
        {
            var scheme = new Scheme
            {
                SchemeId = schemeId,
                // The address of general ledger for current profit scheme.
                VirtualAddress = Context.ConvertVirtualAddressToContractAddress(schemeId),
                Manager = manager,
                ProfitReceivingDuePeriodCount = input.ProfitReceivingDuePeriodCount,
                CurrentPeriod = 1,
                IsReleaseAllBalanceEveryTimeByDefault = input.IsReleaseAllBalanceEveryTimeByDefault,
                DelayDistributePeriodCount = input.DelayDistributePeriodCount,
                CanRemoveBeneficiaryDirectly = input.CanRemoveBeneficiaryDirectly
            };

            return scheme;
        }

        private static long SafeCalculateProfits(long totalAmount, long shares, long totalShares)
        {
            var decimalTotalAmount = (decimal) totalAmount;
            var decimalShares = (decimal) shares;
            var decimalTotalShares = (decimal) totalShares;
            return (long) (decimalTotalAmount * decimalShares / decimalTotalShares);
        }
    }
}