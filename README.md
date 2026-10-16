# WasmCSharp

A proof-of-concept .NET CLI host for format-specific file importer plugins written as ordinary C# source files. The host compiles each provider at runtime with Roslyn, loads it into a collectible `AssemblyLoadContext`, invokes its `Parse` method, and validates the returned dataframe.

This approach is intended for trusted provider code. It is more user-friendly than requiring Rust, WIT, or a WebAssembly toolchain, but runtime-compiled C# is **not a security sandbox**.

## Prerequisites

- .NET 10 SDK

## CLI

Pass an input file and a provider source file to the host:

```sh
dotnet run -- parse samples/temperature.csv plugins/temperature_csv/parse.cs
dotnet run -- parse samples/sunshine.tsv plugins/sunshine_tsv/parse.cs
```

The dataframe is written as JSON to stdout. Status and errors are written to stderr, so the JSON can be piped to another process.

## Provider contract

A provider is one C# source file defining a public static `Plugin` class with this method:

```csharp
public static DataFrame Parse(ReadOnlyMemory<byte> input)
```

The provider can use the host's public `WasmCSharp.DataFrame` and `WasmCSharp.DataFrameRow` types. The host validates the returned dataframe after invocation.

Example:

```csharp
using System;
using System.Text;
using WasmCSharp;

public static class Plugin
{
    public static DataFrame Parse(ReadOnlyMemory<byte> input)
    {
        var text = Encoding.UTF8.GetString(input.Span);
        // Format-specific parsing here.
        return new DataFrame(
            ["Berlin"],
            [new DataFrameRow("2026-01-01T00:00:00Z", [2.5])]);
    }
}
```

The web-upload and Kafka paths can pass their received bytes to the same `CSharpPluginRunner.ParseAsync` method. They do not need to know anything about the provider's source code.

## Included examples

- `samples/temperature.csv` is comma-separated, row-major, and has leading blank lines plus commented metadata. `plugins/temperature_csv/parse.cs` parses it.
- `samples/sunshine.tsv` is tab-separated and column-major: each city has one row and timestamps are the columns. `plugins/sunshine_tsv/parse.cs` transposes it into the common dataframe shape.
- `samples/temperature-corrupt.csv` deliberately contains `not-a-temperature`; the provider throws and the host exits with code `1`.

Both valid examples produce 10 timestamp rows and 5 city columns for Berlin, London, New York, Tokyo, and Sydney.

## Build the host

```sh
dotnet build
```

Roslyn is provided by the [`Microsoft.CodeAnalysis.CSharp`](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp) NuGet package. Provider source files under `plugins/` are excluded from the host's normal SDK-style compilation and are compiled only when selected by the CLI.

## Failure example

```sh
dotnet run -- parse samples/temperature-corrupt.csv plugins/temperature_csv/parse.cs
```

Expected result:

```text
parse failed: C# plugin execution failed: The input string 'not-a-temperature' was not in a correct format.
```

## Security model

Runtime-compiled C# is trusted code execution. `AssemblyLoadContext` provides assembly loading and unloadability, but it is not a security boundary. A provider can potentially access the host process, filesystem, network, environment, reflection, and other .NET APIs.

Only use this mode for trusted providers. If providers are untrusted or user-uploaded, use an isolated process/container or return to a sandboxed runtime such as WebAssembly Components.
