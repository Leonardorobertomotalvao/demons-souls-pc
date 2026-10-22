using System.Runtime.InteropServices;

namespace DemonsSouls.Launcher;

public sealed record DisplayMonitor(string Device, string Label);

public static class DisplayMonitorService
{
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Device;
    }

    delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="GetMonitorInfoW")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    public static IReadOnlyList<DisplayMonitor> Enumerate()
    {
        var monitors = new List<DisplayMonitor>();
        MonitorEnum callback = (handle, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size=Marshal.SizeOf<MonitorInfoEx>(), Device=string.Empty };
            if (GetMonitorInfo(handle, ref info))
            {
                var r = info.Monitor;
                monitors.Add(new(info.Device,
                    $"{info.Device} · {r.Right-r.Left} × {r.Bottom-r.Top} · ({r.Left}, {r.Top})"));
            }
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "Não foi possível listar os monitores disponíveis.");
        GC.KeepAlive(callback);
        return monitors;
    }
}
