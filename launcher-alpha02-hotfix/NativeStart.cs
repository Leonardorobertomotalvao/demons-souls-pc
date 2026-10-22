using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DemonsSouls.Launcher;

// Redirect directly to OS file handles, not managed pumps. The launcher exits
// after Play while the child keeps its log handles and independent environment.
public static class NativeStart
{
    const uint CreateUnicodeEnvironment=0x00000400;
    const uint CreateNoWindow=0x08000000;

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Startup
    {
        public int cb; public string? reserved,desktop,title;
        public int x,y,cx,cy,charsX,charsY,fill,flags; public short show,reserved2;
        public IntPtr reservedPtr,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr process,thread;public uint pid,tid;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcess(string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static Process Start(ProcessStartInfo start,string logRoot,string stamp)
    {
        using var stdout=new FileStream(Path.Combine(logRoot,"game-"+stamp+".log"),FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite);
        using var stderr=new FileStream(Path.Combine(logRoot,"runtime-"+stamp+".log"),FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite);
        using var stdin=new FileStream(@"\\.\NUL",FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        foreach(var handle in new[]{stdout.SafeFileHandle,stderr.SafeFileHandle,stdin.SafeFileHandle})
            if(!SetHandleInformation(handle.DangerousGetHandle(),1,1))throw new Win32Exception(Marshal.GetLastWin32Error());
        string block=string.Join('\0',start.Environment.OrderBy(e=>e.Key,StringComparer.OrdinalIgnoreCase).Select(e=>e.Key+"="+e.Value))+"\0\0";
        IntPtr environment=Marshal.StringToHGlobalUni(block);
        var si=new Startup{cb=Marshal.SizeOf<Startup>(),flags=0x100,input=stdin.SafeFileHandle.DangerousGetHandle(),output=stdout.SafeFileHandle.DangerousGetHandle(),error=stderr.SafeFileHandle.DangerousGetHandle()};
        try
        {
            var command=new StringBuilder('"'+start.FileName+'"'+string.Concat(start.ArgumentList.Select(a=>" \""+a+"\"")));
            if(!CreateProcess(start.FileName,command,IntPtr.Zero,IntPtr.Zero,true,CreateUnicodeEnvironment|CreateNoWindow,environment,start.WorkingDirectory,ref si,out var pi))throw new Win32Exception(Marshal.GetLastWin32Error());
            try{return Process.GetProcessById((int)pi.pid);}finally{CloseHandle(pi.thread);CloseHandle(pi.process);}
        }
        finally{Marshal.FreeHGlobal(environment);}
    }
}
