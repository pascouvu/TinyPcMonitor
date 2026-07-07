# PC Monitor

A tiny, always-on-top desktop widget for Windows that shows:

- **CPU** – usage % and temperature
- **RAM** – used / total
- **Disks** – used / total per drive

Drag it like any window, close it with the ✕, and optionally tick
**“Start with Windows”** to autostart on login.

## Build & run

```bash
cargo run --release
```

The standalone binary is a single file:

```
target/release/pc-monitor.exe   (~8 MB, no dependencies)
```

Copy it anywhere (e.g. `C:\Tools\PC-Monitor\`) and run from there if you use the
autostart feature — autostart launches whatever `.exe` you started it from.

## CPU temperature

Windows does **not** expose reliable CPU temps to normal apps, so the widget
reads them from a sensor tool’s WMI namespace, in this order:

1. **LibreHardwareMonitor** (recommended) → `ROOT\LibreHardwareMonitor`
2. **OpenHardwareMonitor** → `ROOT\OpenHardwareMonitor`

Install [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor),
launch it once (it can sit in the system tray), and the widget will show your
real CPU temp on the next refresh (~1 s). The source name is printed next to the
value. If neither tool is running, temperature shows **N/A** while RAM/disk
still work.

> LibreHardwareMonitor may need to be **run as administrator** to read sensors
> on some systems.

## Autostart

The “Start with Windows” checkbox toggles the registry key
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No admin rights needed.

## Tech

- [eframe](https://github.com/emilk/egui) (egui) – native GUI, no WebView
- [sysinfo](https://github.com/GuillaumeGomez/sysinfo) – CPU/RAM/disk
- [wmi](https://github.com/ohadravid/wmi-rs) – temperature via WMI (Windows)
- [auto-launch](https://github.com/zzzgydi/auto-launch) – login autostart
