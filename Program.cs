using WasmCSharp;

if (args.Length != 3 || !string.Equals(args[0], "parse", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: dotnet run -- parse <csv-file> <plugin.cs>");
    return 2;
}

var csvPath = Path.GetFullPath(args[1]);
var pluginPath = Path.GetFullPath(args[2]);

try
{
    if (!File.Exists(csvPath))
    {
        throw new FileNotFoundException("CSV input file was not found.", csvPath);
    }

    if (!File.Exists(pluginPath))
    {
        throw new FileNotFoundException("WebAssembly plugin was not found.", pluginPath);
    }

    var csv = await File.ReadAllBytesAsync(csvPath);
    var dataframe = await new CSharpPluginRunner().ParseAsync(pluginPath, csv);
    Console.WriteLine(dataframe.ToJson());
    Console.Error.WriteLine($"Parsed {dataframe.Rows.Length} rows and {dataframe.Columns.Length} columns.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"parse failed: {exception.Message}");
    return 1;
}
