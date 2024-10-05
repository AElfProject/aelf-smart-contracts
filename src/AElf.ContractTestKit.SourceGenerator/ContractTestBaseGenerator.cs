using Microsoft.CodeAnalysis;

namespace AElf.ContractTestKit.SourceGenerator;

[Generator]
public class ContractTestBaseGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var configFile =
            context.AdditionalFiles.Single(at => at.Path.EndsWith("contract-test-config.json"));
        
    }
}