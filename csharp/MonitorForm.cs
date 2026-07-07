using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace TinyPcMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MonitorForm());
    }
}

public sealed class MonitorForm : Form
{
    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly System.Windows.Forms.Timer _timer;

    // Palette
    private static readonly Color Bg = Color.FromArgb(24, 24, 28);
    private static readonly Color Fg = Color.FromArgb(230, 230, 235);
    private static readonly Color Gray = Color.FromArgb(140, 140, 150);
    private static readonly Color Accent = Color.FromArgb(96, 160, 255);
    private static readonly Color Orange = Color.FromArgb(235, 170, 70);
    private static readonly Color Red = Color.FromArgb(235, 90, 90);
    private static readonly Color Green = Color.FromArgb(110, 200, 130);

    private const int ContentWidth = 248;

    // Rows
    private readonly MetricValueRow _tempRow;
    private readonly MetricBarRow _cpuRow;
    private readonly MetricBarRow _ramRow;
    private readonly Panel _diskSection;
    private readonly Label _diskHead;
    private readonly CheckBox _autostartCheck;

    private const string AutostartTaskName = "TinyPcMonitor";

    // Smooths the temperature display so it doesn't swing wildly each second.
    private readonly Queue<float> _tempBuf = new();
    private const int TempSmoothSamples = 3;

    public MonitorForm()
    {
        Text = "Tiny PC Monitor";
        FormBorderStyle = FormBorderStyle.SizableToolWindow; // thin bar: movable + closable
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowInTaskbar = true;

        // --- Hardware init (CPU temp + load via PawnIo) ---
        _computer = new Computer { IsCpuEnabled = true };
        _computer.Open();
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

        // --- Layout: a single auto-sized column so everything fits, no scroll ---
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(14, 12, 14, 12),
            BackColor = Bg,
        };

        _tempRow = new MetricValueRow("CPU Temperature", ContentWidth);
        _cpuRow = new MetricBarRow("CPU Load", ContentWidth);
        _ramRow = new MetricBarRow("Memory", ContentWidth);

        _diskHead = MakeHead("Storage");
        _diskSection = new Panel
        {
            Width = ContentWidth,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Bg,
        };

        _autostartCheck = new CheckBox
        {
            Text = "Launch on startup",
            ForeColor = Fg,
            BackColor = Bg,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0, 10, 0, 0),
        };
        _autostartCheck.CheckedChanged += (_, _) =>
        {
            bool ok = SetAutostart(_autostartCheck.Checked);
            if (!ok)
                _autostartCheck.Checked = IsAutostartEnabled(); // revert to truth
        };

        content.Controls.Add(_tempRow);
        content.Controls.Add(_cpuRow);
        content.Controls.Add(_ramRow);
        content.Controls.Add(_diskHead);
        content.Controls.Add(_diskSection);
        content.Controls.Add(_autostartCheck);

        Controls.Add(content);

        // Auto-size the window to exactly fit the content (no scrollbars).
        content.AutoSize = true;
        content.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // Park it bottom-right of the screen.
        Load += (_, _) =>
        {
            PositionBottomRight();
            _autostartCheck.Checked = IsAutostartEnabled();
        };

        // --- Timer ---
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshAll();
        _timer.Start();
        RefreshAll();
    }

    private void PositionBottomRight()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
    }

    private void RefreshAll()
    {
        try
        {
            _cpu?.Update();
            float? rawTemp = ReadBestTemp(_cpu);
            float? temp = rawTemp;
            if (rawTemp.HasValue)
            {
                _tempBuf.Enqueue(rawTemp.Value);
                while (_tempBuf.Count > TempSmoothSamples) _tempBuf.Dequeue();
                temp = _tempBuf.Average();
            }
            float? load = ReadSensor(_cpu, SensorType.Load, s => s.Name == "CPU Total");

            _tempRow.SetValue(temp.HasValue ? $"{temp.Value:0} °C" : "—", TempColor(temp));
            _cpuRow.SetData(load / 100f ?? 0f, $"{load:0.0}%");

            var mem = MemStatus();
            double ramFrac = mem.Total > 0 ? (double)mem.Used / mem.Total : 0;
            _ramRow.SetData((float)ramFrac, $"{Fmt(mem.Used)} / {Fmt(mem.Total)}");

            RefreshDisks();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Refresh error: " + ex.Message);
        }
    }

    // ---- helpers ----

    private static float? ReadSensor(IHardware? hw, SensorType type, Func<ISensor, bool> match)
    {
        if (hw == null) return null;
        var s = Array.Find(hw.Sensors, x => x.SensorType == type && match(x));
        return s?.Value;
    }

    /// Picks the temperature reading that best represents the whole CPU.
    /// Priority order matters: "CPU Package" is the stable aggregate most tools
    /// show. "Core Max" (hottest single core) is last because it swings wildly.
    private static float? ReadBestTemp(IHardware? hw)
    {
        if (hw == null) return null;
        var temps = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature).ToArray();
        if (temps.Length == 0) return null;

        foreach (var name in new[] { "CPU Package", "Core Average", "Tctl", "Tdie", "Core Max" })
        {
            var s = Array.Find(temps, t => t.Name == name);
            if (s?.Value is float v) return v;
        }
        return temps[0].Value;
    }

    private Color TempColor(float? t) => t switch
    {
        >= 85f => Red,
        >= 70f => Orange,
        >= 0f => Green,
        _ => Gray
    };

    /// Bar colour by usage: blue normally, orange at 80%, red at 90%+ (≈10% free).
    private Color UsageColor(float frac) => frac >= 0.90f ? Red : frac >= 0.80f ? Orange : Accent;

    private void RefreshDisks()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.TotalSize > 0)
            .OrderBy(d => d.Name)
            .ToList();

        // Rebuild only when the set of drives changes (avoid flicker each tick).
        bool same = _diskSection.Controls.Count == drives.Count
            && _diskSection.Controls.Cast<MetricBarRow>().Select(c => c.Tag as string)
                .SequenceEqual(drives.Select(d => d.Name));

        if (!same)
        {
            _diskSection.Controls.Clear();
            int top = 0;
            foreach (var d in drives)
            {
                var row = new MetricBarRow(d.Name, ContentWidth) { Tag = d.Name, Top = top, Left = 0 };
                _diskSection.Controls.Add(row);
                top += row.Height + 4;
            }
            _diskSection.Height = top;
        }

        foreach (MetricBarRow row in _diskSection.Controls)
        {
            var d = drives.First(x => x.Name == (string)row.Tag!);
            double frac = (double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize;
            row.SetData((float)frac, $"{Fmt((ulong)(d.TotalSize - d.AvailableFreeSpace))} / {Fmt((ulong)d.TotalSize)}");
        }
    }

    private static string Fmt(ulong bytes)
    {
        double gb = bytes / 1073741824.0;
        return gb >= 1024 ? $"{gb / 1024:0.0} TB" : $"{gb:0.0} GB";
    }

    private static (ulong Total, ulong Used) MemStatus()
    {
        var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref ms);
        return (ms.ullTotalPhys, ms.ullTotalPhys - ms.ullAvailPhys);
    }

    private static Label MakeHead(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        ForeColor = Gray,
        BackColor = Color.Transparent,
        AutoSize = true,
        Font = new Font("Segoe UI", 8f, FontStyle.Regular),
        Margin = new Padding(0, 6, 0, 2),
    };

    // ---- autostart via Windows Scheduled Task (elevated, no UAC at login) ----

    /// True if the "launch on startup" scheduled task currently exists.
    private static bool IsAutostartEnabled()
    {
        int code = RunSchtasks("/query /tn " + AutostartTaskName);
        return code == 0;
    }

    /// Creates or removes the scheduled task. Returns true on success.
    private static bool SetAutostart(bool enable)
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        string args = enable
            ? "/create /tn " + AutostartTaskName + " /tr \"\\\"" + exe + "\\\"\" /sc onlogon /rl highest /f"
            : "/delete /tn " + AutostartTaskName + " /f";
        return RunSchtasks(args) == 0;
    }

    private static int RunSchtasks(string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return 1;
            p.WaitForExit(8000);
            return p.ExitCode;
        }
        catch
        {
            return 1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _computer.Close();
        }
        base.Dispose(disposing);
    }

    // ---- give the form a way to compute bar colours ----
    public Color GetUsageColor(float frac) => UsageColor(frac);
}

