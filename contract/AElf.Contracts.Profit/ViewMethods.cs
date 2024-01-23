using System;
using System.Collections.Generic;
using System.Linq;
using AElf.CSharp.Core;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Profit;

public partial class ProfitContract
{
    public override CreatedSchemeIds GetManagingSchemeIds(GetManagingSchemeIdsInput input)
    {
        return State.ManagingSchemeIds[input.Manager];
    }

    public override Scheme GetScheme(Hash input)
    {
        return State.SchemeInfos[input];
    }

    /// <summary>
    ///     If input.Period == 0, the result will be the address of general ledger of a certain profit scheme;
    ///     Otherwise the result will be the address of a specific account period of a certain profit scheme,
    ///     which profit receivers will gain profits from.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public override Address GetSchemeAddress(SchemePeriod input)
    {
        var virtualAddress = Context.ConvertVirtualAddressToContractAddress(input.SchemeId);
        return input.Period == 0
            ? virtualAddress
            : GetDistributedPeriodProfitsVirtualAddress(input.SchemeId, input.Period);
    }

    public override DistributedProfitsInfo GetDistributedProfitsInfo(SchemePeriod input)
    {
        var releasedProfitsVirtualAddress = GetDistributedPeriodProfitsVirtualAddress(input.SchemeId, input.Period);
        return State.DistributedProfitsMap[releasedProfitsVirtualAddress] ?? new DistributedProfitsInfo
        {
            TotalShares = -1
        };
    }

    public override ProfitDetails GetProfitDetails(GetProfitDetailsInput input)
    {
        return State.ProfitDetailsMap[input.SchemeId][input.Beneficiary];
    }

    private Address GetDistributedPeriodProfitsVirtualAddress(Hash schemeId, long period)
    {
        return Context.ConvertVirtualAddressToContractAddress(
            GeneratePeriodVirtualAddressFromHash(schemeId, period));
    }

    private Hash GeneratePeriodVirtualAddressFromHash(Hash schemeId, long period)
    {
        return HashHelper.XorAndCompute(schemeId, HashHelper.ComputeFrom(period));
    }

    public override Int64Value GetProfitAmount(GetProfitAmountInput input)
    {
        var output = GetAllProfitAmount(input.SchemeId, input.Symbol, input.Beneficiary);

        return new Int64Value
        {
            Value = output.AllProfitAmount
        };
    }

    public override GetAllProfitAmountOutput GetAllProfitAmount(GetAllProfitAmountInput input)
    {
        return GetAllProfitAmount(input.SchemeId, input.Symbol, input.Beneficiary);
    }

    private GetAllProfitAmountOutput GetAllProfitAmount(Hash schemaId, string symbol, Address beneficiary)
    {
        var profitItem = State.SchemeInfos[schemaId];
        Assert(profitItem != null, "Scheme not found.");
        beneficiary = beneficiary ?? Context.Sender;
        var profitDetails = State.ProfitDetailsMap[schemaId][beneficiary];

        if (profitDetails == null) return new GetAllProfitAmountOutput { AllProfitAmount = 0, OneTimeClaimableProfitAmount = 0 };

        // ReSharper disable once PossibleNullReferenceException
        var availableDetails = profitDetails.Details.Where(d =>
            d.LastProfitPeriod < profitItem.CurrentPeriod && (d.LastProfitPeriod == 0
                ? d.EndPeriod >= d.StartPeriod
                : d.EndPeriod >= d.LastProfitPeriod)
        ).ToList();

        var allProfitAmount = 0L;
        var claimableProfitAmount = 0L;

        var profitableDetailCount =
            Math.Min(ProfitContractConstants.ProfitReceivingLimitForEachTime, availableDetails.Count);
        var maxProfitReceivingPeriodCount = GetMaximumPeriodCountForProfitableDetail(profitableDetailCount);
        
        for (var i = 0;i < availableDetails.Count; i++)
        {
            var profitDetail = availableDetails[i];
            if (profitDetail.LastProfitPeriod == 0) profitDetail.LastProfitPeriod = profitDetail.StartPeriod;

            var totalProfitsDictForEachProfitDetail = ProfitAllPeriods(profitItem, profitDetail, beneficiary,
                profitDetail.EndPeriod.Sub(profitDetail.LastProfitPeriod), true, symbol);
            allProfitAmount =
                allProfitAmount.Add(totalProfitsDictForEachProfitDetail.TryGetValue(symbol, out var value)
                    ? value
                    : 0);
            if(i >= profitableDetailCount) continue;
            var claimableProfitsDictForEachProfitDetail = ProfitAllPeriods(profitItem, profitDetail, beneficiary,
                maxProfitReceivingPeriodCount, true,
                symbol);

            claimableProfitAmount =
                claimableProfitAmount.Add(
                    claimableProfitsDictForEachProfitDetail.TryGetValue(symbol, out var claimableValue)
                        ? claimableValue
                        : 0);
        }

        return new GetAllProfitAmountOutput
        {
            AllProfitAmount = allProfitAmount,
            OneTimeClaimableProfitAmount = claimableProfitAmount
        };
    }

