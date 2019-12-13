using System.Linq;
using System.Threading.Tasks;
using AElf.Kernel;
using AElf.Kernel.SmartContract.Application;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AElf.Contracts.MultiToken
{
    public partial class MultiTokenContractTests
    {

        private async Task InitializeCoefficientAsync()
        {
            var initResult = (await TokenContractStub.InitializeCoefficient.SendAsync(new Empty())).TransactionResult;
            initResult.Status.ShouldBe(TransactionResultStatus.Mined);
        }
        [Fact]
        public async Task Tx_Token_Fee_Calculate_After_Update_Piecewise_Function_Test()
        {
            await InitializeCoefficientAsync();
            var calculateTxCostStrategy = Application.ServiceProvider.GetRequiredService<ICalculateTxCostStrategy>();
            var size = 10000;
            var fee = await calculateTxCostStrategy.GetCostAsync(null, size);
            fee.ShouldBe(12_5001_0000);
            var param = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Tx,
                FunctionType = CalculateFunctionTypeEnum.Liner,
                PieceKey = 1000000,
                CoefficientDic = {{"numerator", 1}, {"denominator", 400}}
            };

            await HandleTestAsync(param, null, null);
            var updatedFee = await calculateTxCostStrategy.GetCostAsync(null, size);
            updatedFee.ShouldBe(25_0000_0000);
            var apiParam2 = new CoefficientFromSender
            {
                IsChangePieceKey = true,
                IsLiner = false,
                PieceKey = 1000000,
                LinerCoefficient = new LinerCoefficient(),
                PowerCoefficient = new PowerCoefficient(),
                NewPieceKeyCoefficient = new NewPieceKeyCoefficient
                {
                    NewPieceKey = 100
                }
            };
            var result = (await TokenContractStub.UpdateCoefficientFromSender.SendAsync(apiParam2)).TransactionResult;
            result.Status.ShouldBe(TransactionResultStatus.Mined);
            var allParameters = await TokenContractStub.GetCalculateFeeCoefficientOfSender.CallAsync(new Empty());
            var changedState2 = allParameters.Coefficients.SingleOrDefault(x => x.PieceKey == apiParam2.NewPieceKeyCoefficient.NewPieceKey);
            changedState2.ShouldNotBeNull();
            var param2 = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Tx,
                PieceKey = 1000000
            };
            await HandleTestAsync(param2, null, null, 100);
            var pieceKeyChangedFee = await calculateTxCostStrategy.GetCostAsync(null, size);
            pieceKeyChangedFee.ShouldBe(9813_6250_0000);
        }

        [Fact]
        public async Task Cpu_Token_Fee_Calculate_After_Update_Piecewise_Function_Test()
        {
            await InitializeCoefficientAsync();
            var calculateCpuCostStrategy = Application.ServiceProvider.GetRequiredService<ICalculateCpuCostStrategy>();
            var apiParam = new CoefficientFromSender
            {
                IsLiner = true,
                PieceKey = 100_0000,
                LinerCoefficient = new LinerCoefficient
                {
                    Denominator = 4,
                    Numerator = 1
                }
            };
            var result = (await TokenContractStub.UpdateCoefficientFromSender.SendAsync(apiParam)).TransactionResult;
            result.Status.ShouldBe(TransactionResultStatus.Mined);
            var param = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Cpu,
                FunctionType = CalculateFunctionTypeEnum.Liner,
                PieceKey = 10,
                CoefficientDic = {{"numerator", 1}, {"denominator", 2}}
            };

            await HandleTestAsync(param, null, null);
            var size = 10;
            var updatedFee = await calculateCpuCostStrategy.GetCostAsync(null, size);
            updatedFee.ShouldBe(500000000);
            var param2 = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Cpu,
                PieceKey = 10,
            };
            await HandleTestAsync(param2, null, null, 50);
            var size2 = 50;
            var updatedFee2 = await calculateCpuCostStrategy.GetCostAsync(null, size2);
            updatedFee2.ShouldBe(2500000000);
        }

        [Fact]
        public async Task Ram_Token_Fee_Calculate_After_Update_Piecewise_Function_Test()
        {
            await InitializeCoefficientAsync();
            var calculateRamCostStrategy = Application.ServiceProvider.GetRequiredService<ICalculateRamCostStrategy>();
            var param = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Ram,
                FunctionType = CalculateFunctionTypeEnum.Liner,
                PieceKey = 10,
                CoefficientDic = {{"numerator", 1}, {"denominator", 2}}
            };

            await HandleTestAsync(param, null, null);
            var size = 10;
            var updatedFee = await calculateRamCostStrategy.GetCostAsync(null, size);
            updatedFee.ShouldBe(500000000);
            var param2 = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Ram,
                PieceKey = 10,
            };
            await HandleTestAsync(param2, null, null, 50);
            var size2 = 50;
            var updatedFee2 = await calculateRamCostStrategy.GetCostAsync(null, size2);
            updatedFee2.ShouldBe(2500000000);
        }

        [Fact]
        public async Task Sto_Token_Fee_Calculate_After_Update_Piecewise_Function_Test()
        {
            await InitializeCoefficientAsync();
            var calculateStoCostStrategy = Application.ServiceProvider.GetRequiredService<ICalculateStoCostStrategy>();
            var param = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Sto,
                FunctionType = CalculateFunctionTypeEnum.Liner,
                PieceKey = 1000000,
                CoefficientDic = {{"numerator", 1}, {"denominator", 400}}
            };

            await HandleTestAsync(param, null, null);
            var size = 10000;
            var updatedFee = await calculateStoCostStrategy.GetCostAsync(null, size);
            updatedFee.ShouldBe(25_0000_0000);
        }

        [Fact]
        public async Task Net_Token_Fee_Calculate_After_Update_Piecewise_Function_Test()
        {
            await InitializeCoefficientAsync();
            var calculateNetCostStrategy = Application.ServiceProvider.GetRequiredService<ICalculateNetCostStrategy>();
            var param = new CalculateFeeCoefficient
            {
                FeeType = FeeTypeEnum.Net,
                FunctionType = CalculateFunctionTypeEnum.Liner,
                PieceKey = 1000000,
                CoefficientDic = {{"numerator", 1}, {"denominator", 400}}
            };

            await HandleTestAsync(param, null, null);
            var size = 10000;
            var updatedFee = await calculateNetCostStrategy.GetCostAsync(null, size);
            updatedFee.ShouldBe(25_0000_0000);
        }

        private async Task HandleTestAsync(CalculateFeeCoefficient param, BlockIndex blockIndex, IChainContext chain,
            int newPieceKey = 0)
        {
            var selectedStrategy = param.FeeType switch
            {
                FeeTypeEnum.Tx => (ICalculateCostStrategy) Application.ServiceProvider
                    .GetRequiredService<ICalculateTxCostStrategy>(),
                FeeTypeEnum.Cpu => Application.ServiceProvider.GetRequiredService<ICalculateCpuCostStrategy>(),
                FeeTypeEnum.Ram => Application.ServiceProvider.GetRequiredService<ICalculateRamCostStrategy>(),
                FeeTypeEnum.Sto => Application.ServiceProvider.GetRequiredService<ICalculateStoCostStrategy>(),
                FeeTypeEnum.Net => Application.ServiceProvider.GetRequiredService<ICalculateNetCostStrategy>(),
                _ => null
            };

            if (selectedStrategy == null)
                return;


            if (newPieceKey > 0)
                await selectedStrategy.ChangeAlgorithmPieceKeyAsync(chain, blockIndex, param.PieceKey, newPieceKey);
            else
            {
                var pieceKey = param.PieceKey;
                var paramDic = param.CoefficientDic.ToDictionary(x => x.Key.ToLower(), x => x.Value);
                await selectedStrategy.ModifyAlgorithmAsync(chain, blockIndex, pieceKey, paramDic);
            }
        }
    }
}