wit_bindgen::generate!({
    path: "../../wit",
    world: "csv-importer",
});

use exports::csv::plugin::importer::{Column, Dataframe, Guest, ParseError, Row};

struct TemperatureImporter;

impl Guest for TemperatureImporter {
    fn parse(input: Vec<u8>) -> Result<Dataframe, ParseError> {
        let input = String::from_utf8(input)
            .map_err(|error| ParseError::InvalidFormat(error.to_string()))?;
        let records: Vec<Vec<&str>> = input
            .lines()
            .map(str::trim)
            .filter(|line| !line.is_empty() && !line.starts_with('#'))
            .map(|line| line.split(',').map(str::trim).collect())
            .collect();

        if records.len() < 2 || records[0].len() < 2 {
            return Err(ParseError::InvalidFormat(
                "temperature CSV has no header or rows".into(),
            ));
        }

        let columns = records[0][1..]
            .iter()
            .map(|name| Column {
                name: (*name).into(),
                data_type: "f64".into(),
            })
            .collect::<Vec<_>>();
        let mut rows = Vec::new();

        for record in records.iter().skip(1) {
            if record.len() != columns.len() + 1 {
                return Err(ParseError::InvalidData(
                    "temperature row has the wrong number of fields".into(),
                ));
            }
            let values = record[1..]
                .iter()
                .map(|value| {
                    value.parse::<f64>().map_err(|_| {
                        ParseError::InvalidData(format!(
                            "temperature value is not numeric: {value}"
                        ))
                    })
                })
                .collect::<Result<Vec<_>, _>>()?;
            rows.push(Row {
                timestamp: record[0].into(),
                values,
            });
        }

        Ok(Dataframe { columns, rows })
    }
}

export!(TemperatureImporter);
