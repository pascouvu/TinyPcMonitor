# Tiny PC Monitor

A tiny, always-on-top Windows widget that shows **live CPU temperature**, CPU
load, memory, and storage — in a small dark window you can move, close, and set
to launch at startup.

![single value, no giant numbers](https://img.shields.io/badge/temp-CPU%20Package-green)

---

## ✨ What it shows

| | |
|---|---|
| **CPU Temperature** | real CPU-package temp, smoothed, colour-coded (green → orange → red) |
| **CPU Load** | current utilisation % |
| **Memory** | used / total |
| **Storage** | per-drive used / total |

Bars turn **orange at 80%** and **red at 90%+** (≈10% free).

---

## ⚙️ Requirements

Two one-time installs, then just run the tiny app:

1. **[.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)**
   — the app no longer bundles .NET, so this must be present. (If you have the
   .NET 8 SDK, you already have it.)
2. **The PawnIo kernel driver** — see first-time setup below (one-time).

- **Windows 10/11 (x64)**
- The published `.exe` is a small (~5 MB) **framework-dependent single file**.
- Building from source needs the [.NET 8 SDK](https://dotnet.microsoft.com/download).

> **CPU temperature is read from the CPU's internal thermal sensors, which only a
> kernel driver can access.** That's a hard Windows limitation — no app, in any
> language, can read it without a driver. This widget uses the **PawnIo** driver
> (the same one LibreHardwareMonitor uses). **PawnIo is NOT bundled in the app**
> — a kernel driver cannot live inside an executable, so it must be installed at
> the OS level, once (see below).

---

## 🚀 First-time setup — install the PawnIo driver (once)

PawnIo must be installed before the widget can read temperatures. The easiest
way is to run LibreHardwareMonitor once and approve the driver install:

1. Download **LibreHardwareMonitor** from
   https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases
   (any `LibreHardwareMonitor.zip`).
2. Unzip, then **right-click `LibreHardwareMonitor.exe` → Run as administrator**.
3. When it prompts **"install PawnIO"** (or similar), click **Install / Yes**.
4. That's it — the driver is now installed permanently as a Windows service
   (`PawnIO`, located in `C:\Windows\System32\DriverStore\...`).
5. You can now **close and even delete LibreHardwareMonitor**. The widget does
   **not** need LHM to run — only the PawnIo driver it left behind.

Verify the driver is present:
```powershell
Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'" | Select Name,State
# Expect: State = Running
```

---

## ▶️ Run the widget

Just double-click **`TinyPcMonitor.exe`**.

- It will ask for **administrator permission (UAC)** — click **Yes**. Admin is
  required because reading the thermal sensors goes through the kernel driver.
- The widget opens in the bottom-right corner, always on top, refreshing every
  second.

### Launch on startup

Tick the **"Launch on startup"** checkbox in the widget. This creates a Windows
**Scheduled Task** that runs the widget at login *with admin rights*, so it
starts silently — **no UAC prompt on every boot** (the standard trick for
admin apps). Untick to remove it.

The scheduled task points to wherever the `.exe` is when you tick the box, so
**copy `TinyPcMonitor.exe` to its final folder first** (e.g. `C:\Tools\TinyPcMonitor\`),
run it from there, then tick the checkbox.

---

## 🔧 Build from source

```bash
cd csharp
dotnet publish -c Release
```

Output — a small framework-dependent single-file `.exe` (~5 MB):
```
csharp/bin/Release/net8.0-windows/win-x64/publish/TinyPcMonitor.exe
```

> **Self-contained build (no .NET prerequisite, ~159 MB):** if you want a single
> `.exe` that runs on any PC without .NET installed, build with
> `dotnet publish -c Release -p:SelfContained=true`.

---

## 🧠 How it works

- **CPU temp & load** — read directly from the **PawnIo** driver via the
  `LibreHardwareMonitorLib` NuGet package. No separate monitor app needed.
- **Memory** — `GlobalMemoryStatusEx` (native).
- **Storage** — `System.IO.DriveInfo`.
- **GUI** — WinForms, dark theme, auto-sized to fit (no scroll).

> The temperature is the **CPU Package** sensor (the stable whole-CPU reading),
> not "Core Max", with a short rolling average so it doesn't jitter.

---

## 🛠️ Troubleshooting

| Symptom | Fix |
|---|---|
| App won't start (double-click does nothing / error about .NET) | Install the **.NET 8 Desktop Runtime (x64)** — the app no longer bundles it. |
| Temperature shows **—** | PawnIo driver not installed, or the widget isn't running as admin. Do the first-time setup above. |
| Widget asks for admin every launch | Expected on manual launch. Use the **"Launch on startup"** checkbox for silent boot start. |
| Temperature still jumps a lot | Normal short spikes under load; it's smoothed over ~3s. The reading is CPU Package. |
| "Access denied" on startup checkbox | The widget must be running as admin to manage the scheduled task (it is, when launched normally). |

---

## 📁 Repository layout

```
csharp/        ← the widget (WinForms, .NET 8)  ← PRIMARY
src/           ← earlier Rust/eframe prototype (no CPU temp) — legacy
```

## License

MIT. Uses [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
(MPL 2.0) and the PawnIo driver.
