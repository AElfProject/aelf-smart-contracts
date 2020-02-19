using System;
using System.Threading.Tasks;
using AElf.Contracts.TestContract.BasicSecurity;
using AElf.Kernel;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace AElf.Contract.TestContract
{
    public class ContractSecurityTests : TestContractTestBase
    {
        public ContractSecurityTests()
        {
            InitializeTestContracts();
        }

        [Fact]
        public async Task Int32_OverFlow_UpperLimit_Test()
        {
            await TestBasicSecurityContractStub.TestInt32State.SendAsync(
                new Int32Input
                {
                    Int32Value = Int32.MaxValue
                });

            var resultValue = (await TestBasicSecurityContractStub.QueryInt32State.CallAsync(
                new Empty())).Int32Value;
            resultValue.ShouldBe(Int32.MaxValue);

            var transactionResult = (await TestBasicSecurityContractStub.TestInt32State.SendWithExceptionAsync(
                new Int32Input
                {
                    Int32Value = 100
                })).TransactionResult;
            transactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            transactionResult.Error.Contains("System.OverflowException").ShouldBeTrue();

            //state not change
            var resultValue1 = (await TestBasicSecurityContractStub.QueryInt32State.CallAsync(
                new Empty())).Int32Value;
            resultValue1.ShouldBe(Int32.MaxValue);
        }

        [Fact]
        public async Task Int32_OverFlow_LowerLimit_Test()
        {
            await TestBasicSecurityContractStub.TestInt32State.SendAsync(
                new Int32Input
                {
                    Int32Value = int.MinValue
                });

            var resultValue = (await TestBasicSecurityContractStub.QueryInt32State.CallAsync(
                new Empty())).Int32Value;
            resultValue.ShouldBe(int.MinValue);

            var transactionResult = (await TestBasicSecurityContractStub.TestInt32State.SendWithExceptionAsync(
                new Int32Input
                {
                    Int32Value = -100
                })).TransactionResult;
            transactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            transactionResult.Error.Contains("System.OverflowException").ShouldBeTrue();

            //state not change
            var resultValue1 = (await TestBasicSecurityContractStub.QueryInt32State.CallAsync(
                new Empty())).Int32Value;
            resultValue1.ShouldBe(int.MinValue);
        }

        [Fact]
        public async Task Int64_OverFlow_UpperLimit_Test()
        {
            await TestBasicSecurityContractStub.TestInt64State.SendAsync(
                new Int64Input()
                {
                    Int64Value = Int64.MaxValue
                });

            var resultValue = (await TestBasicSecurityContractStub.QueryInt64State.CallAsync(
                new Empty())).Int64Value;
            resultValue.ShouldBe(Int64.MaxValue);

            var transactionResult = (await TestBasicSecurityContractStub.TestInt64State.SendWithExceptionAsync(
                new Int64Input
                {
                    Int64Value = 100
                })).TransactionResult;
            transactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            transactionResult.Error.Contains("System.OverflowException").ShouldBeTrue();

            //state not change
            var resultValue1 = (await TestBasicSecurityContractStub.QueryInt64State.CallAsync(
                new Empty())).Int64Value;
            resultValue1.ShouldBe(Int64.MaxValue);
        }
        
        [Fact]
        public async Task Int64_OverFlow_LowerLimit_Test()
        {
            await TestBasicSecurityContractStub.TestInt64State.SendAsync(
                new Int64Input()
                {
                    Int64Value = long.MinValue
                });

            var resultValue = (await TestBasicSecurityContractStub.QueryInt64State.CallAsync(
                new Empty())).Int64Value;
            resultValue.ShouldBe(long.MinValue);

            var transactionResult = (await TestBasicSecurityContractStub.TestInt64State.SendWithExceptionAsync(
                new Int64Input
                {
                    Int64Value = -100
                })).TransactionResult;
            transactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            transactionResult.Error.Contains("System.OverflowException").ShouldBeTrue();

            //state not change
            var resultValue1 = (await TestBasicSecurityContractStub.QueryInt64State.CallAsync(
                new Empty())).Int64Value;
            resultValue1.ShouldBe(long.MinValue);
        }

        [Fact]
        public async Task ResetFields()
        {
            var result = await TestBasicSecurityContractStub.TestResetFields.SendAsync(new ResetInput
            {
                BoolValue = true,
                Int32Value = 100,
                Int64Value = 1000,
                StringValue = "TEST"
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            var int64 = await TestBasicSecurityContractStub.QueryInt64State.CallAsync(new Empty());
            var s = await TestBasicSecurityContractStub.QueryStringState.CallAsync(new Empty());
            var constValue = await TestBasicSecurityContractStub.QueryConst.CallAsync(new Empty());
            int64.Int64Value.Equals(constValue.Int64Const).ShouldBeTrue();
            s.StringValue.Equals(constValue.StringConst).ShouldBeTrue();
            
            var fields = await TestBasicSecurityContractStub.QueryFields.CallAsync(new Empty());
            fields.BoolValue.ShouldBeFalse();
            fields.Int32Value.ShouldBe(0);
            fields.Int64Value.ShouldBe(0);
            fields.StringValue.ShouldBe(string.Empty);
        }
    }
}