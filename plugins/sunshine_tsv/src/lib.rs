wit_bindgen::generate!({
    path: "../../wit",
    world: "csv-importer",
});

use exports::csv::plugin::importer::{Column, Dataframe, Guest, ParseError, Row};

struct SunshineImporter;

impl Guest for SunshineImporter {
    fn parse(input: Vec<u8>) -> Result<Dataframe, ParseError> {
        let input = String::from_utf8(input)
            .map_err(|error| ParseError::InvalidFormat(error.to_string()))?;
        let records: Vec<Vec<&str>> = input
            .lines()
            .map(str::trim)
            .filter(|line| !line.is_empty() && !line.starts_with('#'))
            .map(|line| line.split('\t').map(str::trim).collect())
            .collect();

        if records.len() < 2 || records[0].len() < 2 {
            return Err(ParseError::InvalidFormat(
                "sunshine TSV has no header or columns".into(),
            ));
        }

        let timestamps = &records[0][1..];
        let columns = records[1..]
            .iter()
            .map(|record| Column {
                name: record[0].into(),
                data_type: "f64".into(),
            })
            .collect::<Vec<_>>();
        if records
            .iter()
            .skip(1)
            .any(|record| record.len() != timestamps.len() + 1)
        {
            return Err(ParseError::InvalidData(
                "sunshine column has the wrong number of fields".into(),
            ));
        }

        let mut rows = Vec::new();
        for (timestamp_index, timestamp) in timestamps.iter().enumerate() {
            let values = records[1..]
                .iter()
                .map(|record| {
                    record[timestamp_index + 1].parse::<f64>().map_err(|_| {
                        ParseError::InvalidData(format!(
                            "sunshine value is not numeric: {}",
                            record[timestamp_index + 1]
                        ))
                    })
                })
                .collect::<Result<Vec<_>, _>>()?;
            rows.push(Row {
                timestamp: (*timestamp).into(),
                values,
            });
        }

        Ok(Dataframe { columns, rows })
    }
}

export!(SunshineImporter);
