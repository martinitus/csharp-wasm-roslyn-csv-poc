using System.Text.Json;

namespace WasmCSharp;

public sealed record DataFrame(
    string[] Columns,
    DataFrameRow[] Rows)
{
    public void Validate()
    {
        if (Columns.Length == 0)
        {
            throw new InvalidDataException("The dataframe must contain at least one column.");
        }

        if (Columns.Any(string.IsNullOrWhiteSpace) || Columns.Distinct(StringComparer.Ordinal).Count() != Columns.Length)
        {
            throw new InvalidDataException("Dataframe columns must have unique, non-empty names.");
        }

        foreach (var row in Rows)
        {
            if (string.IsNullOrWhiteSpace(row.Timestamp))
            {
                throw new InvalidDataException("Every dataframe row must have a timestamp.");
            }

            if (row.Values.Length != Columns.Length)
            {
                throw new InvalidDataException(
                    $"Row '{row.Timestamp}' contains {row.Values.Length} values; expected {Columns.Length}.");
            }
        }
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

public sealed record DataFrameRow(
    string Timestamp,
    double[] Values);
