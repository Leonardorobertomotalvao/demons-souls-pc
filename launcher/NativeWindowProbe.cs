using System.Runtime.InteropServices;

namespace DemonsSouls.Launcher;

public static class NativeWindowProbe
{
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct CursorInfo { public int Size; public uint Flags; public IntPtr Cursor; public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Device;
    }

    delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder className, int max);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref System.Drawing.Point point);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] static extern bool GetClipCursor(out Rect rect);
    [DllImport("user32.dll", EntryPoint="GetCursorInfo")] static extern bool GetCursorInfoNative(ref CursorInfo info);

    public static object? Read(int processId, int renderWidth, int renderHeight)
    {
        IntPtr found=IntPtr.Zero;
        EnumWindowCallback callback=(hwnd,_) =>
        {
            GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid!=(uint)processId)return true;
            var className=new System.Text.StringBuilder(128);
            if(GetClassName(hwnd,className,className.Capacity)>0&&className.ToString()=="ps3recomp_d3d12")
                found=hwnd;
            return true;
        };
        if(!EnumWindows(callback,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(callback);
        if(found==IntPtr.Zero)return null;

        GetWindowRect(found,out Rect window);
        GetClientRect(found,out Rect client);
        var origin=new System.Drawing.Point(client.Left,client.Top);
        ClientToScreen(found,ref origin);
        var cursor=new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};
        GetCursorInfoNative(ref cursor);
        GetClipCursor(out Rect clip);
        var mi=new MonitorInfoEx{Size=Marshal.SizeOf<MonitorInfoEx>(),Device=string.Empty};
        GetMonitorInfo(MonitorFromWindow(found,2),ref mi);
        return new
        {
            hwnd=$"0x{found.ToInt64():X}",pid=processId,className="ps3recomp_d3d12",
            style=$"0x{unchecked((ulong)GetWindowLongPtr(found,-16).ToInt64()):X}",
            exStyle=$"0x{unchecked((ulong)GetWindowLongPtr(found,-20).ToInt64()):X}",
            window=new[]{window.Left,window.Top,window.Right,window.Bottom},
            clientScreen=new[]{origin.X,origin.Y,origin.X+client.Right,origin.Y+client.Bottom},
            clientSize=new[]{client.Right-client.Left,client.Bottom-client.Top},
            monitor=mi.Device,monitorRect=new[]{mi.Monitor.Left,mi.Monitor.Top,mi.Monitor.Right,mi.Monitor.Bottom},
            workArea=new[]{mi.Work.Left,mi.Work.Top,mi.Work.Right,mi.Work.Bottom},
            focused=GetForegroundWindow()==found,cursorVisible=(cursor.Flags&1)!=0,
            cursor=new[]{cursor.X,cursor.Y},clip=new[]{clip.Left,clip.Top,clip.Right,clip.Bottom},
            render=new[]{renderWidth,renderHeight},output=new[]{client.Right-client.Left,client.Bottom-client.Top}
        };
    }
}
