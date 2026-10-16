# WasmCSharp

A proof-of-concept .NET CLI host for CSV importer plugins executed with Wasmtime and WASI. A plugin can support a format-specific CSV dialect while producing one validated dataframe shape for the host.

## Prerequisites

- .NET 10 SDK
- Rust with the `wasm32-wasip1` target, only required when rebuilding the sample plugins

## CLI

Pass a CSV file and a WASI plugin to the host:

```sh
dotnet run -- parse samples/temperature.csv plugins/bin/temperature_csv.wasm
dotnet run -- parse samples/sunshine.tsv plugins/bin/sunshine_tsv.wasm
```

The dataframe is written as JSON to stdout. Status and plugin diagnostics are written to stderr, so the JSON can be piped to another process.

## Plugin contract

Each plugin is a WebAssembly module that:

- Reads the input file from WASI stdin
- Writes a normalized dataframe to WASI stdout
- Writes diagnostics to WASI stderr
- Exports a WASI `_start` function

The stdout dataframe shape is:

```json
{
  "columns": ["Berlin", "London"],
  "rows": [
    { "timestamp": "2026-01-01T00:00:00Z", "values": [2.5, 7.0] }
  ]
}
```

Both web uploads and Kafka consumers can read their input bytes and call `WasiPluginRunner.RunAsync` without exposing transport details to plugins.

## Included examples

- `samples/temperature.csv` is comma-separated, row-major, and has leading blank lines plus commented metadata. `plugins/temperature_csv` parses it.
- `samples/sunshine.tsv` is tab-separated and column-major: each city has one row and timestamps are the columns. `plugins/sunshine_tsv` transposes it into the common dataframe shape.
- `samples/temperature-corrupt.csv` deliberately contains `not-a-temperature` where a numeric value is required. The temperature plugin rejects it, and the host exits with code `1` while reporting the plugin error.
- `plugins/bin/*.wasm` contains the compiled WASI plugin binaries.

Both valid examples produce 10 timestamp rows and 5 city columns for Berlin, London, New York, Tokyo, and Sydney.

To exercise the failure path:

```sh
dotnet run -- parse samples/temperature-corrupt.csv plugins/bin/temperature_csv.wasm
```

Expected result:

```text
parse failed: WASI plugin execution failed: Error: Custom { kind: InvalidData, error: "temperature value is not numeric" }
```

The exact Wasmtime/Rust error prefix may vary by toolchain, but the command must fail rather than produce a partial dataframe.

## Build the host

```sh
dotnet build
```

## Rebuild the sample plugins

The sample implementations use Rust and target WASI Preview 1, which matches the host's stdin/stdout contract:

```sh
rustup target add wasm32-wasip1
cargo build --release --target wasm32-wasip1 \
  --manifest-path plugins/temperature_csv/Cargo.toml
cargo build --release --target wasm32-wasip1 \
  --manifest-path plugins/sunshine_tsv/Cargo.toml
mkdir -p plugins/bin
cp plugins/temperature_csv/target/wasm32-wasip1/release/temperature_csv_plugin.wasm \
  plugins/bin/temperature_csv.wasm
cp plugins/sunshine_tsv/target/wasm32-wasip1/release/sunshine_tsv_plugin.wasm \
  plugins/bin/sunshine_tsv.wasm
```

## Package

The runtime is provided by the [`Wasmtime`](https://www.nuget.org/packages/Wasmtime) NuGet package.

## Security notes

Uploaded modules are untrusted code. Before using this in production, add strict limits for execution time, memory, input size, output size, and fuel. Do not preopen host directories or expose network-related host functions. Validate the plugin output against the expected DataFrame schema before creating a DataFrame.


