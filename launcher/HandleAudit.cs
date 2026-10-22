using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DemonsSouls.Launcher;

// Diagnostic snapshots of our own child only. Not an exhaustive I/O trace:
// transient opens and failed opens between samples remain invisible.
public static class HandleAudit
{
    [StructLayout(LayoutKind.Sequential)] struct HandleEntry
    {
        public IntPtr Object; public UIntPtr Pid,Handle; public uint Access;
        public ushort Trace,Type; public uint Attributes,Reserved;
    }
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls,IntPtr buffer,int size,out int needed);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(IntPtr source,IntPtr handle,IntPtr target,out IntPtr duplicate,uint access,bool inherit,uint options);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern uint GetFinalPathNameByHandle(IntPtr handle,StringBuilder name,uint length,uint flags);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool GetVolumeNameForVolumeMountPoint(string mount,StringBuilder volume,uint length);
    public static object Snapshot(Process child,string probePath)
    {
        using var probe=File.OpenRead(probePath);
        IntPtr block=IntPtr.Zero,process=IntPtr.Zero;int size=1024*1024;
        try
        {
            int status;
            while(true)
            {
                block=Marshal.AllocHGlobal(size);status=NtQuerySystemInformation(64,block,size,out int needed);
                if(status!=unchecked((int)0xC0000004))break;
                Marshal.FreeHGlobal(block);block=IntPtr.Zero;
                size=Math.Max(checked(size*2),checked(needed+65536));if(size>64*1024*1024)throw new IOException("Tabela de handles excede limite de diagnóstico.");
            }
            if(status!=0)throw new IOException("NtQuerySystemInformation status "+status);
            long count=Marshal.ReadIntPtr(block).ToInt64();int stride=Marshal.SizeOf<HandleEntry>();
            if(count<0||count>(size-16)/stride)throw new IOException("Layout de handles não reconhecido neste Windows.");
            ushort? fileType=null;var owned=new List<HandleEntry>();
            for(int i=0;i<count;i++)
            {
                var e=Marshal.PtrToStructure<HandleEntry>(block+16+i*stride);
                if(e.Pid.ToUInt64()==(ulong)Environment.ProcessId&&e.Handle.ToUInt64()==(ulong)probe.SafeFileHandle.DangerousGetHandle().ToInt64())fileType=e.Type;
                if(e.Pid.ToUInt64()==(ulong)child.Id)owned.Add(e);
            }
            if(fileType==null)throw new IOException("Tipo de arquivo não identificado.");
            process=OpenProcess(0x40,false,child.Id);if(process==IntPtr.Zero)throw new IOException("Sem permissão para consultar handles do processo filho.");
            var dVolume=new StringBuilder(1024);bool volumeKnown=GetVolumeNameForVolumeMountPoint(@"D:\",dVolume,1024);
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var media=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int failed=0,named=0;
            foreach(var e in owned.Where(e=>e.Type==fileType))
            {
                if(!DuplicateHandle(process,new IntPtr((long)e.Handle.ToUInt64()),new IntPtr(-1),out var duplicate,0,false,2)){failed++;continue;}
                try
                {
                    if(GetFileType(duplicate)!=1)continue;
                    var name=new StringBuilder(32768);uint n=GetFinalPathNameByHandle(duplicate,name,(uint)name.Capacity,0);
                    if(n==0||n>=name.Capacity){failed++;continue;}
                    string path=name.ToString();named++;paths.Add(path);
                    var guid=new StringBuilder(32768);uint gn=GetFinalPathNameByHandle(duplicate,guid,(uint)guid.Capacity,1);
                    if(path.StartsWith(@"\\?\D:\",StringComparison.OrdinalIgnoreCase)||path.StartsWith(@"D:\",StringComparison.OrdinalIgnoreCase)||(volumeKnown&&gn>0&&gn<guid.Capacity&&guid.ToString().StartsWith(dVolume.ToString(),StringComparison.OrdinalIgnoreCase)))media.Add(path);
                }
                finally{CloseHandle(duplicate);}
            }
            return new {status="sampled",at=DateTimeOffset.Now,pid=child.Id,named,failed,volumeKnown,paths=paths.Order().ToArray(),mediaPaths=media.ToArray(),limitation="snapshot only; not proof of no transient/failed opens"};
        }
        catch(Exception e){return new{status="inconclusive",at=DateTimeOffset.Now,pid=child.Id,error=e.Message};}
        finally{if(process!=IntPtr.Zero)CloseHandle(process);if(block!=IntPtr.Zero)Marshal.FreeHGlobal(block);}
    }
}