/// <summary>A row with a name on the left and a value on the right (no bar).
/// Used for temperature. The value text is coloured by the caller.</summary>
internal sealed class MetricValueRow : Panel
{
    private readonly Label _value;
    public MetricValueRow(string name, int width)
    {
        Width = width;
        Height = 22;
        BackColor = Color.Transparent;
        var head = new Label
        {
            Text = name,
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 140, 150),
            Font = new Font("Segoe UI", 8f),
            Location = new Point(0, 2),
        };
        _value = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(110, 200, 130),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
        };
        Controls.Add(head);
        Controls.Add(_value);
        Layout += (_, _) => _value.Left = Width - _value.Width;
    }

    public void SetValue(string text, Color color)
    {
        _value.Text = text;
        _value.ForeColor = color;
    }
}

/// <summary>A row with name (left), value (right), and a usage bar underneath.
/// The bar colour tracks usage (blue → orange 80% → red 90%).</summary>
internal sealed class MetricBarRow : Panel
{
    private readonly Label _value;
    private readonly BarControl _bar = new();

    public MetricBarRow(string name, int width)
    {
        Width = width;
        Height = 40;
        BackColor = Color.Transparent;

        var head = new Label
        {
            Text = name,
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 140, 150),
            Font = new Font("Segoe UI", 8f),
            Location = new Point(0, 2),
        };
        _value = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(230, 230, 235),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(0, 0),
        };
        _bar.Location = new Point(0, 24);
        _bar.Size = new Size(width, 8);

        Controls.Add(head);
        Controls.Add(_value);
        Controls.Add(_bar);
        Layout += (_, _) =>
        {
            _value.Left = Width - _value.Width;
            _bar.Width = Width;
        };
    }

    public void SetData(float frac, string valueText)
    {
        _value.Text = valueText;
        _bar.SetFraction(frac, ResolveColor(frac));
    }

    private static Color ResolveColor(float frac) =>
        frac >= 0.90f ? Color.FromArgb(235, 90, 90)
        : frac >= 0.80f ? Color.FromArgb(235, 170, 70)
        : Color.FromArgb(96, 160, 255);
}

/// <summary>A thin horizontal usage bar drawn on a Control.</summary>
internal sealed class BarControl : Control
{
    private float _frac;
    private Color _color = Color.FromArgb(96, 160, 255);
    private static readonly Color Track = Color.FromArgb(52, 52, 60);

    public BarControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetFraction(float frac, Color color)
    {
        _frac = Math.Clamp(frac, 0f, 1f);
        _color = color;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Transparent);
        using var track = new SolidBrush(Track);
        using var fill = new SolidBrush(_color);
        g.FillRectangle(track, 0, 0, Width, Height);
        if (_frac > 0)
            g.FillRectangle(fill, 0, 0, Width * _frac, Height);
    }
}
