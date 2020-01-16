using Acs1;
using AElf.Contracts.Parliament;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Profit
{
    public partial class ProfitContract
    {
        #region Views

        public override MethodFees GetMethodFee(StringValue input)
        {
            var methodFees = State.TransactionFees[input.Value];
            if (methodFees != null)
            {
                return methodFees;
            }

            switch (input.Value)
            {
                case nameof(CreateScheme):
                    return new MethodFees
                    {
                        Fees =
                        {
                            new MethodFee {Symbol = Context.Variables.NativeSymbol, BasicFee = 10_00000000}
                        }
                    };
                default:
                    return new MethodFees
                    {
                        Fees =
                        {
                            new MethodFee {Symbol = Context.Variables.NativeSymbol, BasicFee = 1_00000000}
                        }
                    };
            }
        }

        public override AuthorityStuff GetMethodFeeController(Empty input)
        {
            RequiredMethodFeeControllerSet();
            return State.MethodFeeController.Value;
        }

        #endregion
        
        public override Empty SetMethodFee(MethodFees input)
        {
            Assert(input.Fees.Count <= ProfitContractConstants.TokenAmountLimit, "Invalid input.");
            RequiredMethodFeeControllerSet();
            Assert(Context.Sender == State.MethodFeeController.Value.OwnerAddress, "Unauthorized to set method fee.");
            State.TransactionFees[input.MethodName] = input;

            return new Empty();
        }

        public override Empty ChangeMethodFeeController(AuthorityStuff input)
        {
            RequiredMethodFeeControllerSet();
            AssertSenderAddressWith(State.MethodFeeController.Value.OwnerAddress);
            var organizationExist = CheckOrganizationExist(input);
            Assert(organizationExist, "Invalid authority input.");

            State.MethodFeeController.Value = input;
            return new Empty();
        }

        #region private methods

        private void RequiredMethodFeeControllerSet()
        {
            if (State.MethodFeeController.Value != null) return;
            ValidateContractState(State.ParliamentContract, SmartContractConstants.ParliamentContractSystemName);

            var defaultAuthority = new AuthorityStuff
            {
                OwnerAddress = State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty()),
                ContractAddress = State.ParliamentContract.Value
            };

            State.MethodFeeController.Value = defaultAuthority;
        }

        private void AssertSenderAddressWith(Address address)
        {
            Assert(Context.Sender == address, "Unauthorized behavior.");
        }

        private bool CheckOrganizationExist(AuthorityStuff authorityStuff)
        {
            return Context.Call<BoolValue>(authorityStuff.ContractAddress,
                nameof(ParliamentContractContainer.ParliamentContractReferenceState.ValidateOrganizationExist),
                authorityStuff.OwnerAddress).Value;
        }

        #endregion
    }
}