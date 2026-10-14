using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NsdRpc;
using NsdRpc.Generators;

namespace NsdRpc.Generators.Tests;

internal static class GeneratorRunner
{
    public const string AttributeSource = """
        #if !NSDRPC_GENERATORS
        namespace NsdRpc
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Interface)]
            internal sealed class RpcServiceAttribute : global::System.Attribute
            {
            }
        }
        #endif
        """;

    public static (Compilation Compilation, GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> Diagnostics)
        Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RpcServiceGenerator().AsSourceGenerator();
        AdditionalText[] additionalTexts =
        [
            new InMemoryAdditionalText("RpcServiceAttribute.cs", AttributeSource),
        ];

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator],
            additionalTexts,
            parseOptions: (CSharpParseOptions)syntaxTree.Options);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();
        return (output, result, diagnostics);
    }

    private static IEnumerable<MetadataReference> References()
    {
        string tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        foreach (string path in tpa.Split(Path.PathSeparator))
        {
            yield return MetadataReference.CreateFromFile(path);
        }

        yield return MetadataReference.CreateFromFile(typeof(RpcClient).Assembly.Location);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return SourceText.From(text);
        }
    }
}
