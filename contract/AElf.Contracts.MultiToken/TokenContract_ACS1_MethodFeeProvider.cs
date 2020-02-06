using System.Collections.Generic;
using System.Linq;
using Acs1;
using Acs3;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.MultiToken
{
    public partial class TokenContract
    {
        #region Views
        
        public override MethodFees GetMethodFee(StringValue input)
        {
            var officialTokenContractAddress =
                Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            var primaryTokenSymbol =
                Context.Call<StringValue>(officialTokenContractAddress, nameof(GetPrimaryTokenSymbol), new Empty())
                    .Value;
            if (primaryTokenSymbol == string.Empty)
            {
                return new MethodFees();
            }

            if (input.Value == nameof(Transfer) || input.Value == nameof(TransferFrom))
            {
                var methodFees = State.MethodFees[input.Value];
                if (methodFees == null) return new MethodFees();
                var symbols = GetMethodFeeSymbols();
                var fees = methodFees.Fees.Where(f => symbols.Contains(f.Symbol));
                return new MethodFees
                {
                    MethodName = input.Value,
                    Fees =
                    {
                        fees
                    }
                };
            }

            return State.MethodFees[input.Value] ?? new MethodFees();
        }

        public override AuthorityInfo GetMethodFeeController(Empty input)
        {
            RequiredMethodFeeControllerSet();
            return State.MethodFeeController.Value;
        }

        #endregion

        public override Empty SetMethodFee(MethodFees input)
        {
            foreach (var symbolToAmount in input.Fees)
            {
                AssertValidToken(symbolToAmount.Symbol, symbolToAmount.BasicFee);
            }
            RequiredMethodFeeControllerSet();
            Assert(Context.Sender == State.MethodFeeController.Value.OwnerAddress, "Unauthorized to set method fee.");

            State.MethodFees[input.MethodName] = input;
            return new Empty();
        }

        public override Empty ChangeMethodFeeController(AuthorityInfo input)
        {
            RequiredMethodFeeControllerSet();
            AssertSenderAddressWith(State.MethodFeeController.Value.OwnerAddress);
            var organizationExist = CheckOrganizationExist(input);
            Assert(organizationExist, "Invalid authority input.");

            State.MethodFeeController.Value = input;
            return new Empty();
        }

        #region private methods

        private List<string> GetMethodFeeSymbols()
        {
            var symbols = new List<string>();
            var primaryTokenSymbol = GetPrimaryTokenSymbol(new Empty()).Value;
            if (primaryTokenSymbol != string.Empty) symbols.Add(primaryTokenSymbol);
            return symbols;
        }

        private void RequiredMethodFeeControllerSet()
        {
            if (State.MethodFeeController.Value != null) return;
            if (State.ParliamentContract.Value == null)
            {
                State.ParliamentContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ParliamentContractSystemName);
            }

            var defaultAuthority = new AuthorityInfo();

            // Parliament Auth Contract maybe not deployed.
            if (State.ParliamentContract.Value != null)
            {
                defaultAuthority.OwnerAddress =
                    State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());
                defaultAuthority.ContractAddress = State.ParliamentContract.Value;
            }

            State.MethodFeeController.Value = defaultAuthority;
        }

        private void AssertSenderAddressWith(Address address)
        {
            Assert(Context.Sender == address, "Unauthorized behavior.");
        }

        private bool CheckOrganizationExist(AuthorityInfo authorityInfo)
        {
            return Context.Call<BoolValue>(authorityInfo.ContractAddress,
                nameof(AuthorizationContractContainer.AuthorizationContractReferenceState.ValidateOrganizationExist),
                authorityInfo.OwnerAddress).Value;
        }

        #endregion
    }
}