using Wasmtime;

namespace WasmCSharp;

public sealed record WasiPluginResult(
    byte[] StandardOutput,
    string StandardError);

public sealed class WasiPluginRunner
{
    private readonly int _maximumOutputBytes;

    public WasiPluginRunner(int maximumOutputBytes = 10 * 1024 * 1024)
    {
        if (maximumOutputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
        }

        _maximumOutputBytes = maximumOutputBytes;
    }

    public async Task<WasiPluginResult> RunAsync(
        string wasmPath,
        ReadOnlyMemory<byte> standardInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wasmPath);
        cancellationToken.ThrowIfCancellationRequested();

        var workingDirectory = Directory.CreateTempSubdirectory("wasm-csv-");
        var inputPath = Path.Combine(workingDirectory.FullName, "stdin");
        var outputPath = Path.Combine(workingDirectory.FullName, "stdout");
        var errorPath = Path.Combine(workingDirectory.FullName, "stderr");

        try
        {
            await File.WriteAllBytesAsync(inputPath, standardInput.ToArray(), cancellationToken);

            var config = new Config().WithFuelConsumption(true);
            using var engine = new Engine(config);
            using var module = Module.FromFile(engine, wasmPath);
            using var store = new Store(engine);
            store.Fuel = 10_000_000;
            store.SetWasiConfiguration(
                new WasiConfiguration()
                    .WithStandardInput(inputPath)
                    .WithStandardOutput(outputPath)
                    .WithStandardError(errorPath));

            using var linker = new Linker(engine);
            linker.DefineWasi();
            var instance = linker.Instantiate(store, module);
            var start = instance.GetFunction("_start")
                ?? throw new InvalidOperationException(
                    "The WebAssembly plugin must export a WASI _start function.");

            try
            {
                start.Invoke();
            }
            catch (Exception exception)
            {
                var pluginError = File.Exists(errorPath)
                    ? await File.ReadAllTextAsync(errorPath, cancellationToken)
                    : string.Empty;
                var detail = string.IsNullOrWhiteSpace(pluginError)
                    ? exception.Message
                    : pluginError.Trim();
                throw new InvalidOperationException(
                    $"WASI plugin execution failed: {detail}",
                    exception);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var outputInfo = new FileInfo(outputPath);
            if (outputInfo.Exists && outputInfo.Length > _maximumOutputBytes)
            {
                throw new InvalidDataException(
                    $"The plugin output exceeds the {_maximumOutputBytes} byte limit.");
            }

            var output = outputInfo.Exists
                ? await File.ReadAllBytesAsync(outputPath, cancellationToken)
                : [];
            var error = File.Exists(errorPath)
                ? await File.ReadAllTextAsync(errorPath, cancellationToken)
                : string.Empty;

            return new WasiPluginResult(output, error);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }
}
