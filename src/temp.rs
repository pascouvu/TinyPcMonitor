//! CPU temperature reader for Windows.
//!
//! Strategy (in order):
//!   1. LibreHardwareMonitor  -> `ROOT\LibreHardwareMonitor`
//!   2. OpenHardwareMonitor   -> `ROOT\OpenHardwareMonitor`
//!
//! Both expose a WMI `Sensor` class. We filter `SensorClass = "Temperature"`
//! and pick the CPU package/core reading by name. If neither tool is running,
//! temperature is simply reported as unavailable (we deliberately avoid the
//! `MSAcpi_ThermalZoneTemperature` fallback because it returns bogus values
//! on most boards).

use serde::Deserialize;
use wmi::WMIConnection;

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Sensor {
    name: String,
    sensor_class: String,
    value: f64,
}

/// One candidate WMI namespace we know how to query for temperatures.
struct Source {
    label: &'static str,
    namespace: &'static str,
    conn: Option<WMIConnection>,
}

pub struct TempReader {
    sources: Vec<Source>,
}

impl TempReader {
    pub fn new() -> Self {
        let candidates = [
            ("LibreHardwareMonitor", r"ROOT\LibreHardwareMonitor"),
            ("OpenHardwareMonitor", r"ROOT\OpenHardwareMonitor"),
        ];

        let mut sources = Vec::new();
        for (label, ns) in candidates {
            // Try to connect up front; ignore namespaces that don't exist
            // (i.e. the tool isn't installed / running).
            let conn = WMIConnection::with_namespace_path(ns).ok();
            sources.push(Source { label, namespace: ns, conn });
        }
        Self { sources }
    }

    /// Returns (celsius, source label) if a CPU temperature is available.
    pub fn read(&mut self) -> Option<(f32, &'static str)> {
        for src in &mut self.sources {
            let Some(conn) = src.conn.as_ref() else { continue };

            let query = "SELECT Name, SensorClass, Value FROM Sensor";
            let Ok(sensors) = conn.raw_query::<Sensor>(query) else {
                continue;
            };

            let temps: Vec<&Sensor> = sensors
                .iter()
                .filter(|s| s.sensor_class.eq_ignore_ascii_case("Temperature"))
                .collect();

            if let Some(value) = pick_cpu_temp(&temps) {
                return Some((value as f32, src.label));
            }
            let _ = src.namespace; // (kept for potential future diagnostics)
        }
        None
    }
}

impl Default for TempReader {
    fn default() -> Self {
        Self::new()
    }
}

/// Choose the temperature reading that most likely represents the CPU as a
/// whole, preferring "package"/"Tctl" style aggregate sensors.
fn pick_cpu_temp(temps: &[&Sensor]) -> Option<f64> {
    // Exact-name priorities (covers common LHM/OHM sensor names).
    for wanted in ["CPU Package", "CPU Total", "Tctl", "Tdie", "Core (Tctl/Tdie)"] {
        if let Some(s) = temps.iter().find(|s| s.name.eq_ignore_ascii_case(wanted)) {
            return Some(s.value);
        }
    }
    // Substring fallbacks.
    for needle in ["package", "tctl", "tdie"] {
        if let Some(s) = temps
            .iter()
            .find(|s| s.name.to_ascii_lowercase().contains(needle))
        {
            return Some(s.value);
        }
    }
    if let Some(s) = temps
        .iter()
        .find(|s| s.name.to_ascii_lowercase().contains("cpu"))
    {
        return Some(s.value);
    }
    // Last resort: the first temperature reading we got.
    temps.first().map(|s| s.value)
}
