using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AElf.ContractTestKit.SourceGenerator;

[Generator]
public class ContractTestModuleGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var assemblyName = context.Compilation.AssemblyName;
        var contractName = assemblyName.Split('.').SkipLast(1).Last();
        var sourceCode = $@"using AElf.Contracts.TestBase;
using Volo.Abp.Modularity;
using AElf.ContractTestBase.ContractTestKit;
using AElf.Kernel.Consensus.AEDPoS;
using AElf.Kernel.SmartContract;

namespace AElf.Contracts.{contractName}.Tests;

[DependsOn(typeof(ContractTestAElfModule))]
public class {contractName}ContractTestAElfModule : ContractTestAElfModule
{{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {{
        Configure<ConsensusOptions>(options =>
        {{
            options.MiningInterval = 4000;
            options.InitialMinerList =
                SampleAccount.Accounts.Take(5).Select(a => a.KeyPair.PublicKey.ToHex()).ToList();
        }});
        Configure<ContractOptions>(o => o.ContractDeploymentAuthorityRequired = false);
    }}
}}";

        context.AddSource($"{contractName}ContractTestAElfModule.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
    }
}