using AElf.Contracts.MultiToken;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.TestContract.BasicFunctionWithParallel
{
    /// <summary>
    /// Action methods
    /// </summary>
    public partial class BasicFunctionWithParallelContract : BasicFunctionWithParallelContractContainer.BasicFunctionWithParallelContractBase
    {
        public override Empty InitialBasicFunctionWithParallelContract(InitialBasicFunctionWithParallelContractInput input)
        {
            Assert(!State.Initialized.Value, "Already initialized.");
            Assert(input.MinValue >0 && input.MaxValue >0 && input.MaxValue >= input.MinValue, "Invalid min/max value input setting.");
            
            State.Initialized.Value = true;
            State.ContractName.Value = input.ContractName;
            State.ContractManager.Value = input.Manager;
            State.MinBet.Value = input.MinValue;
            State.MaxBet.Value = input.MaxValue;
            State.MortgageBalance.Value = input.MortgageValue;

            return new Empty();
        }

        public override Empty UpdateBetLimit(BetLimitInput input)
        {
            Assert(Context.Sender == State.ContractManager.Value, "Only manager can perform this action."); 
            Assert(input.MinValue >0 && input.MaxValue >0 && input.MaxValue >= input.MinValue, "Invalid min/max value input setting.");
            
            State.MinBet.Value = input.MinValue;
            State.MaxBet.Value = input.MaxValue;

            return new Empty();
        }

        public override Empty UserPlayBet(BetInput input)
        {
            Assert(input.Int64Value >= State.MinBet.Value && input.Int64Value <=State.MaxBet.Value, $"Input balance not in boundary({State.MinBet.Value}, {State.MaxBet.Value}).");
            Assert(input.Int64Value > State.WinerHistory[Context.Sender], "Should bet bigger than your reward money.");
            State.TotalBetBalance.Value = State.TotalBetBalance.Value.Add(input.Int64Value);
            
            var result = WinOrLose(input.Int64Value);

            if (result == 0)
            {
                State.LoserHistory[Context.Sender] = State.LoserHistory[Context.Sender].Add(input.Int64Value);
            }
            else
            {
                State.RewardBalance.Value = State.RewardBalance.Value.Add(result);
                State.WinerHistory[Context.Sender] = State.WinerHistory[Context.Sender].Add(result);
            }
            
            return new Empty();
        }

        public override Empty LockToken(LockTokenInput input)
        {
            State.TokenContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            
            State.TokenContract.Lock.Send(new LockInput
            {
                Symbol = input.Symbol,
                Address = input.Address,
                Amount = input.Amount,
                LockId = input.LockId,
                Usage = input.Usage
            });
            
            return new Empty();
        }

        public override Empty UnlockToken(UnlockTokenInput input)
        {
            State.TokenContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            
            State.TokenContract.Unlock.Send(new UnlockInput
            {
                Symbol = input.Symbol,
                Address = input.Address,
                Amount = input.Amount,
                LockId = input.LockId,
                Usage = input.Usage
            });
            return new Empty();
        }

        public override Empty ValidateOrigin(Address address)
        {
            Assert(address == Context.Origin, "Validation failed, origin is not expected.");
            return new Empty();
        }

        public override Empty SetValue(SetValueInput input)
        {
            State.LongValueMap[input.Key] = input.Int64Value;
            State.StringValueMap[input.Key] = input.StringValue;
            State.BoolValueMap[input.Key] = input.BoolValue;
            State.MessageValueMap[input.Key] = input.MessageValue;
            return new Empty();
        }

        public override Empty SetValueWithPlugin(SetValueInput input)
        {
            return SetValue(input);
        }

        public override Empty RemoveValue(RemoveValueInput input)
        {
            State.LongValueMap.Remove(input.Key);
            State.StringValueMap.Remove(input.Key);
            State.BoolValueMap.Remove(input.Key);
            State.MessageValueMap.Remove(input.Key);
            return new Empty();
        }

        public override Empty RemoveValueWithPlugin(RemoveValueInput input)
        {
            return RemoveValue(input);
        }

        public override Empty RemoveAfterSetValue(RemoveAfterSetValueInput input)
        {
            State.LongValueMap[input.Key] = input.Int64Value;
            State.StringValueMap[input.Key] = input.StringValue;
            State.BoolValueMap[input.Key] = input.BoolValue;
            State.MessageValueMap[input.Key] = input.MessageValue;
            
            State.LongValueMap.Remove(input.Key);
            State.StringValueMap.Remove(input.Key);
            State.BoolValueMap.Remove(input.Key);
            State.MessageValueMap.Remove(input.Key);
            
            return new Empty();
        }

        public override Empty RemoveAfterSetValueWithPlugin(RemoveAfterSetValueInput input)
        {
            return RemoveAfterSetValue(input);
        }

        public override Empty SetAfterRemoveValue(SetAfterRemoveValueInput input)
        {
            State.LongValueMap.Remove(input.Key);
            State.StringValueMap.Remove(input.Key);
            State.BoolValueMap.Remove(input.Key);
            State.MessageValueMap.Remove(input.Key);
            
            State.LongValueMap[input.Key] = input.Int64Value;
            State.StringValueMap[input.Key] = input.StringValue;
            State.BoolValueMap[input.Key] = input.BoolValue;
            State.MessageValueMap[input.Key] = input.MessageValue;
            
            return new Empty();
        }

        public override Empty SetAfterRemoveValueWithPlugin(SetAfterRemoveValueInput input)
        {
            return SetAfterRemoveValue(input);
        }

        public override Empty IncreaseValue(IncreaseValueInput input)
        {
            var longValue = State.LongValueMap[input.Key];
            longValue++;
            State.LongValueMap[input.Key] = longValue;
            return new Empty();
        }

        public override Empty IncreaseValueParallel(IncreaseValueInput input)
        {
            return IncreaseValue(input);
        }

        public override Empty RemoveValueParallel(RemoveValueInput input)
        {
            return RemoveValue(input);
        }

        private long WinOrLose(long betAmount)
        {
            var data = State.TotalBetBalance.Value.Sub(State.RewardBalance.Value);
            if(data < 0)
                data = data *(-1);
                
            if (data % 100 == 1)
                return betAmount * 1000;
            if (data % 50 == 5)
                return betAmount * 50;
            return 0;
        }
    }
}