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
            .Select(line => line.Split('\t').Select(field => field.Trim()).ToArray())
            .ToArray();

        if (records.Length < 2 || records[0].Length < 2)
        {
            throw new FormatException("sunshine TSV has no header or columns");
        }

        var timestamps = records[0][1..];
        var columns = records[1..]
            .Select(record => record[0])
            .ToArray();

        if (records[1..].Any(record => record.Length != timestamps.Length + 1))
        {
            throw new FormatException("sunshine column has the wrong number of fields");
        }

        var rows = timestamps
            .Select((timestamp, timestampIndex) => new DataFrameRow(
                timestamp,
                records[1..]
                    .Select(record => double.Parse(
                        record[timestampIndex + 1],
                        CultureInfo.InvariantCulture))
                    .ToArray()))
            .ToArray();

        return new DataFrame(columns, rows);
    }
}
