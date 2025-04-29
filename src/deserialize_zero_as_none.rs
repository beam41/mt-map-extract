use serde::Deserialize;
use serde::de::Deserializer;
use std::num::NonZeroI64;

pub fn deserialize_zero_as_none<'de, D>(deserializer: D) -> Result<Option<NonZeroI64>, D::Error>
where
    D: Deserializer<'de>,
{
    let value = i64::deserialize(deserializer)?;
    Ok(NonZeroI64::new(value))
}
