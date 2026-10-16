# WasmCSharp

A proof-of-concept .NET CLI host for format-specific file importer components. Plugins are WebAssembly Components described by WIT and hosted through Wasmtime's native Component Model C API called directly from C# via P/Invoke.

The host accepts input bytes from the CLI today and can use the same `ComponentPluginRunner` from an HTTP upload or Kafka consumer. The plugin boundary does not use temporary files, stdin/stdout, OS pipes, named pipes, or a virtual filesystem.

## Prerequisites

- .NET 10 SDK
- Rust toolchain
- `wasm-tools` CLI
- The Rust `wasm32-unknown-unknown` target for rebuilding the sample components

## CLI

Pass a file and a compiled WebAssembly Component to the host:

```sh
dotnet run -- parse samples/temperature.csv plugins/bin/temperature_csv.component.wasm
dotnet run -- parse samples/sunshine.tsv plugins/bin/sunshine_tsv.component.wasm
```

The dataframe is written as JSON to stdout. Status and errors are written to stderr, so the JSON can be piped to another process.

## Component contract

The shared contract is [`wit/csv-importer.wit`](wit/csv-importer.wit):

```wit
parse: func(input: list<u8>) -> result<dataframe, parse-error>;
```

The result contains named columns and timestamp-indexed rows:

```json
{
  "columns": ["Berlin", "London"],
  "rows": [
    { "timestamp": "2026-01-01T00:00:00Z", "values": [2.5, 7.0] }
  ]
}
```

Parse failures are typed WIT errors, for example:

```text
parse failed: invalid-data: temperature value is not numeric: not-a-temperature
```

The initial contract intentionally accepts the complete input as `list<u8>`. Streaming is not part of this first version.

## Included examples

- `samples/temperature.csv` is comma-separated, row-major, and has leading blank lines plus commented metadata. `plugins/temperature_csv` parses it.
- `samples/sunshine.tsv` is tab-separated and column-major: each city has one row and timestamps are the columns. `plugins/sunshine_tsv` transposes it into the common dataframe shape.
- `samples/temperature-corrupt.csv` deliberately contains `not-a-temperature`; the component returns `parse-error.invalid-data` and the host exits with code `1`.
- `plugins/bin/*.component.wasm` contains the compiled sample components.

Both valid examples produce 10 timestamp rows and 5 city columns for Berlin, London, New York, Tokyo, and Sydney.

## Build the host

```sh
dotnet build
```

The `Wasmtime` NuGet package supplies the native `libwasmtime` RID asset used by the C# P/Invoke layer. The host enables the native Component Model API and manually marshals the WIT values required by this contract.

## Rebuild the sample components

The Rust implementations use `wit-bindgen` and share the repository WIT contract. They are compiled as core Wasm for `wasm32-unknown-unknown`, then wrapped into Components using the embedded WIT metadata:

```sh
rustup target add wasm32-unknown-unknown

cargo build --release --target wasm32-unknown-unknown \
  --manifest-path plugins/temperature_csv/Cargo.toml
cargo build --release --target wasm32-unknown-unknown \
  --manifest-path plugins/sunshine_tsv/Cargo.toml

mkdir -p plugins/bin
wasm-tools component new \
  plugins/temperature_csv/target/wasm32-unknown-unknown/release/temperature_csv_plugin.wasm \
  -o plugins/bin/temperature_csv.component.wasm
wasm-tools component new \
  plugins/sunshine_tsv/target/wasm32-unknown-unknown/release/sunshine_tsv_plugin.wasm \
  -o plugins/bin/sunshine_tsv.component.wasm
```

## Runtime security

Uploaded components are untrusted code. Before production use, add strict input-size, output-size, memory, fuel, and execution-time limits. Keep the linker free of host imports unless explicitly required, validate the returned dataframe schema, and isolate plugin engines/stores according to the service's concurrency and tenancy model.

## Package

The runtime is provided by the [`Wasmtime`](https://www.nuget.org/packages/Wasmtime) NuGet package. The managed package's high-level Component Model API is not used; the project calls the native Component Model C API directly from C#.
