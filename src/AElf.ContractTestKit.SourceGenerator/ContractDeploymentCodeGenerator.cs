using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AElf.ContractTestKit.SourceGenerator;

[Generator]
public class ContractDeploymentCodeGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var configFile = context.AdditionalFiles.SingleOrDefault(at => at.Path.EndsWith("contract-test-config.json"));
        if (configFile == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CDG001", "Config File Missing",
                    "The config file 'contract-test-config.json' is missing.", "ContractDeployment",
                    DiagnosticSeverity.Error, true), Location.None));
            return;
        }

        var configText = configFile.GetText(context.CancellationToken)?.ToString();
        if (string.IsNullOrEmpty(configText))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CDG002", "Config File Empty",
                    "The config file 'contract-test-config.json' is empty.", "ContractDeployment",
                    DiagnosticSeverity.Error, true), Location.None));
            return;
        }

        var configJson = JsonDocument.Parse(configText);
        if (!configJson.RootElement.TryGetProperty("DeploySystemContractList", out var deploySystemContractList))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("CDG003", "DeploySystemContractList Missing",
                    "The 'DeploySystemContractList' section is missing in the config file.", "ContractDeployment",
                    DiagnosticSeverity.Error, true), Location.None));
            return;
        }

        foreach (var contract in deploySystemContractList.EnumerateArray())
        {
            var contractName = contract.GetString();
            if (string.IsNullOrEmpty(contractName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CDG004", "Contract Name Invalid",
                        "A contract name in 'DeploySystemContractList' is invalid.", "ContractDeployment",
                        DiagnosticSeverity.Error, true), Location.None));
                continue;
            }

            try
            {
                var dllName = $"AElf.Contracts.{contractName}.dll";
                var assembly = Assembly.GetExecutingAssembly();
                using var stream =
                    assembly.GetManifestResourceStream(
                        $"AElf.ContractTestKit.SourceGenerator.AElf.Contracts.{contractName}.dll");
                if (stream == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("CDG007", "Resource Not Found",
                            $"The embedded resource '{dllName}' could not be found.", "ContractDeployment",
                            DiagnosticSeverity.Error, true), Location.None));
                    continue;
                }

                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                var binaryContent = memoryStream.ToArray();

                var byteArrayString = string.Join(", ", binaryContent.Select(b => $"0x{b:X2}"));
                var sourceCode = $@"using Volo.Abp.DependencyInjection;
using AElf.ContractTestKit;

namespace AElf.Contracts.{contractName}.Tests;

public class {contractName}DeployingContractCodeProvider : IDeployingContractCodeProvider, ITransientDependency
{{
    public byte[] GetContractCode()
    {{
        return new byte[] {{ {byteArrayString} }};
    }}
}}";

                context.AddSource($"{contractName}DeployingContractCodeProvider.g.cs",
                    SourceText.From(sourceCode, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("CDG007", "Exception Occurred", $"An exception occurred: {ex.Message}",
                        "ContractDeployment", DiagnosticSeverity.Error, true), Location.None));
            }
        }
    }
}