using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace WasmCSharp;

public sealed class CSharpPluginRunner
{
    public async Task<DataFrame> ParseAsync(
        string sourcePath,
        ReadOnlyMemory<byte> input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
        var references = GetMetadataReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: $"CsvPlugin_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true));

        await using var assemblyStream = new MemoryStream();
        var emitResult = compilation.Emit(assemblyStream);
        if (!emitResult.Success)
        {
            var diagnostics = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));
            throw new InvalidDataException($"C# plugin compilation failed:{Environment.NewLine}{diagnostics}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        assemblyStream.Position = 0;
        var loadContext = new PluginLoadContext();
        try
        {
            var assembly = loadContext.LoadFromStream(assemblyStream);
            var pluginType = assembly.GetType("Plugin", throwOnError: false)
                ?? throw new InvalidDataException("C# plugin must define a public type named Plugin.");
            var parseMethod = pluginType.GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(ReadOnlyMemory<byte>)],
                modifiers: null)
                ?? throw new InvalidDataException(
                    "C# plugin must define public static DataFrame Parse(ReadOnlyMemory<byte> input).");

            if (parseMethod.ReturnType != typeof(DataFrame))
            {
                throw new InvalidDataException(
                    "C# plugin Parse method must return WasmCSharp.DataFrame.");
            }

            try
            {
                var result = parseMethod.Invoke(null, [input]) as DataFrame
                    ?? throw new InvalidDataException("C# plugin returned a null dataframe.");
                result.Validate();
                return result;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidDataException(
                    $"C# plugin execution failed: {exception.InnerException.Message}",
                    exception.InnerException);
            }
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static IReadOnlyList<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(typeof(DataFrame).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext() : base(isCollectible: true)
        {
        }
    }
}
