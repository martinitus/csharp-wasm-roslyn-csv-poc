use std::io::{self, Read, Write};

fn main() -> io::Result<()> {
    let mut input = String::new();
    io::stdin().read_to_string(&mut input)?;
    let records: Vec<Vec<&str>> = input
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .map(|line| line.split(',').map(str::trim).collect())
        .collect();

    if records.len() < 2 || records[0].len() < 2 {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "temperature CSV has no header or rows"));
    }

    let columns = &records[0][1..];
    let mut output = String::from("{\"columns\":[");
    for (index, column) in columns.iter().enumerate() {
        if index > 0 { output.push(','); }
        output.push('"');
        output.push_str(column);
        output.push('"');
    }
    output.push_str("],\"rows\":[");

    for (row_index, record) in records.iter().skip(1).enumerate() {
        if record.len() != columns.len() + 1 {
            return Err(io::Error::new(io::ErrorKind::InvalidData, "temperature row has the wrong number of fields"));
        }
        if row_index > 0 { output.push(','); }
        output.push_str("{\"timestamp\":\"");
        output.push_str(record[0]);
        output.push_str("\",\"values\":[");
        for (value_index, value) in record.iter().skip(1).enumerate() {
            if value_index > 0 { output.push(','); }
            value.parse::<f64>()
                .map_err(|_| io::Error::new(io::ErrorKind::InvalidData, "temperature value is not numeric"))?;
            output.push_str(value);
        }
        output.push_str("]}");
    }

    output.push_str("]}");
    io::stdout().write_all(output.as_bytes())?;
    Ok(())
}
