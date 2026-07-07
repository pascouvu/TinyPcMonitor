// Release builds run as a GUI app with no console window.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod autostart;
mod stats;
#[cfg(windows)]
mod temp;

use std::time::{Duration, Instant};

use eframe::egui;
use stats::{fmt_gib, Snapshot, StatsCollector};

const REFRESH_INTERVAL: Duration = Duration::from_secs(1);

fn main() -> eframe::Result {
    let viewport = egui::ViewportBuilder::default()
        .with_title("PC Monitor")
        .with_inner_size([300.0, 250.0])
        .with_min_inner_size([260.0, 200.0])
        .with_always_on_top() // floats above other windows
        .with_resizable(true);

    let options = eframe::NativeOptions {
        viewport,
        ..Default::default()
    };

    eframe::run_native(
        "PC Monitor",
        options,
        Box::new(|_cc| Ok(Box::new(MonitorApp::new()))),
    )
}

pub struct MonitorApp {
    collector: StatsCollector,
    snapshot: Snapshot,
    last_refresh: Instant,

    autostart: Option<auto_launch::AutoLaunch>,
    autostart_enabled: bool,
    autostart_status: String,
}

impl MonitorApp {
    fn new() -> Self {
        let mut collector = StatsCollector::new();
        // Second CPU sample so usage isn't 0.0 on the very first frame.
        std::thread::sleep(Duration::from_millis(120));
        let snapshot = collector.refresh();

        let (autostart, autostart_enabled, autostart_status) = match autostart::build() {
            Ok(al) => {
                let on = al.is_enabled().unwrap_or(false);
                let status = if on {
                    "Autostart: ON".to_string()
                } else {
                    "Autostart: OFF".to_string()
                };
                (Some(al), on, status)
            }
            Err(e) => (None, false, format!("Autostart unavailable: {e}")),
        };

        Self {
            collector,
            snapshot,
            last_refresh: Instant::now(),
            autostart,
            autostart_enabled,
            autostart_status,
        }
    }

    fn maybe_refresh(&mut self) {
        if self.last_refresh.elapsed() >= REFRESH_INTERVAL {
            self.snapshot = self.collector.refresh();
            self.last_refresh = Instant::now();
        }
    }

    /// Enable/disable autostart to match `self.autostart_enabled`.
    fn apply_autostart(&mut self) {
        let Some(al) = self.autostart.as_ref() else {
            self.autostart_status = "Autostart unavailable".to_string();
            return;
        };
        let result = if self.autostart_enabled {
            al.enable()
        } else {
            al.disable()
        };
        match result {
            Ok(()) => {
                self.autostart_status = if self.autostart_enabled {
                    "Autostart: ON".to_string()
                } else {
                    "Autostart: OFF".to_string()
                };
            }
            Err(e) => {
                // Reflect the OS's actual state after a failure.
                self.autostart_enabled = al.is_enabled().unwrap_or(self.autostart_enabled);
                self.autostart_status = format!("Autostart error: {e}");
            }
        }
    }
}

impl eframe::App for MonitorApp {
    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.maybe_refresh();
        // Keep the widget live without busy-looping.
        ctx.request_repaint_after(Duration::from_millis(500));
    }

    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        egui::CentralPanel::default().show(ui, |ui| {
            ui.spacing_mut().item_spacing.y = 6.0;

            ui.horizontal(|ui| {
                ui.heading("🖥  PC Monitor");
            });
            ui.separator();

            self.render_cpu_row(ui);
            self.render_ram_row(ui);
            ui.add_space(2.0);
            self.render_disk_rows(ui);

            ui.with_layout(egui::Layout::bottom_up(egui::Align::LEFT), |ui| {
                ui.separator();
                ui.horizontal(|ui| {
                    let mut on = self.autostart_enabled;
                    if ui.checkbox(&mut on, "Start with Windows").changed() {
                        self.autostart_enabled = on;
                        self.apply_autostart();
                    }
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        ui.label(
                            egui::RichText::new(&self.autostart_status)
                                .small()
                                .color(ui.style().visuals.weak_text_color()),
                        );
                    });
                });
            });
        });
    }
}

impl MonitorApp {
    fn render_cpu_row(&self, ui: &mut egui::Ui) {
        let s = &self.snapshot;
        ui.horizontal(|ui| {
            ui.label("CPU");
            ui.add(
                egui::ProgressBar::new((s.cpu_usage / 100.0).clamp(0.0, 1.0))
                    .desired_width(140.0)
                    .text(format!("{:>5.1} %", s.cpu_usage)),
            );
            let temp_txt = match s.cpu_temp {
                Some(t) => format!("{:>4.0} °C", t),
                None => "  N/A".to_string(),
            };
            ui.label(
                egui::RichText::new(temp_txt)
                    .strong()
                    .color(temp_color(s.cpu_temp)),
            );
            if !s.cpu_temp_source.is_empty() {
                ui.label(
                    egui::RichText::new(&s.cpu_temp_source)
                        .small()
                        .color(ui.style().visuals.weak_text_color()),
                );
            }
        });
    }

    fn render_ram_row(&self, ui: &mut egui::Ui) {
        let s = &self.snapshot;
        let frac = if s.ram_total_bytes > 0 {
            (s.ram_used_bytes as f64 / s.ram_total_bytes as f64).clamp(0.0, 1.0)
        } else {
            0.0
        };
        ui.horizontal(|ui| {
            ui.label("RAM");
            ui.add(
                egui::ProgressBar::new(frac as f32)
                    .desired_width(140.0)
                    .text(format!(
                        "{} / {}",
                        fmt_gib(s.ram_used_bytes),
                        fmt_gib(s.ram_total_bytes)
                    )),
            );
        });
    }

    fn render_disk_rows(&self, ui: &mut egui::Ui) {
        let s = &self.snapshot;
        if s.disks.is_empty() {
            ui.label(
                egui::RichText::new("No fixed disks found")
                    .italics()
                    .color(ui.style().visuals.weak_text_color()),
            );
            return;
        }
        for d in &s.disks {
            let frac = if d.total_bytes > 0 {
                (d.used_bytes as f64 / d.total_bytes as f64).clamp(0.0, 1.0)
            } else {
                0.0
            };
            ui.horizontal(|ui| {
                ui.label(
                    egui::RichText::new(&d.mount)
                        .monospace()
                        .color(ui.style().visuals.strong_text_color()),
                );
                ui.vertical(|ui| {
                    ui.add(
                        egui::ProgressBar::new(frac as f32)
                            .desired_width(175.0)
                            .text(format!(
                                "{} / {}  ({:>3.0}%)",
                                fmt_gib(d.used_bytes),
                                fmt_gib(d.total_bytes),
                                frac * 100.0
                            )),
                    );
                });
            });
        }
    }
}

/// Warm = high temperature; blue/neutral when unknown.
fn temp_color(t: Option<f32>) -> egui::Color32 {
    match t {
        Some(v) if v >= 85.0 => egui::Color32::from_rgb(235, 90, 90),
        Some(v) if v >= 70.0 => egui::Color32::from_rgb(235, 170, 70),
        Some(_) => egui::Color32::from_rgb(110, 200, 130),
        None => egui::Color32::from_gray(150),
    }
}