    public override ReceivedProfitsMap GetProfitsMap(ClaimProfitsInput input)
    {
        var output = GetAllProfitsMap(input.SchemeId, input.Beneficiary);

        return new ReceivedProfitsMap
        {
            Value = { output.AllProfitsMap }
        };
    }

    public override GetAllProfitsMapOutput GetAllProfitsMap(GetAllProfitsMapInput input)
    {
        return GetAllProfitsMap(input.SchemeId, input.Beneficiary);
    }

    private GetAllProfitsMapOutput GetAllProfitsMap(Hash schemeId, Address beneficiary)
    {
        var scheme = State.SchemeInfos[schemeId];
        Assert(scheme != null, "Scheme not found.");
        beneficiary = beneficiary ?? Context.Sender;
        var profitDetails = State.ProfitDetailsMap[schemeId][beneficiary];

        if (profitDetails == null) return new GetAllProfitsMapOutput();

        // ReSharper disable once PossibleNullReferenceException
        var availableDetails = profitDetails.Details.Where(d =>
            d.LastProfitPeriod < scheme.CurrentPeriod && (d.LastProfitPeriod == 0
                ? d.EndPeriod >= d.StartPeriod
                : d.EndPeriod >= d.LastProfitPeriod)
        ).ToList();
        
        var profitableDetailCount =
            Math.Min(ProfitContractConstants.ProfitReceivingLimitForEachTime, availableDetails.Count);
        var maxProfitReceivingPeriodCount = GetMaximumPeriodCountForProfitableDetail(profitableDetailCount);

        var allProfitsDict = new Dictionary<string, long>();
        var claimableProfitsDict = new Dictionary<string, long>();
        for (var i = 0; i < availableDetails.Count; i++)
        {
            var profitDetail = availableDetails[i];
            if (profitDetail.LastProfitPeriod == 0) profitDetail.LastProfitPeriod = profitDetail.StartPeriod;
            
            var totalProfitsDictForEachProfitDetail = ProfitAllPeriods(scheme, profitDetail, beneficiary, profitDetail.EndPeriod.Sub(profitDetail.LastProfitPeriod),true);
            AddProfitToDict(allProfitsDict, totalProfitsDictForEachProfitDetail);
            if(i >= profitableDetailCount) continue;
            var claimableProfitsDictForEachProfitDetail = ProfitAllPeriods(scheme, profitDetail, beneficiary, maxProfitReceivingPeriodCount,true);
            AddProfitToDict(claimableProfitsDict, claimableProfitsDictForEachProfitDetail);
        }

        return new GetAllProfitsMapOutput
        {
            AllProfitsMap = { allProfitsDict },
            OneTimeClaimableProfitsMap = { claimableProfitsDict }
        };
    }
    
    private void AddProfitToDict(Dictionary<string, long> profitsDict, Dictionary<string,long> profitsToAdd)
    {
        foreach (var kv in profitsToAdd)
            if (profitsDict.ContainsKey(kv.Key))
                profitsDict[kv.Key] = profitsDict[kv.Key].Add(kv.Value);
            else
                profitsDict[kv.Key] = kv.Value;
    }

    public override Int32Value GetMaximumProfitReceivingPeriodCount(Empty input)
    {
        return new Int32Value
        {
            Value = GetMaximumProfitReceivingPeriodCount()
        };
    }
}