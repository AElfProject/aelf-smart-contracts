using Acs1;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.Association
{
    public partial class AssociationContract
    {
        public override MethodFees GetMethodFee(StringValue input)
        {
            return State.TransactionFees[input.Value];
        }

        public override Empty SetMethodFee(MethodFees input)
        {
            RequiredMethodFeeControllerSet();

            Assert(Context.Sender == State.MethodFeeController.Value.OwnerAddress, "Unauthorized to set method fee.");
            State.TransactionFees[input.MethodName] = input;

            return new Empty();
        }
    }
}