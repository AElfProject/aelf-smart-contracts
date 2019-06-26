using System.Linq;
using System.Threading.Tasks;
using Acs1;
using Acs5;
using AElf.Contracts.MultiToken.Messages;
using AElf.Contracts.TestContract.ProfitSharing;
using AElf.Contracts.TokenConverter;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace AElf.Contracts.EconomicSystem.Tests.BVT
{
    public partial class EconomicSystemTest : EconomicSystemTestBase
    {
        public EconomicSystemTest()
        {
            InitializeContracts();
        }

        [Fact]
        public async Task EconomistSystem_CheckBasicInformation()
        {
            // Treasury contract created Treasury Profit Item and set Profit Id to Profit Contract.
            ProfitItemsIds[ProfitType.Treasury].ShouldBe(await ProfitContractStub.GetTreasuryProfitId.CallAsync(new Empty()));
            var treasuryProfit = await ProfitContractStub.GetProfitItem.CallAsync(ProfitItemsIds[ProfitType.Treasury]);
            treasuryProfit.Creator.ShouldBe(TreasuryContractAddress);
            treasuryProfit.SubProfitItems.Count.ShouldBe(3);
            treasuryProfit.IsReleaseAllBalanceEverytimeByDefault.ShouldBe(true);

            // Token Converter Contract created AETC token.
            var tokenInformation = await TokenContractStub.GetTokenInfo.CallAsync(new GetTokenInfoInput
            {
                Symbol = EconomicSystemTestConstants.ConverterTokenSymbol
            });
            tokenInformation.Issuer.ShouldBe(TokenConverterContractAddress);
            tokenInformation.TotalSupply.ShouldBe(EconomicSystemTestConstants.TotalSupply);
        }

        [Fact]
        public async Task EconomistSystem_SetMethodTransactionFee()
        {
            const long feeAmount = 10L;
            await TransactionFeeChargingContractStub.SetMethodFee.SendAsync(new SetMethodFeeInput
            {
                Method = nameof(TransactionFeeChargingContractStub.SendForFun),
                SymbolToAmount = {{EconomicSystemTestConstants.NativeTokenSymbol, feeAmount}}
            });

            var tokenAmount = await TransactionFeeChargingContractStub.GetMethodFee.CallAsync(new MethodName
            {
                Name = nameof(TransactionFeeChargingContractStub.SendForFun)
            });
            tokenAmount.SymbolToAmount[EconomicSystemTestConstants.NativeTokenSymbol].ShouldBe(feeAmount);
        }

        [Fact]
        public async Task EconomistSystem_ChargeMethodTransactionFee()
        {
            await EconomistSystem_SetMethodTransactionFee();

            var chosenOneKeyPair = CoreDataCenterKeyPairs.First();
            var chosenOneAddress = Address.FromPublicKey(chosenOneKeyPair.PublicKey);
            var balanceBefore = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = chosenOneAddress,
                Symbol = EconomicSystemTestConstants.NativeTokenSymbol
            });
            var tycoon = GetTransactionFeeChargingContractStub(chosenOneKeyPair);
            await tycoon.SendForFun.SendAsync(new Empty());
            var balanceAfter = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = chosenOneAddress,
                Symbol = EconomicSystemTestConstants.NativeTokenSymbol
            });
            balanceAfter.Balance.ShouldBeLessThan(balanceBefore.Balance);
        }

        [Fact]
        public async Task<long> EconomistSystem_SetMethodTransactionFee_MultipleSymbol()
        {
            const long feeAmount = 10L;

            await TransactionFeeChargingContractStub.SetMethodFee.SendAsync(new SetMethodFeeInput
            {
                Method = nameof(TransactionFeeChargingContractStub.SendForFun),
                SymbolToAmount =
                {
                    {EconomicSystemTestConstants.TransactionFeeChargingContractTokenSymbol, feeAmount},
                    {EconomicSystemTestConstants.NativeTokenSymbol, feeAmount}
                }
            });

            var tokenAmount = await TransactionFeeChargingContractStub.GetMethodFee.CallAsync(new MethodName
                {Name = nameof(TransactionFeeChargingContractStub.SendForFun)});
            tokenAmount.SymbolToAmount[EconomicSystemTestConstants.NativeTokenSymbol].ShouldBe(feeAmount);
            tokenAmount.SymbolToAmount[EconomicSystemTestConstants.TransactionFeeChargingContractTokenSymbol]
                .ShouldBe(feeAmount);

            return feeAmount;
        }

        [Fact]
        public async Task EconomicSystem_ChangeMethodTransactionFee_MultipleSymbol()
        {
            var feeAmount = await EconomistSystem_SetMethodTransactionFee_MultipleSymbol();

            var chosenOneKeyPair = CoreDataCenterKeyPairs.First();
            var chosenOneAddress = Address.FromPublicKey(chosenOneKeyPair.PublicKey);
            
            {
                var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = chosenOneAddress,
                    Symbol = EconomicSystemTestConstants.TransactionFeeChargingContractTokenSymbol
                });
                balance.Balance.ShouldBe(0);
            }
            
            // The chosen one decide to buy some TFCC tokens.
            var chosenOneTokenContractStub =
                GetTester<TokenContractContainer.TokenContractStub>(TokenContractAddress, chosenOneKeyPair);
            await chosenOneTokenContractStub.Approve.SendAsync(new ApproveInput
            {
                Spender = TokenConverterContractAddress,
                Symbol = EconomicSystemTestConstants.NativeTokenSymbol,
                Amount = 100_000// Enough,
            });
            var chosenOneTokenConverterContractStub =
                GetTester<TokenConverterContractContainer.TokenConverterContractStub>(TokenConverterContractAddress,
                    chosenOneKeyPair);
            await chosenOneTokenConverterContractStub.Buy.SendAsync(new BuyInput
            {
                Symbol = EconomicSystemTestConstants.TransactionFeeChargingContractTokenSymbol,
                Amount = feeAmount
            });
            
            {
                var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = chosenOneAddress,
                    Symbol = EconomicSystemTestConstants.TransactionFeeChargingContractTokenSymbol
                });
                balance.Balance.ShouldBe(feeAmount);
            }
            
            var tycoon = GetTransactionFeeChargingContractStub(chosenOneKeyPair);
            await tycoon.SendForFun.SendAsync(new Empty());
            
            {
                var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = chosenOneAddress,
                    Symbol = EconomicSystemTestConstants.TransactionFeeChargingContractTokenSymbol
                });
                balance.Balance.ShouldBe(0L);
            }
        }
        
        [Fact]
        public async Task EconomistSystem_SetMethodProfitFee()
        {
            const long profitAmount = 100L;
            await ProfitSharingContractStub.SetMethodProfitFee.SendAsync(new SetMethodProfitFeeInput
            {
                Method = nameof(ProfitSharingContractStub.SendForFun),
                SymbolToAmount = {{EconomicSystemTestConstants.NativeTokenSymbol, profitAmount}}
            });

            var tokenAmount = await ProfitSharingContractStub.GetMethodProfitFee.CallAsync(new StringValue
            {
                Value = nameof(ProfitSharingContractStub.SendForFun)
            });
            tokenAmount.SymbolToAmount[EconomicSystemTestConstants.NativeTokenSymbol].ShouldBe(profitAmount);
        }
    }
}