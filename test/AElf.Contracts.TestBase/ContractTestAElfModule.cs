using System.Threading.Tasks;
using AElf.CrossChain;
using AElf.Cryptography;
using AElf.Database;
using AElf.Kernel;
using AElf.Kernel.Account.Application;
using AElf.Kernel.ChainController;
using AElf.Kernel.ChainController.Application;
using AElf.Kernel.Consensus.Application;
using AElf.Kernel.Infrastructure;
using AElf.Kernel.Miner.Application;
using AElf.Kernel.Proposal;
using AElf.Kernel.SmartContract;
using AElf.Kernel.SmartContract.Application;
using AElf.Kernel.SmartContract.Infrastructure;
using AElf.Kernel.SmartContractExecution;
using AElf.Kernel.SmartContractExecution.Application;
using AElf.Kernel.TransactionPool;
using AElf.Kernel.TransactionPool.Infrastructure;
using AElf.Modularity;
using AElf.OS;
using AElf.OS.Network.Application;
using AElf.OS.Network.Infrastructure;
using AElf.Runtime.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Volo.Abp.EventBus;
using Volo.Abp.Modularity;

namespace AElf.Contracts.TestBase;

[DependsOn(
    typeof(CSharpRuntimeAElfModule),
    typeof(CoreOSAElfModule),
    typeof(KernelAElfModule),
    typeof(SmartContractAElfModule),
    typeof(SmartContractExecutionAElfModule),
    typeof(TransactionPoolAElfModule),
    typeof(ChainControllerAElfModule),
    typeof(AbpEventBusModule)
)]
public class ContractTestAElfModule : AElfModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        services.AddSingleton(o => Mock.Of<IAElfNetworkServer>());
        services.AddSingleton(o => Mock.Of<IPeerPool>());

        services.AddSingleton(o => Mock.Of<INetworkService>());

        // When testing contract and packaging transactions, no need to generate and schedule real consensus stuff.
        context.Services.AddSingleton(o => Mock.Of<IConsensusService>());
        context.Services.AddSingleton(o => Mock.Of<IConsensusScheduler>());

        var ecKeyPair = CryptoHelper.GenerateKeyPair();

        context.Services.AddTransient(o =>
        {
            var mockService = new Mock<IAccountService>();
            mockService.Setup(a => a.SignAsync(It.IsAny<byte[]>())).Returns<byte[]>(data =>
                Task.FromResult(CryptoHelper.SignWithPrivateKey(ecKeyPair.PrivateKey, data)));

            mockService.Setup(a => a.GetPublicKeyAsync()).ReturnsAsync(ecKeyPair.PublicKey);

            return mockService.Object;
        });

        context.Services.RemoveAll<IPreExecutionPlugin>();

        context.Services.AddSingleton<ISmartContractRunner, UnitTestCSharpSmartContractRunner>(provider =>
        {
            var option = provider.GetService<IOptions<RunnerOptions>>();
            return new UnitTestCSharpSmartContractRunner(
                option.Value.SdkDir);
        });
        context.Services.AddSingleton<IDefaultContractZeroCodeProvider, UnitTestContractZeroCodeProvider>();
        context.Services.AddSingleton<ISmartContractAddressService, UnitTestSmartContractAddressService>();
        context.Services
            .AddSingleton<ISmartContractAddressNameProvider, ParliamentSmartContractAddressNameProvider>();
        context.Services
            .AddSingleton<ISmartContractAddressNameProvider, CrossChainSmartContractAddressNameProvider>();

        context.Services.AddTransient<BasicTransactionValidationProvider>();
        services.AddTransient<ChainCreationService>();
        context.Services.Replace(ServiceDescriptor
            .Singleton<ITransactionExecutingService, PlainTransactionExecutingService>());
        services.AddSingleton<SmartContractRunnerContainer>();
        services.AddSingleton<IDefaultContractZeroCodeProvider, UnitTestContractZeroCodeProvider>();

        Configure<HostSmartContractBridgeContextOptions>(options =>
        {
            options.ContextVariables[ContextVariableDictionary.NativeSymbolName] = "ELF";
            options.ContextVariables["SymbolListToPayTxFee"] = "WRITE,STO,READ,NET";
        });

        context.Services.AddSingleton<IBlockAcceptedLogEventProcessor, ContractDeployedLogEventProcessor>();

        context.Services.AddSingleton<ContractDeployedLogEventProcessor>();
        context.Services.AddSingleton<CodeUpdatedLogEventProcessor>();
        context.Services.Replace(ServiceDescriptor
            .Singleton<ITransactionExecutingService, PlainTransactionExecutingService>());
        
        services.AddKeyValueDbContext<BlockchainKeyValueDbContext>(o => o.UseInMemoryDatabase());
        services.AddKeyValueDbContext<StateKeyValueDbContext>(o => o.UseInMemoryDatabase());

        services.AddTransient<BlockValidationProvider>();
        services.AddTransient<SystemTransactionValidationProvider>();
        // services.AddSingleton(p => Mock.Of<IAccountService>());
        
        services.AddTransient<ChainCreationService>();
        services.Replace(ServiceDescriptor
            .Singleton<ITransactionExecutingService, PlainTransactionExecutingService>());

        services.AddSingleton<ISmartContractAddressProvider, SmartContractAddressProvider>();
    }
}