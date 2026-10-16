using System;
using System.Globalization;
using System.Linq;
using System.Text;
using WasmCSharp;

public static class Plugin
{
    public static DataFrame Parse(ReadOnlyMemory<byte> input)
    {
        var records = Encoding.UTF8
            .GetString(input.Span)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line => line.Split(',').Select(field => field.Trim()).ToArray())
            .ToArray();

        if (records.Length < 2 || records[0].Length < 2)
        {
            throw new FormatException("temperature CSV has no header or rows");
        }

        var columns = records[0][1..]
            .Select(name => name)
            .ToArray();
        var rows = records[1..]
            .Select(record =>
            {
                if (record.Length != columns.Length + 1)
                {
                    throw new FormatException("temperature row has the wrong number of fields");
                }

                return new DataFrameRow(
                    record[0],
                    record[1..]
                        .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                        .ToArray());
            })
            .ToArray();

        return new DataFrame(columns, rows);
    }
}
