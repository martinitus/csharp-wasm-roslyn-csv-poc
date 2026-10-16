use std::io::{self, Read, Write};

fn main() -> io::Result<()> {
    let mut input = String::new();
    io::stdin().read_to_string(&mut input)?;
    let records: Vec<Vec<&str>> = input
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .map(|line| line.split('\t').map(str::trim).collect())
        .collect();

    if records.len() < 2 || records[0].len() < 2 {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "sunshine TSV has no header or columns"));
    }

    let timestamps = &records[0][1..];
    let columns: Vec<&str> = records.iter().skip(1).map(|record| record[0]).collect();
    if columns.len() == 0 || records.iter().any(|record| record.len() != timestamps.len() + 1) {
        return Err(io::Error::new(io::ErrorKind::InvalidData, "sunshine column has the wrong number of fields"));
    }

    let mut output = String::from("{\"columns\":[");
    for (index, column) in columns.iter().enumerate() {
        if index > 0 { output.push(','); }
        output.push('"');
        output.push_str(column);
        output.push('"');
    }
    output.push_str("],\"rows\":[");

    for (timestamp_index, timestamp) in timestamps.iter().enumerate() {
        if timestamp_index > 0 { output.push(','); }
        output.push_str("{\"timestamp\":\"");
        output.push_str(timestamp);
        output.push_str("\",\"values\":[");
        for (column_index, record) in records.iter().skip(1).enumerate() {
            if column_index > 0 { output.push(','); }
            record[timestamp_index + 1]
                .parse::<f64>()
                .map_err(|_| io::Error::new(io::ErrorKind::InvalidData, "sunshine value is not numeric"))?;
            output.push_str(record[timestamp_index + 1]);
        }
        output.push_str("]}");
    }

    output.push_str("]}");
    io::stdout().write_all(output.as_bytes())?;
    Ok(())
}
