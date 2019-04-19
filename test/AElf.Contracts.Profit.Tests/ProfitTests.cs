using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken.Messages;
using AElf.Kernel;
using Shouldly;
using Xunit;

namespace AElf.Contracts.Profit
{
    public class ProfitTests : ProfitContractTestBase
    {
        public ProfitTests()
        {
            InitializeContracts();
        }

        [Fact]
        public async Task ProfitContract_CheckTreasury()
        {
            await CreateTreasury();

            var treasury = await ProfitContractStub.GetProfitItem.CallAsync(TreasuryHash);

            treasury.Creator.ShouldBe(Address.FromPublicKey(StarterKeyPair.PublicKey));
            treasury.TokenSymbol.ShouldBe(ProfitContractTestConsts.NativeTokenSymbol);
            treasury.TotalAmount.ShouldBe((long) (ProfitContractTestConsts.NativeTokenTotalSupply * 0.2));

            var treasuryAddress = await ProfitContractStub.GetProfitItemVirtualAddress.CallAsync(
                new GetProfitItemVirtualAddressInput
                {
                    ProfitId = TreasuryHash
                });
            var treasuryBalance = (await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Symbol = ProfitContractTestConsts.NativeTokenSymbol,
                Owner = treasuryAddress
            })).Balance;

            treasuryBalance.ShouldBe((long) (ProfitContractTestConsts.NativeTokenTotalSupply * 0.2));
        }

        [Fact]
        public async Task ProfitContract_CreateProfitItem()
        {
            var creator = Creators[0];
            var creatorAddress = Address.FromPublicKey(CreatorMinerKeyPair[0].PublicKey);

            await creator.CreateProfitItem.SendAsync(new CreateProfitItemInput
            {
                TokenSymbol = ProfitContractTestConsts.NativeTokenSymbol,
            });

            var createdProfitIds = (await creator.GetCreatedProfitItems.CallAsync(new GetCreatedProfitItemsInput
            {
                Creator = creatorAddress
            })).ProfitIds;

            createdProfitIds.Count.ShouldBe(1);

            var profitId = createdProfitIds.First();
            var profitItem = await creator.GetProfitItem.CallAsync(profitId);

            profitItem.Creator.ShouldBe(creatorAddress);
            profitItem.TokenSymbol.ShouldBe(ProfitContractTestConsts.NativeTokenSymbol);
            profitItem.CurrentPeriod.ShouldBe(1);
            profitItem.ExpiredPeriodNumber.ShouldBe(ProfitContractConsts.DefaultExpiredPeriodNumber);
            profitItem.TotalWeight.ShouldBe(0);
            profitItem.TotalAmount.ShouldBe(0);

            var itemBalance = (await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Symbol = ProfitContractTestConsts.NativeTokenSymbol,
                Owner = profitItem.VirtualAddress
            })).Balance;

            Assert.Equal(0, itemBalance);
        }

        /// <summary>
        /// Of course it's okay for an address to creator many profit items.
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task ProfitContract_CreateManyProfitItems()
        {
            const int createTimes = 5;

            var creator = Creators[0];
            var creatorAddress = Address.FromPublicKey(CreatorMinerKeyPair[0].PublicKey);

            for (var i = 0; i < createTimes; i++)
            {
                var executionResult = await creator.CreateProfitItem.SendAsync(new CreateProfitItemInput
                {
                    TokenSymbol = ProfitContractTestConsts.NativeTokenSymbol,
                });
                executionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);
            }

            var createdProfitIds = await creator.GetCreatedProfitItems.CallAsync(new GetCreatedProfitItemsInput
            {
                Creator = creatorAddress
            });

            createdProfitIds.ProfitIds.Count.ShouldBe(createTimes);
        }

        [Fact]
        public async Task ProfitContract_CreateProfitItemWithInvalidTokenSymbol()
        {
            var creator = Creators[0];

            var executionResult = await creator.CreateProfitItem.SendAsync(new CreateProfitItemInput
            {
                TokenSymbol = "WTF"
            });

            executionResult.TransactionResult.Status.ShouldBe(TransactionResultStatus.Failed);
            executionResult.TransactionResult.Error.ShouldContain("Invalid token symbol.");
        }

        [Fact]
        public async Task ProfitContract_AddProfits()
        {
            const int amount = 1000;
            var creator = Creators[0];
            var tokenContractStub = GetTokenContractTester(CreatorMinerKeyPair[0]);

            var profitId = await CreateProfitItem();

            // Add profits to virtual address of this profit item.
            await creator.AddProfits.SendAsync(new AddProfitsInput
            {
                ProfitId = profitId,
                Amount = amount,
            });

            // Check profit item and corresponding balance.
            {
                var profitItem = await creator.GetProfitItem.CallAsync(profitId);
                Assert.Equal(amount, profitItem.TotalAmount);

                var balance = (await tokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = profitItem.VirtualAddress,
                    Symbol = ProfitContractTestConsts.NativeTokenSymbol
                })).Balance;
                balance.ShouldBe(amount);
            }

            // Add profits to release profits virtual address of this profit item.
            const int period = 3;
            await creator.AddProfits.SendAsync(new AddProfitsInput
            {
                ProfitId = profitId,
                Amount = amount,
                Period = period
            });

            // Check profit item and corresponding balance.
            {
                var profitItem = await creator.GetProfitItem.CallAsync(profitId);
                // Total amount stay.
                profitItem.TotalAmount.ShouldBe(amount);

                var virtualAddress = await creator.GetProfitItemVirtualAddress.CallAsync(
                    new GetProfitItemVirtualAddressInput
                    {
                        ProfitId = profitId,
                        Period = period
                    });
                var balance = (await tokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = virtualAddress,
                    Symbol = ProfitContractTestConsts.NativeTokenSymbol
                })).Balance;
                balance.ShouldBe(amount);

                var releasedProfitInformation = await creator.GetReleasedProfitsInformation.CallAsync(
                    new GetReleasedProfitsInformationInput
                    {
                        ProfitId = profitId,
                        Period = period
                    });
                releasedProfitInformation.IsReleased.ShouldBe(false);
                releasedProfitInformation.TotalWeight.ShouldBe(0);
                releasedProfitInformation.ProfitsAmount.ShouldBe(amount);
            }
        }

        private async Task<Hash> CreateProfitItem()
        {
            var creator = Creators[0];
            var creatorAddress = Address.FromPublicKey(CreatorMinerKeyPair[0].PublicKey);

            await creator.CreateProfitItem.SendAsync(new CreateProfitItemInput
            {
                TokenSymbol = ProfitContractTestConsts.NativeTokenSymbol,
            });

            var createdProfitIds = (await creator.GetCreatedProfitItems.CallAsync(new GetCreatedProfitItemsInput
            {
                Creator = creatorAddress
            })).ProfitIds;

            return createdProfitIds.First();
        }
    }
}