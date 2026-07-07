//! Collects CPU / memory / disk stats (and CPU temperature on Windows).

use sysinfo::{Disks, System};

#[cfg(windows)]
use crate::temp::TempReader;

#[derive(Clone)]
pub struct DiskInfo {
    pub mount: String,
    pub used_bytes: u64,
    pub total_bytes: u64,
}

#[derive(Clone)]
pub struct Snapshot {
    pub cpu_usage: f32, // 0.0 - 100.0
    pub cpu_temp: Option<f32>,
    pub cpu_temp_source: String,
    pub ram_used_bytes: u64,
    pub ram_total_bytes: u64,
    pub disks: Vec<DiskInfo>,
}

pub struct StatsCollector {
    sys: System,
    disks: Disks,
    #[cfg(windows)]
    temp: TempReader,
}

impl StatsCollector {
    pub fn new() -> Self {
        let mut sys = System::new();
        sys.refresh_cpu_usage();
        sys.refresh_memory();

        Self {
            sys,
            disks: Disks::new_with_refreshed_list(),
            #[cfg(windows)]
            temp: TempReader::new(),
        }
    }

    pub fn refresh(&mut self) -> Snapshot {
        self.sys.refresh_cpu_usage();
        self.sys.refresh_memory();
        // Rebuild the disk list (cheap, and handles mount changes).
        self.disks = Disks::new_with_refreshed_list();

        #[cfg(windows)]
        let (cpu_temp, cpu_temp_source) = match self.temp.read() {
            Some((t, src)) => (Some(t), src.to_string()),
            None => (None, String::new()),
        };
        #[cfg(not(windows))]
        let (cpu_temp, cpu_temp_source) = (None, String::new());

        let disks = self
            .disks
            .list()
            .iter()
            .filter(|d| d.total_space() > 0)
            .map(|d| DiskInfo {
                mount: d.mount_point().display().to_string(),
                used_bytes: d.total_space().saturating_sub(d.available_space()),
                total_bytes: d.total_space(),
            })
            .collect();

        Snapshot {
            cpu_usage: self.sys.global_cpu_usage(),
            cpu_temp,
            cpu_temp_source,
            ram_used_bytes: self.sys.used_memory(),
            ram_total_bytes: self.sys.total_memory(),
            disks,
        }
    }
}

impl Default for StatsCollector {
    fn default() -> Self {
        Self::new()
    }
}

/// Human-friendly "12.3 GB".
pub fn fmt_gib(bytes: u64) -> String {
    let v = bytes as f64 / 1_073_741_824.0;
    format!("{v:.1} GB")
}
