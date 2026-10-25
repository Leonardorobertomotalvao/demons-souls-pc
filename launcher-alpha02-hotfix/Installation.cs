using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace DemonsSouls.Launcher;

public record Entry(string Path, long Length, string Sha256, long? LastWriteUtcTicks = null, long? CreationUtcTicks = null,
    uint? VolumeSerialNumber = null, ulong? FileId = null, long? Usn = null);
public record Transfer(string Phase, string File, long Done, long Total, long Copied, int Files, int Count,
    long BytesRead = 0);
public record LaunchVerification(bool UsedMetadataFastPath, long ElapsedMilliseconds, int FilesChecked,
    bool ReadOnlyExisting=false, long BytesRead=0,string DecisionReason="");
sealed record FileSnapshot(long Length,long LastWriteUtcTicks,long CreationUtcTicks,uint VolumeSerialNumber,
    ulong FileId,long? Usn)
{
    public bool CanReuse(FileSnapshot? current) =>
        Usn.HasValue && current is not null && current.Usn.HasValue &&
        Length==current.Length && LastWriteUtcTicks==current.LastWriteUtcTicks &&
        CreationUtcTicks==current.CreationUtcTicks && VolumeSerialNumber==current.VolumeSerialNumber &&
        FileId==current.FileId && Usn==current.Usn;
    public bool SameObjectState(FileSnapshot? current) =>
        current is not null && Length==current.Length && LastWriteUtcTicks==current.LastWriteUtcTicks &&
        CreationUtcTicks==current.CreationUtcTicks && VolumeSerialNumber==current.VolumeSerialNumber &&
        FileId==current.FileId && Usn==current.Usn;
    public string? ReuseFailureReason(FileSnapshot? current)
    {
        if(current is null)return "metadados do arquivo indisponíveis";
        if(!Usn.HasValue||!current.Usn.HasValue)return "USN por arquivo indisponível";
        if(Length!=current.Length)return "tamanho alterado";
        if(LastWriteUtcTicks!=current.LastWriteUtcTicks)return "data de gravação alterada";
        if(CreationUtcTicks!=current.CreationUtcTicks)return "data de criação alterada";
        if(VolumeSerialNumber!=current.VolumeSerialNumber)return "volume diferente";
        if(FileId!=current.FileId)return "identificador de arquivo diferente";
        if(Usn!=current.Usn)return "USN alterado";
        return null;
    }
}
sealed class IntegritySnapshot
{
    public string Path { get; set; }="";
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public long CreationUtcTicks { get; set; }
    public uint VolumeSerialNumber { get; set; }
    public ulong FileId { get; set; }
    public long? Usn { get; set; }
    public string Sha256 { get; set; }="";
    public bool CanReuse(FileSnapshot? current) =>
        Usn.HasValue && current is not null && current.Usn.HasValue &&
        Length==current.Length && LastWriteUtcTicks==current.LastWriteUtcTicks &&
        CreationUtcTicks==current.CreationUtcTicks && VolumeSerialNumber==current.VolumeSerialNumber &&
        FileId==current.FileId && Usn==current.Usn;
    public static IntegritySnapshot Create(string path,FileSnapshot state,string hash) => new()
    {
        Path=path,Length=state.Length,LastWriteUtcTicks=state.LastWriteUtcTicks,
        CreationUtcTicks=state.CreationUtcTicks,VolumeSerialNumber=state.VolumeSerialNumber,
        FileId=state.FileId,Usn=state.Usn,Sha256=hash
    };
    public FileSnapshot ToFileSnapshot() => new(Length,LastWriteUtcTicks,CreationUtcTicks,VolumeSerialNumber,FileId,Usn);
}
public sealed class Manifest
{
    public int Schema { get; set; } = 1;
    public string TitleId { get; set; } = "BLUS30443";
    public string AppVersion { get; set; } = "01.00";
    public string Origin { get; set; } = "";
    public bool Complete { get; set; }
    public bool Synthetic { get; set; }
    public string IntegrityMac { get; set; }="";
    public string SnapshotRootPath { get; set; }="";
    public uint? SnapshotVolumeSerialNumber { get; set; }
    public ulong? SnapshotRootFileId { get; set; }
    public long? SnapshotUsnJournalId { get; set; }
    public List<Entry> Files { get; set; } = [];
    public List<string> Directories { get; set; } = [];
}
sealed class ExistingVfsProfileManifest
{
    public int Schema { get; set; }=3;
    public bool Complete { get; set; }
    public string SourceManifestSha256 { get; set; }="";
    public string SourceRootPath { get; set; }="";
    public uint? SourceVolumeSerialNumber { get; set; }
    public ulong? SourceRootFileId { get; set; }
    public long? SourceUsnJournalId { get; set; }
    public string ProfileRootPath { get; set; }="";
    public uint? ProfileVolumeSerialNumber { get; set; }
    public ulong? ProfileRootFileId { get; set; }
    public string IntegrityMac { get; set; }="";
    public List<Entry> Files { get; set; }=[];
    public List<IntegritySnapshot> SourceSnapshots { get; set; }=[];
    public List<IntegritySnapshot> ProfileSnapshots { get; set; }=[];
    public List<IntegritySnapshot> RuntimeCacheFiles { get; set; }=[];
}

public static class Installation
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    const uint FileReadAttributes=0x80,FileShareRead=1,FileShareWrite=2,FileShareDelete=4,OpenExisting=3;
    const uint FsctlReadFileUsnData=0x000900EB;
    const uint FsctlQueryUsnJournal=0x000900F4;
    const uint GenericRead=0x80000000;
    const uint FileFlagBackupSemantics=0x02000000;
    [StructLayout(LayoutKind.Sequential)]
    struct DataBlob { public int Length; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)]
    struct DirectoryIdentity(uint volumeSerialNumber,ulong fileId)
    {
        public uint VolumeSerialNumber { get; }=volumeSerialNumber;
        public ulong FileId { get; }=fileId;
    }
    static byte[]? integrityKey;
    static readonly object integrityKeyLock=new();
    [StructLayout(LayoutKind.Sequential)]
    struct NativeFileTime { public uint Low; public uint High; }
    [StructLayout(LayoutKind.Sequential)]
    struct ByHandleFileInformation
    {
        public uint Attributes;
        public NativeFileTime Creation;
        public NativeFileTime LastAccess;
        public NativeFileTime LastWrite;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,
        uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle,out ByHandleFileInformation info);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool DeviceIoControl(SafeFileHandle handle,uint control,IntPtr input,uint inputLength,
        byte[] output,uint outputLength,out uint returned,IntPtr overlapped);
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptProtectData(ref DataBlob input,string? description,IntPtr entropy,IntPtr reserved,
        IntPtr prompt,uint flags,out DataBlob output);
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CryptUnprotectData(ref DataBlob input,out IntPtr description,IntPtr entropy,IntPtr reserved,
        IntPtr prompt,uint flags,out DataBlob output);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    public static string Full(string path) => System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
    public static bool Under(string path, string root) => Full(path).StartsWith(Full(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void NoLinks(string path)
    {
        using var diagnostic=OperationTrace.MeasureActivity("path_link_checks");
        for (var p = Full(path); !string.IsNullOrEmpty(p); p = System.IO.Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Link/junction não permitido na instalação: {p}");
    }
    public static string Child(string root, string relative)
    {
        if (System.IO.Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p is ".." or "." or "" || p.Contains(':') || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new IOException("Caminho relativo inválido: " + relative);
        string result = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (!Under(result, root)) throw new IOException("Caminho fora da instalação.");
        NoLinks(result);
        return result;
    }
    public static Dictionary<string, string> Sfo(string file)
    {
        byte[] data = OperationTrace.ReadAuxiliaryBytes(file);
        if (data.Length < 20 || data.Length > 1024 * 1024 || !data.AsSpan(0,4).SequenceEqual(new byte[] {0,80,83,70})) throw new IOException("PARAM.SFO inválido.");
        uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at,4));
        uint keys = U(8), values = U(12), count = U(16);
        if (count > 1024 || 20UL + 16UL * count > (ulong)data.Length || keys > data.Length || values > data.Length) throw new IOException("Tabelas SFO inválidas.");
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        for (int i=0; i<count; i++)
        {
            int at = 20 + 16*i;
            ulong key = keys + (ulong)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at,2));
            ulong value = values + (ulong)U(at+12); uint length = U(at+4), max = U(at+8);
            if (key >= (ulong)data.Length || length > max || value + max > (ulong)data.Length) throw new IOException("Offset SFO inválido.");
            int end = Array.IndexOf(data, (byte)0, (int)key);
            if (end < 0 || end >= values) throw new IOException("Chave SFO inválida.");
            var format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at+2,2));
            if (format == 0x204) result[Encoding.UTF8.GetString(data,(int)key,end-(int)key)] = Encoding.UTF8.GetString(data,(int)value,(int)length).TrimEnd('\0');
        }
        return result;
    }
    public static string GameRoot(string selected)
    {
        string root = Full(selected);
        if (!File.Exists(System.IO.Path.Combine(root,"PARAM.SFO")))
        {
            bool resolved=false;
            foreach (string candidate in new[] {"PS3_GAME","vfs/PS3_GAME"})
            {
                string[] parts=candidate.Split('/');
                string gameRoot=System.IO.Path.Combine(root,parts[0]);
                for (int i=1;i<parts.Length;i++) gameRoot=System.IO.Path.Combine(gameRoot,parts[i]);
                if (File.Exists(System.IO.Path.Combine(gameRoot,"PARAM.SFO"))) { root=gameRoot; resolved=true; break; }
            }
            if (!resolved) root = System.IO.Path.Combine(root,"PS3_GAME");
        }
        if (!Directory.Exists(root)) throw new IOException("PS3_GAME não encontrado. Localize uma pasta extraída e legível.");
        // The explicitly selected root may be the old recovery junction. Resolve
        // it once; descendants are never followed through reparse points.
        var info = new DirectoryInfo(root);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) root = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Link de origem inválido.");
        var sfo = Sfo(System.IO.Path.Combine(root,"PARAM.SFO"));
        if (sfo.GetValueOrDefault("TITLE_ID") != "BLUS30443" || sfo.GetValueOrDefault("APP_VER") != "01.00" || sfo.GetValueOrDefault("VERSION") != "01.00") throw new IOException("Versão não suportada. Esta build usa BLUS30443 / APP_VER 01.00 / VERSION 01.00.");
        if (!File.Exists(System.IO.Path.Combine(root,"USRDIR/EBOOT.BIN"))) throw new IOException("USRDIR/EBOOT.BIN ausente.");
        foreach (string directory in new[] {"map","chr","obj","shader","sound","menu","script"})
            if (!Directory.Exists(System.IO.Path.Combine(root,"USRDIR",directory))) throw new IOException("Pasta obrigatória ausente: " + directory);
        return Full(root);
    }
    public static List<(string Relative,string Source,long Length)> Inventory(string root,List<string>? directories=null)
    {
        using var diagnostic=OperationTrace.MeasureActivity("inventory");
        List<(string,string,long)> files = []; var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Link interno não permitido: " + item);
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(item); directories?.Add(System.IO.Path.GetRelativePath(root,item)); }
                else files.Add((System.IO.Path.GetRelativePath(root,item),item,new FileInfo(item).Length));
            }
        }
        files.Sort((a,b) => StringComparer.OrdinalIgnoreCase.Compare(a.Item1,b.Item1));
        if (files.Select(f=>f.Item1).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count) throw new IOException("Nomes duplicados na origem.");
        return files;
    }
    public static void CheckElf(string path,bool fixture=false,bool verifyHash=true)
    {
        using var diagnostic=OperationTrace.MeasureActivity("elf_header_and_optional_hash");
        NoLinks(path);
        using var f = File.OpenRead(path); Span<byte> h = stackalloc byte[64];
        if (f.Read(h) != 64 || !h[..6].SequenceEqual(new byte[]{127,69,76,70,2,2}) || BinaryPrimitives.ReadUInt16BigEndian(h[18..20]) != 21 || BinaryPrimitives.ReadUInt16BigEndian(h[16..18]) != 2)
            throw new IOException("Selecione o EBOOT.ELF PPC64 big-endian já preparado legitimamente. EBOOT.BIN/SELF criptografado não é aceito aqui.");
        if(!fixture&&verifyHash)
        {
            f.Position=0;
            try
            {
                if(f.Length!=28222408 || Convert.ToHexString(SHA256.HashData(f))!="90EAA5E9E48B4874292BADC311CCEA253E6A622FF188BFBDBE76E6F6821269D8")
                    throw new IOException("O ELF não corresponde à build recompilada BLUS30443 01.00 validada neste projeto. Nenhuma conversão/descriptografia foi executada.");
            }
            finally{OperationTrace.RecordIo(f.Position,0);OperationTrace.RecordHash(f.Position);}
        }
    }
    static FileSnapshot? ReadFileSnapshot(string path)
    {
        using var diagnostic=OperationTrace.MeasureActivity("file_identity_and_usn");
        if(!OperatingSystem.IsWindows())return null;
        using var handle=CreateFileW(path,GenericRead|FileReadAttributes,FileShareRead|FileShareWrite|FileShareDelete,
            IntPtr.Zero,OpenExisting,0,IntPtr.Zero);
        if(handle.IsInvalid||!GetFileInformationByHandle(handle,out var info))
        {OperationTrace.Observe("file_identity_unavailable_win32_"+Marshal.GetLastWin32Error());return null;}
        long? usn=null;
        byte[] record=new byte[1024];
        if(DeviceIoControl(handle,FsctlReadFileUsnData,IntPtr.Zero,0,record,(uint)record.Length,out uint length,IntPtr.Zero)&&length>=32)
        {
            ushort version=BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4,2));
            int offset=version==2?24:version==3?40:-1;
            if(offset>=0&&length>=(uint)(offset+8))
                usn=BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(offset,8));
        }
        else OperationTrace.Observe("file_usn_unavailable_win32_"+Marshal.GetLastWin32Error());
        var file=new FileInfo(path);
        file.Refresh();
        if(!file.Exists)return null;
        ulong id=((ulong)info.FileIndexHigh<<32)|info.FileIndexLow;
        return new(file.Length,file.LastWriteTimeUtc.Ticks,file.CreationTimeUtc.Ticks,
            info.VolumeSerialNumber,id,usn);
    }
    static DirectoryIdentity? ReadDirectoryIdentity(string path)
    {
        if(!OperatingSystem.IsWindows())return null;
        using var handle=CreateFileW(path,FileReadAttributes,FileShareRead|FileShareWrite|FileShareDelete,
            IntPtr.Zero,OpenExisting,FileFlagBackupSemantics,IntPtr.Zero);
        if(handle.IsInvalid||!GetFileInformationByHandle(handle,out var info))return null;
        return new(info.VolumeSerialNumber,((ulong)info.FileIndexHigh<<32)|info.FileIndexLow);
    }
    static long? ReadUsnJournalId(string path)
        =>ReadUsnJournalId(path,out _);
    internal static bool ForceUsnUnavailableForTest;
    static long? ReadUsnJournalId(string path,out string reason)
    {
        using var diagnostic=OperationTrace.MeasureActivity("usn_journal_query");
        if(ForceUsnUnavailableForTest){reason="USN indisponível (falha sintética)";return null;}
        if(!OperatingSystem.IsWindows()){reason="não é Windows";return null;}
        string? volumeRoot=Path.GetPathRoot(Full(path));
        if(string.IsNullOrWhiteSpace(volumeRoot)||volumeRoot.Length<2||volumeRoot[1]!=':')
        {reason="raiz não é um volume local com letra";return null;}
        // A volume-device handle can open with FILE_READ_ATTRIBUTES yet reject
        // this FSCTL (ERROR_INVALID_FUNCTION) for a standard user. NTFS also
        // accepts a directory handle on the volume; no raw-volume access needed.
        using var handle=CreateFileW(volumeRoot,FileReadAttributes,FileShareRead|FileShareWrite|FileShareDelete,
            IntPtr.Zero,OpenExisting,FileFlagBackupSemantics,IntPtr.Zero);
        if(handle.IsInvalid)
        {reason=$"abertura do volume falhou (Win32 {Marshal.GetLastWin32Error()})";return null;}
        // V0 is 56 bytes; V1/V2 extend the same prefix (up to 80 bytes).
        byte[] journal=new byte[80];
        if(!DeviceIoControl(handle,FsctlQueryUsnJournal,IntPtr.Zero,0,journal,(uint)journal.Length,
            out uint length,IntPtr.Zero))
        {reason=$"FSCTL_QUERY_USN_JOURNAL falhou (Win32 {Marshal.GetLastWin32Error()})";return null;}
        if(length<56){reason=$"registro USN curto ({length} bytes)";return null;}
        long id=BinaryPrimitives.ReadInt64LittleEndian(journal.AsSpan(0,8));
        long first=BinaryPrimitives.ReadInt64LittleEndian(journal.AsSpan(8,8));
        long next=BinaryPrimitives.ReadInt64LittleEndian(journal.AsSpan(16,8));
        if(id==0||first<0||next<first)
        {reason=$"registro USN inválido (id={id}, first={first}, next={next})";return null;}
        reason=$"válido (id={id}, first={first}, next={next})";
        return id;
    }
    static byte[] Dpapi(byte[] data,bool protect)
    {
        IntPtr inputMemory=Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data,0,inputMemory,data.Length);
        var input=new DataBlob{Length=data.Length,Data=inputMemory};
        DataBlob output=default;
        IntPtr description=IntPtr.Zero;
        try
        {
            bool success=protect
                ?CryptProtectData(ref input,"Demon's Souls Launcher integrity state",IntPtr.Zero,IntPtr.Zero,
                    IntPtr.Zero,1,out output)
                :CryptUnprotectData(ref input,out description,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!success)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "Não foi possível proteger/desproteger a chave local de integridade com DPAPI.");
            byte[] result=new byte[output.Length];
            Marshal.Copy(output.Data,result,0,result.Length);
            return result;
        }
        finally
        {
            if(output.Data!=IntPtr.Zero)LocalFree(output.Data);
            if(description!=IntPtr.Zero)LocalFree(description);
            Marshal.FreeHGlobal(inputMemory);
        }
    }
    static byte[] GetIntegrityKey()
    {
        lock(integrityKeyLock)
        {
            if(integrityKey is not null)return integrityKey;
            if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("O snapshot confiável exige DPAPI do Windows.");
            string directory=Preferences.UserDataDirectory;
            NoLinks(directory);Directory.CreateDirectory(directory);NoLinks(directory);
            string path=Child(directory,"integrity-snapshot.key");
            NoLinks(path);
            if(File.Exists(path))
            {
                byte[] key=Dpapi(File.ReadAllBytes(path),false);
                if(key.Length!=32)throw new IOException("A chave DPAPI do snapshot de integridade tem tamanho inválido.");
                integrityKey=key;
                return key;
            }
            byte[] generated=RandomNumberGenerator.GetBytes(32);
            byte[] protectedKey=Dpapi(generated,true);
            try
            {
                using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                stream.Write(protectedKey);stream.Flush(true);
                integrityKey=generated;
                return generated;
            }
            catch(IOException) when(File.Exists(path))
            {
                byte[] concurrent=Dpapi(File.ReadAllBytes(path),false);
                if(concurrent.Length!=32)throw new IOException("A chave DPAPI concorrente do snapshot tem tamanho inválido.");
                integrityKey=concurrent;
                return concurrent;
            }
        }
    }
    static string ComputeProfileStateMac(ExistingVfsProfileManifest state)
    {
        using var diagnostic=OperationTrace.MeasureActivity("profile_mac_compute");
        static object Snapshot(IntegritySnapshot item)=>new
        {
            item.Path,item.Length,item.LastWriteUtcTicks,item.CreationUtcTicks,item.VolumeSerialNumber,
            item.FileId,item.Usn,item.Sha256
        };
        var payload=new
        {
            state.Schema,state.Complete,state.SourceManifestSha256,state.SourceRootPath,state.SourceVolumeSerialNumber,
            state.SourceRootFileId,state.SourceUsnJournalId,state.ProfileRootPath,state.ProfileVolumeSerialNumber,
            state.ProfileRootFileId,
            Files=state.Files.OrderBy(item=>item.Path,StringComparer.OrdinalIgnoreCase)
                .Select(item=>new{item.Path,item.Length,item.Sha256}),
            SourceSnapshots=state.SourceSnapshots.OrderBy(item=>item.Path,StringComparer.OrdinalIgnoreCase).Select(Snapshot),
            ProfileSnapshots=state.ProfileSnapshots.OrderBy(item=>item.Path,StringComparer.OrdinalIgnoreCase).Select(Snapshot),
            RuntimeCacheFiles=state.RuntimeCacheFiles.OrderBy(item=>item.Path,StringComparer.OrdinalIgnoreCase).Select(Snapshot)
        };
        return Convert.ToHexString(HMACSHA256.HashData(GetIntegrityKey(),JsonSerializer.SerializeToUtf8Bytes(payload)));
    }
    static string ComputeManifestMac(Manifest manifest)
    {
        string previous=manifest.IntegrityMac;
        try
        {
            manifest.IntegrityMac="";
            return Convert.ToHexString(HMACSHA256.HashData(GetIntegrityKey(),
                JsonSerializer.SerializeToUtf8Bytes(manifest,Json)));
        }
        finally{manifest.IntegrityMac=previous;}
    }
    static void SignManifestSnapshot(string root,Manifest manifest)
    {
        var identity=ReadDirectoryIdentity(root);
        manifest.SnapshotRootPath=Full(root);
        manifest.SnapshotVolumeSerialNumber=identity?.VolumeSerialNumber;
        manifest.SnapshotRootFileId=identity?.FileId;
        manifest.SnapshotUsnJournalId=ReadUsnJournalId(root);
        manifest.IntegrityMac="";
        manifest.IntegrityMac=ComputeManifestMac(manifest);
    }
    static bool IsManifestSnapshotTrusted(string root,Manifest manifest,long? journalId,out string reason)
    {
        if(string.IsNullOrEmpty(manifest.IntegrityMac))
        {
            reason="snapshot assinado ausente/legado";
            return false;
        }
        if(manifest.IntegrityMac.Length!=64||!manifest.IntegrityMac.All(Uri.IsHexDigit)||
           !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(manifest.IntegrityMac),
               Convert.FromHexString(ComputeManifestMac(manifest))))
        {
            reason="MAC DPAPI inválido; estado do manifesto foi alterado";
            return false;
        }
        var identity=ReadDirectoryIdentity(root);
        if(!string.Equals(manifest.SnapshotRootPath,Full(root),StringComparison.OrdinalIgnoreCase)||
           manifest.SnapshotVolumeSerialNumber!=identity?.VolumeSerialNumber||
           manifest.SnapshotRootFileId!=identity?.FileId)
        {
            reason="caminho, volume ou ID da raiz mudou";
            return false;
        }
        if(!manifest.SnapshotUsnJournalId.HasValue||manifest.SnapshotUsnJournalId!=journalId)
        {
            reason=journalId is null?"USN Journal indisponível/inválido":"USN Journal mudou";
            return false;
        }
        reason="MAC DPAPI, raiz, volume, ID e USN Journal correspondem";
        return true;
    }
    static bool HasValidProfileStateMac(ExistingVfsProfileManifest state)
    {
        using var diagnostic=OperationTrace.MeasureActivity("profile_mac_verify");
        if(state.Schema!=3||string.IsNullOrEmpty(state.IntegrityMac)||state.IntegrityMac.Length!=64||
           state.Files is null||state.SourceSnapshots is null||state.ProfileSnapshots is null||state.RuntimeCacheFiles is null)
            return false;
        if(!state.IntegrityMac.All(Uri.IsHexDigit))return false;
        byte[] expected=Convert.FromHexString(state.IntegrityMac);
        byte[] actual=Convert.FromHexString(ComputeProfileStateMac(state));
        return CryptographicOperations.FixedTimeEquals(expected,actual);
    }
    static ExistingVfsProfileManifest LoadProfileState(string path,string invalidMessage)
    {
        using var diagnostic=OperationTrace.MeasureActivity("profile_state_load");
        return JsonSerializer.Deserialize<ExistingVfsProfileManifest>(OperationTrace.ReadAuxiliaryText(path))
            ??throw new IOException(invalidMessage);
    }
    static void SaveProfileState(string path,ExistingVfsProfileManifest state)
    {
        using var diagnostic=OperationTrace.MeasureActivity("profile_state_persist");
        state.Schema=3;
        state.IntegrityMac=ComputeProfileStateMac(state);
        Save(path,state);
    }
    internal static bool HasUsnSnapshotForTest(string path) => ReadFileSnapshot(path)?.Usn.HasValue==true;
    internal static bool HasUsnJournalForTest(string path) => ReadUsnJournalId(path).HasValue;
    internal static string UsnJournalStatusForTest(string path){_ = ReadUsnJournalId(path,out string status);return status;}
    internal static bool HasDirectoryIdentityForTest(string path) => ReadDirectoryIdentity(path) is not null;
    internal static void ResetIntegrityKeyForTest(){lock(integrityKeyLock)integrityKey=null;}
    internal static bool HasTrustedManifestStateForTest(string root,Manifest manifest) =>
        IsManifestSnapshotTrusted(root,manifest,ReadUsnJournalId(root),out _);

    public static async Task<string> Hash(string path, CancellationToken ct,Action<long>? bytesRead=null)
    {
        using var diagnostic=OperationTrace.MeasureActivity("sha256_stream");
        using var f = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[1024*1024];
        int count;
        while((count=await f.ReadAsync(buffer.AsMemory(),ct))>0)
        {
            hash.AppendData(buffer,0,count);
            bytesRead?.Invoke(count);
            OperationTrace.RecordIo(count,0);
            OperationTrace.RecordHash(count);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public static void Save(string path, object value)
    {
        NoLinks(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string temp = path + ".new";
        NoLinks(temp);
        using (var f = new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
        { JsonSerializer.Serialize(f,value,Json); f.Flush(true); }
        NoLinks(path+".previous");
        if (File.Exists(path)) File.Copy(path,path + ".previous",true);
        File.Move(temp,path,true);
    }
    public static Manifest Load(string root)
    {
        using var diagnostic=OperationTrace.MeasureActivity("manifest_load_and_validate");
        NoLinks(root);
        string path = Child(root,"installation.json");
        if (new FileInfo(path).Length > 16*1024*1024) throw new IOException("Manifesto excede limite.");
        var manifest = JsonSerializer.Deserialize<Manifest>(OperationTrace.ReadAuxiliaryText(path)) ?? throw new IOException("Manifesto inválido.");
        if (manifest.Schema != 1 || manifest.TitleId != "BLUS30443" || manifest.AppVersion != "01.00" || manifest.Files.Count == 0 || manifest.Files.Count > 100000) throw new IOException("Instalação incompatível.");
        foreach (var entry in manifest.Files)
            if (entry.Length < 0 || entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit) || !(entry.Path.StartsWith("vfs/PS3_GAME/",StringComparison.Ordinal) || entry.Path is "runtime/EBOOT.ELF" or "runtime/des_boot.exe")) throw new IOException("Entrada de manifesto inválida.");
            else Child(root,entry.Path);
        if (manifest.Files.Select(e=>e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count) throw new IOException("Manifesto duplicado.");
        foreach(string directory in manifest.Directories) {if(!directory.StartsWith("vfs/PS3_GAME/",StringComparison.Ordinal))throw new IOException("Pasta de manifesto inválida.");Child(root,directory);}
        return manifest;
    }

    public static Manifest ValidateExistingSelection(string root)
    {
        root=Full(root);
        if(!File.Exists(Path.Combine(root,"installation.json")))
            throw new IOException("A pasta escolhida não contém installation.json. Se o manifesto estiver em uma subpasta, selecione diretamente essa pasta, não o diretório pai. A origem não foi alterada.");
        return ValidateExistingStructureReadOnly(root);
    }
    public static async Task Verify(string root, IProgress<Transfer>? progress, CancellationToken ct,bool allowIncomplete=false,bool fixture=false)
    {
        var m = Load(root); if ((!m.Complete&&!allowIncomplete)||m.Synthetic!=fixture) throw new IOException("Importação incompleta ou fixture de teste. Não é uma instalação jogável.");
        ValidateInventory(root,m);
        foreach(string directory in m.Directories) if(!Directory.Exists(Child(root,directory)))throw new IOException("Pasta ausente: "+directory);
        long total=m.Files.Sum(e=>e.Length);
        using var phase=OperationTrace.Measure("Verificação SHA-256",m.Files.Count,total);
        long done=0; int n=0;
        foreach(var e in m.Files)
        {
            ct.ThrowIfCancellationRequested(); string file=Child(root,e.Path);
            if (!File.Exists(file))throw new IOException("Arquivo ausente: "+e.Path+". Use Reparar com a origem disponível.");
            var before=ReadFileSnapshot(file);
            long lengthBefore=new FileInfo(file).Length;
            string found=await Hash(file,ct);
            long lengthAfter=new FileInfo(file).Length;
            var after=ReadFileSnapshot(file);
            if(lengthBefore!=lengthAfter||(before is not null&&!before.SameObjectState(after)))
                throw new IOException("Arquivo alterado enquanto era verificado; feche o runtime ou outros programas que possam gravá-lo: "+e.Path);
            if(lengthAfter!=e.Length||!string.Equals(found,e.Sha256,StringComparison.OrdinalIgnoreCase))
                throw IntegrityMismatch(e.Path,e.Length,lengthAfter,e.Sha256,found,"instalação gerenciada");
            done += e.Length; n++; progress?.Report(new("Verificando",e.Path,done,total,0,n,m.Files.Count));
        }
        GameRoot(Child(root,"vfs/PS3_GAME")); CheckElf(Child(root,"runtime/EBOOT.ELF"),fixture);
        phase?.Complete();
    }
    public static async Task<LaunchVerification> VerifyForLaunch(string root,IProgress<Transfer>? progress,CancellationToken ct,bool fixture=false)
    {
        ct.ThrowIfCancellationRequested();
        var timer=System.Diagnostics.Stopwatch.StartNew();
        root=Full(root);
        var manifest=Load(root);
        if(!manifest.Complete||manifest.Synthetic!=fixture)
            throw new IOException("Instalação incompleta ou incompatível. Prepare ou repare os arquivos antes de jogar.");

        using var launchPhase=OperationTrace.Measure("Validação da instalação antes de iniciar",manifest.Files.Count,manifest.Files.Sum(entry=>entry.Length));
        long? journalId=ReadUsnJournalId(root,out string journalStatus);
        OperationTrace.RecordDecision("usn_journal",journalStatus);
        bool trustedSnapshot=IsManifestSnapshotTrusted(root,manifest,journalId,out string stateReason);
        if(!string.IsNullOrEmpty(manifest.IntegrityMac)&&stateReason=="MAC DPAPI inválido; estado do manifesto foi alterado")
            throw new IOException("O estado de integridade autenticado em installation.json foi alterado. JOGAR foi bloqueado; use Reparar a partir da mesma origem para revalidar sem sobrescrever dados divergentes.");
        string fileReason="";
        bool fast=trustedSnapshot&&MatchesUsnSnapshot(root,manifest,out fileReason);
        string fallbackReason=trustedSnapshot?fileReason:stateReason;
        if(!trustedSnapshot)
            OperationTrace.RecordDecision("managed_fast_path_fallback",stateReason);
        else if(!fast)
            OperationTrace.RecordDecision("managed_fast_path_fallback",fileReason);
        else
            OperationTrace.RecordDecision("managed_fast_path","MAC DPAPI e identidades NTFS/USN correspondem; SHA-256 reutilizado");
        if(fast)
        {
            foreach(var entry in manifest.Files)OperationTrace.RecordReuse(entry.Length);
            VerifyManagedDerivatives(root,manifest,ct);
            GameRoot(Child(root,"vfs/PS3_GAME"));
            CheckElf(Child(root,"runtime/EBOOT.ELF"),fixture,verifyHash:false);
            long total=manifest.Files.Sum(entry=>entry.Length);
            progress?.Report(new("Validação rápida de instalação verificada", "",total,total,0,manifest.Files.Count,manifest.Files.Count));
            launchPhase?.Complete();
            timer.Stop();
            return new(true,timer.ElapsedMilliseconds,manifest.Files.Count,false,0,"MAC DPAPI e identidades NTFS/USN correspondem");
        }

        ValidateInventory(root,manifest);
        long done=0,totalBytes=manifest.Files.Sum(entry=>entry.Length),bytesRead=0;
        int verifiedFiles=0;
        using(var fullHashPhase=OperationTrace.Measure("Verificação SHA-256 completa e atualização do snapshot USN",
            manifest.Files.Count,totalBytes))
        {
            for(int i=0;i<manifest.Files.Count;i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry=manifest.Files[i];
                string path=Child(root,entry.Path);
                if(!File.Exists(path))throw new IOException("Arquivo ausente: "+entry.Path+". Use Reparar com a origem disponível.");
                var before=ReadFileSnapshot(path);
                // The authenticated manifest still pins each expected hash. A change
                // in one file must not invalidate unchanged NTFS/USN identities.
                if(trustedSnapshot&&entry.VolumeSerialNumber is uint volume&&entry.FileId is ulong fileId&&
                   entry.LastWriteUtcTicks is long write&&entry.CreationUtcTicks is long creation&&
                   new FileSnapshot(entry.Length,write,creation,volume,fileId,entry.Usn).CanReuse(before))
                {
                    OperationTrace.RecordReuse(entry.Length);
                    done+=entry.Length;verifiedFiles++;
                    continue;
                }
                long lengthBefore=new FileInfo(path).Length;
                string foundHash=await Hash(path,ct,count=>bytesRead+=count);
                var after=ReadFileSnapshot(path);
                long lengthAfter=new FileInfo(path).Length;
                if(lengthBefore!=lengthAfter||(before is not null&&!before.SameObjectState(after)))
                    throw new IOException("Arquivo alterado durante a verificação; feche o runtime e tente novamente: "+entry.Path);
                if(lengthAfter!=entry.Length||!string.Equals(foundHash,entry.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw IntegrityMismatch(entry.Path,entry.Length,lengthAfter,entry.Sha256,foundHash,"instalação gerenciada");
                manifest.Files[i]=entry with
                {
                    LastWriteUtcTicks=after?.LastWriteUtcTicks,
                    CreationUtcTicks=after?.CreationUtcTicks,
                    VolumeSerialNumber=after?.VolumeSerialNumber,
                    FileId=after?.FileId,
                    Usn=after?.Usn
                };
                done+=entry.Length;verifiedFiles++;
                progress?.Report(new("Verificando SHA-256 completo da instalação",entry.Path,done,totalBytes,0,
                    verifiedFiles,manifest.Files.Count,bytesRead));
            }
            GameRoot(Child(root,"vfs/PS3_GAME"));
            CheckElf(Child(root,"runtime/EBOOT.ELF"),fixture);
            VerifyManagedDerivatives(root,manifest,ct);
            fullHashPhase?.Complete();
        }
        using(var metadataPhase=OperationTrace.Measure("Gravação do snapshot de integridade",manifest.Files.Count,manifest.Files.Sum(entry=>entry.Length)))
        {
            SignManifestSnapshot(root,manifest);
            Save(Child(root,"installation.json"),manifest);
            metadataPhase?.Complete();
        }
        launchPhase?.Complete();
        timer.Stop();
        return new(false,timer.ElapsedMilliseconds,manifest.Files.Count,false,bytesRead,
            $"{manifest.Files.Count} arquivos revalidados; {bytesRead:N0} bytes submetidos a SHA-256; fallback: {fallbackReason}");
    }
    public static Manifest ValidateExistingStructureReadOnly(string root)
    {
        root=Full(root);
        NoLinks(root);
        var manifest=Load(root);
        if(!manifest.Complete||manifest.Synthetic)
            throw new IOException("A instalação existente está incompleta ou é uma fixture sintética.");
        string gameRoot=Child(root,"vfs/PS3_GAME");
        NoLinks(gameRoot);
        GameRoot(gameRoot);
        foreach(string directory in manifest.Directories)
            if(!Directory.Exists(Child(root,directory)))
                throw new IOException("Pasta registrada no manifesto está ausente: "+directory);
        if(!manifest.Files.Any(entry=>entry.Path.StartsWith("vfs/PS3_GAME/",StringComparison.OrdinalIgnoreCase)))
            throw new IOException("O manifesto não registra arquivos em vfs\\PS3_GAME; não é possível usar esta raiz sem copiar ou reorganizar dados.");
        return manifest;
    }

    public static async Task<LaunchVerification> VerifyExistingReadOnly(string root,IProgress<Transfer>? progress,CancellationToken ct)
    {
        var result=await VerifyExistingReadOnlyCore(root,progress,ct,null);
        return result.Verification;
    }

    static async Task<(LaunchVerification Verification,List<IntegritySnapshot> Snapshots)> VerifyExistingReadOnlyCore(
        string root,IProgress<Transfer>? progress,CancellationToken ct,IReadOnlyList<IntegritySnapshot>? cachedSnapshots)
    {
        using var diagnostic=OperationTrace.MeasureActivity("source_validation_total");
        var timer=System.Diagnostics.Stopwatch.StartNew();
        root=Full(root);
        var manifest=ValidateExistingStructureReadOnly(root);
        using var phase=OperationTrace.Measure("Verificação SHA-256 da instalação existente",manifest.Files.Count,manifest.Files.Sum(entry=>entry.Length));
        var expected=manifest.Files.ToDictionary(entry=>entry.Path.Replace('\\','/'),entry=>entry,StringComparer.OrdinalIgnoreCase);
        var actual=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Inventory(Child(root,"vfs/PS3_GAME")).Where(file=>!IsQuarantined(file.Relative)))
        {
            string path="vfs/PS3_GAME/"+file.Relative.Replace('\\','/');
            if(!IsGeneratedCache(path,expected))actual.Add(path,file.Length);
        }
        if(Directory.Exists(Child(root,"runtime")))
            foreach(var file in Inventory(Child(root,"runtime")).Where(file=>!IsQuarantined(file.Relative)))
                actual.Add("runtime/"+file.Relative.Replace('\\','/'),file.Length);
        string[] missing=expected.Keys.Except(actual.Keys,StringComparer.OrdinalIgnoreCase).ToArray();
        string[] unexpected=actual.Keys.Except(expected.Keys,StringComparer.OrdinalIgnoreCase).ToArray();
        if(missing.Length>0||unexpected.Length>0)
            throw new IOException("A instalação existente diverge do manifesto. Ausentes: "+string.Join(", ",missing.Take(5))+
                "; inesperados/procedência não reconhecida: "+string.Join(", ",unexpected.Take(5))+
                ". A origem original não foi alterada.");

        var prior=cachedSnapshots?.ToDictionary(snapshot=>snapshot.Path,snapshot=>snapshot,StringComparer.OrdinalIgnoreCase)
            ??new Dictionary<string,IntegritySnapshot>(StringComparer.OrdinalIgnoreCase);
        var refreshed=new List<IntegritySnapshot>(manifest.Files.Count);
        long done=0,bytesRead=0,total=manifest.Files.Sum(entry=>entry.Length);
        int checkedFiles=0,reusedFiles=0;
        var fallbackReasons=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var entry in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            string path=Child(root,entry.Path);
            if(!File.Exists(path))
                throw new IOException("Arquivo ausente na origem, relativo: "+entry.Path+". A instalação original não foi alterada.");
            var before=ReadFileSnapshot(path);
            var file=new FileInfo(path);file.Refresh();
            if(!file.Exists)throw new IOException("Arquivo ausente na origem, relativo: "+entry.Path+". A instalação original não foi alterada.");
            if(prior.TryGetValue(entry.Path,out var old)&&
               string.Equals(old.Sha256,entry.Sha256,StringComparison.OrdinalIgnoreCase)&&
               old.CanReuse(before))
            {
                refreshed.Add(old);
                done+=entry.Length;checkedFiles++;reusedFiles++;
                OperationTrace.RecordReuse(entry.Length);
                progress?.Report(new("Reutilizando hash SHA-256 previamente confirmado (USN)",entry.Path,done,total,0,
                    checkedFiles,manifest.Files.Count,bytesRead));
                continue;
            }
            string reason=!prior.ContainsKey(entry.Path)?"snapshot autenticado ausente":
                !string.Equals(prior[entry.Path].Sha256,entry.Sha256,StringComparison.OrdinalIgnoreCase)
                    ?"hash esperado mudou":prior[entry.Path].ToFileSnapshot().ReuseFailureReason(before)!;
            fallbackReasons[reason]=fallbackReasons.GetValueOrDefault(reason)+1;
            long lengthBefore=file.Length;
            string foundHash=await Hash(path,ct,count=>bytesRead+=count);
            var after=ReadFileSnapshot(path);
            file.Refresh();
            if(lengthBefore!=file.Length||(before is not null&&!before.SameObjectState(after)))
                throw new IOException("Arquivo da origem foi alterado durante a verificação; feche o runtime e tente novamente: "+entry.Path);
            if(file.Length!=entry.Length||
               !string.Equals(foundHash,entry.Sha256,StringComparison.OrdinalIgnoreCase))
                throw IntegrityMismatch(entry.Path,entry.Length,file.Length,entry.Sha256,foundHash,"origem selecionada");
            if(after is not null)refreshed.Add(IntegritySnapshot.Create(entry.Path,after,foundHash));
            checkedFiles++;
            done+=entry.Length;
            progress?.Report(new("Verificando SHA-256 da origem (somente leitura)",entry.Path,done,total,0,
                checkedFiles,manifest.Files.Count,bytesRead));
        }
        timer.Stop();
        phase?.Complete();
        bool allReused=reusedFiles==manifest.Files.Count&&bytesRead==0;
        string decision=allReused?"todos os hashes reutilizados; identidade NTFS/USN inalterada":
            $"{reusedFiles}/{manifest.Files.Count} snapshots reutilizados; {bytesRead:N0} bytes submetidos a SHA-256; "+
            string.Join(", ",fallbackReasons.OrderByDescending(item=>item.Value).Select(item=>$"{item.Key}: {item.Value}"));
        OperationTrace.RecordDecision("existing_source_validation",decision);
        return (new(allReused,timer.ElapsedMilliseconds,checkedFiles,true,bytesRead,decision),refreshed);
    }

    public static async Task<LaunchVerification> PrepareExistingRuntimeProfile(string root,string profileRoot,IProgress<Transfer>? progress,CancellationToken ct)
    {
        using var diagnostic=OperationTrace.MeasureActivity("prepare_existing_total");
        root=Full(root);profileRoot=Full(profileRoot);
        RuntimeService.ValidateProfileRoot(root,profileRoot);
        RuntimeService.EnsureNoRuntimeAlreadyRunning();
        using var sourceLease=TakeLease(root);
        using var profileLease=TakeLease(profileRoot);
        NoLinks(profileRoot);Directory.CreateDirectory(profileRoot);NoLinks(profileRoot);
        string sourceManifestPath=Child(root,"installation.json");
        string sourceManifestHash=await Hash(sourceManifestPath,ct);
        string targetVfs=Child(profileRoot,"vfs");
        string profileManifestPath=Child(profileRoot,"existing-vfs-profile.json");
        var sourceIdentity=ReadDirectoryIdentity(root);
        var profileIdentity=ReadDirectoryIdentity(profileRoot);
        long? journalId=ReadUsnJournalId(root,out string journalStatus);
        OperationTrace.RecordDecision("usn_journal",journalStatus);
        var sourceManifest=Load(root);
        var files=sourceManifest.Files.Where(entry=>entry.Path.StartsWith("vfs/PS3_GAME/",StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry=>entry.Path,StringComparer.OrdinalIgnoreCase).ToList();
        ExistingVfsProfileManifest? prior=null;
        bool trustedPrior=false;
        if(File.Exists(profileManifestPath))
        {
            prior=LoadProfileState(profileManifestPath,
                "O manifesto do perfil VFS está inválido; selecione uma nova pasta de perfil para preservar os dados existentes.");
            if(prior.Schema is not (1 or 2 or 3)||!string.Equals(prior.SourceManifestSha256,sourceManifestHash,StringComparison.OrdinalIgnoreCase)||
               !prior.Files.Select(entry=>(entry.Path,entry.Length,entry.Sha256)).SequenceEqual(files.Select(entry=>(entry.Path,entry.Length,entry.Sha256))))
                throw new IOException("A instalação de origem mudou desde a preparação do perfil VFS. Selecione uma nova pasta de perfil; saves e dados atuais não foram substituídos.");
            bool macValid=HasValidProfileStateMac(prior);
            // This schema persists only the source journal ID. It can protect
            // mirror snapshots only on that same volume; cross-volume profiles
            // must rehash until a separate authenticated profile journal is stored.
            bool identityMatches=macValid&&sourceIdentity is not null&&profileIdentity is not null&&
                sourceIdentity?.VolumeSerialNumber==profileIdentity?.VolumeSerialNumber&&
                string.Equals(prior.SourceRootPath,root,StringComparison.OrdinalIgnoreCase)&&
                prior.SourceVolumeSerialNumber==sourceIdentity?.VolumeSerialNumber&&
                prior.SourceRootFileId==sourceIdentity?.FileId&&
                string.Equals(prior.ProfileRootPath,profileRoot,StringComparison.OrdinalIgnoreCase)&&
                prior.ProfileVolumeSerialNumber==profileIdentity?.VolumeSerialNumber&&
                prior.ProfileRootFileId==profileIdentity?.FileId&&
                prior.SourceUsnJournalId.HasValue&&prior.SourceUsnJournalId==journalId;
            trustedPrior=identityMatches;
            OperationTrace.RecordDecision("existing_profile_evidence",JsonSerializer.Serialize(new
            {
                macValid,prior.Complete,sameVolume=sourceIdentity?.VolumeSerialNumber==profileIdentity?.VolumeSerialNumber,
                storedSourceJournal=prior.SourceUsnJournalId,currentSourceJournal=journalId,
                sourceSnapshots=prior.SourceSnapshots.Count,profileSnapshots=prior.ProfileSnapshots.Count,
                derivedSnapshots=prior.RuntimeCacheFiles.Count
            }));
            string reason=trustedPrior
                ?"MAC DPAPI válido; raízes, volumes, IDs, manifesto e USN Journal correspondem"
                :!macValid?"snapshot ausente, legado ou MAC DPAPI inválido; SHA-256 completo obrigatório"
                :journalId is null?"USN Journal indisponível/inválido; SHA-256 completo obrigatório"
                :"caminho/volume/ID da raiz ou USN Journal mudou; SHA-256 completo obrigatório";
            OperationTrace.RecordDecision("existing_profile_state",reason);
        }
        else if(Directory.Exists(targetVfs)&&Inventory(targetVfs).Count>0)
            throw new IOException("A pasta vfs do perfil já contém dados sem manifesto de origem. Selecione uma pasta de perfil nova para evitar sobrescrita.");

        if(!File.Exists(profileManifestPath))
            OperationTrace.RecordDecision("existing_profile_state","nenhum estado anterior; primeira verificação integral");
        var sourceCheck=await VerifyExistingReadOnlyCore(root,progress,ct,
            trustedPrior?prior!.SourceSnapshots:null);
        var verification=sourceCheck.Verification;
        if(trustedPrior&&prior!.Complete)
        {
            if(!verification.UsedMetadataFastPath)
            {
                prior.SourceSnapshots=sourceCheck.Snapshots;
                SaveProfileState(profileManifestPath,prior);
            }
            // Revalidate the mirror and derivatives without entering the copy/resume
            // transaction or rewriting three incomplete markers on an unchanged run.
            using var repeatPhase=OperationTrace.Measure("Validação incremental do perfil completo",files.Count,files.Sum(entry=>entry.Length));
            try{ValidatePreparedExistingRuntimeVfsMarker(root,profileRoot,ct);}
            catch
            {
                // Failed/cancelled validation must never leave a persisted ready
                // marker. Keep the previously authenticated per-file evidence.
                prior.Complete=false;
                SaveProfileState(profileManifestPath,prior);
                throw;
            }
            repeatPhase?.Complete();
            return verification;
        }
        var profileManifest=new ExistingVfsProfileManifest
        {
            SourceManifestSha256=sourceManifestHash,Files=files,Complete=false,
            SourceRootPath=root,SourceVolumeSerialNumber=sourceIdentity?.VolumeSerialNumber,
            SourceRootFileId=sourceIdentity?.FileId,SourceUsnJournalId=journalId,
            ProfileRootPath=profileRoot,ProfileVolumeSerialNumber=profileIdentity?.VolumeSerialNumber,
            ProfileRootFileId=profileIdentity?.FileId,
            SourceSnapshots=sourceCheck.Snapshots,
            ProfileSnapshots=trustedPrior?prior!.ProfileSnapshots:[],
            RuntimeCacheFiles=trustedPrior?prior!.RuntimeCacheFiles:[]
        };
        using(var markerPhase=OperationTrace.Measure("Gravação do manifesto retomável da VFS",files.Count,files.Sum(entry=>entry.Length)))
        {
            SaveProfileState(profileManifestPath,profileManifest);
            markerPhase?.Complete();
        }
        long done=0,total=files.Sum(entry=>entry.Length),copied=0,bytesRead=0;int count=0;
        var knownProfile=profileManifest.ProfileSnapshots.ToDictionary(snapshot=>snapshot.Path,snapshot=>snapshot,StringComparer.OrdinalIgnoreCase);
        var currentProfile=new List<IntegritySnapshot>(files.Count);
        using(var copyPhase=OperationTrace.Measure("Cópia VFS com flush, fechamento e SHA-256 temporário",files.Count,total))
        {
            foreach(var entry in files)
            {
                ct.ThrowIfCancellationRequested();
                string source=Child(root,entry.Path),target=Child(profileRoot,entry.Path);
                string temp=target+".launcher-partial";
                NoLinks(temp);
                if(File.Exists(target))
                {
                    var state=ReadFileSnapshot(target);
                    string actualHash;
                    bool reused=knownProfile.TryGetValue(entry.Path,out var known)&&
                       string.Equals(known.Sha256,entry.Sha256,StringComparison.OrdinalIgnoreCase)&&known.CanReuse(state);
                    if(reused)
                    {
                        actualHash=known!.Sha256;
                        OperationTrace.RecordReuse(entry.Length);
                    }
                    else
                    {
                        long lengthBefore=new FileInfo(target).Length;
                        actualHash=await Hash(target,ct,countBytes=>bytesRead+=countBytes);
                        var after=ReadFileSnapshot(target);
                        if(lengthBefore!=new FileInfo(target).Length||(state is not null&&!state.SameObjectState(after)))
                            throw new IOException("Arquivo do perfil foi alterado durante a validação; feche o runtime e tente novamente: "+entry.Path);
                    }
                    if(new FileInfo(target).Length!=entry.Length||
                       !string.Equals(actualHash,entry.Sha256,StringComparison.OrdinalIgnoreCase))
                        throw IntegrityMismatch(entry.Path,entry.Length,new FileInfo(target).Length,entry.Sha256,
                            actualHash,"perfil experimental; o arquivo divergente foi preservado");
                    var confirmed=ReadFileSnapshot(target);
                    if(confirmed is not null)currentProfile.Add(IntegritySnapshot.Create(entry.Path,confirmed,actualHash));
                    done+=entry.Length;count++;
                    progress?.Report(new(reused
                        ?"Reutilizando arquivo confirmado no perfil (USN)"
                        :"Verificando SHA-256 do arquivo no perfil",
                        entry.Path,done,total,copied,count,files.Count,bytesRead));
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if(File.Exists(temp))
                {
                    string partialHash=await Hash(temp,ct,countBytes=>bytesRead+=countBytes);
                    if(new FileInfo(temp).Length==entry.Length&&
                       string.Equals(partialHash,entry.Sha256,StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(temp,target,false);
                        var promoted=ReadFileSnapshot(target);
                        if(promoted is not null)currentProfile.Add(IntegritySnapshot.Create(entry.Path,promoted,partialHash));
                        done+=entry.Length;count++;
                        progress?.Report(new("Retomando cópia parcial já íntegra",entry.Path,done,total,copied,count,files.Count,bytesRead));
                        continue;
                    }
                    string preservedDirectory=Child(profileRoot,"launcher-recovery/partials");
                    Directory.CreateDirectory(preservedDirectory);
                    string preserved=Child(preservedDirectory,
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Path)))+"-"+Guid.NewGuid().ToString("N")+".partial");
                    File.Move(temp,preserved,false);
                }
                string tempHash;
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true))
                using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,true))
                using(var copiedHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] buffer=new byte[1024*1024];int read;
                    while((read=await input.ReadAsync(buffer,ct))>0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0,read),ct);
                        copiedHash.AppendData(buffer,0,read);
                        copied+=read;bytesRead+=read;OperationTrace.RecordIo(read,read);
                        OperationTrace.RecordHash(read);
                        progress?.Report(new("Copiando dados originais verificados para o perfil isolado",entry.Path,
                            done+output.Position,total,copied,count,files.Count,bytesRead));
                    }
                    await output.FlushAsync(ct);output.Flush(true);
                    tempHash=Convert.ToHexString(copiedHash.GetHashAndReset());
                }
                ct.ThrowIfCancellationRequested();
                long tempLength=new FileInfo(temp).Length;
                if(tempLength!=entry.Length||!string.Equals(tempHash,entry.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw IntegrityMismatch(entry.Path,entry.Length,tempLength,entry.Sha256,tempHash,
                        "cópia temporária; o arquivo parcial foi preservado");
                NoLinks(target);File.Move(temp,target,false);
                var copiedState=ReadFileSnapshot(target);
                if(copiedState is not null)currentProfile.Add(IntegritySnapshot.Create(entry.Path,copiedState,tempHash));
                done+=entry.Length;count++;
                progress?.Report(new("Arquivo copiado e verificado no perfil isolado",entry.Path,done,total,copied,count,files.Count,bytesRead));
            }
            copyPhase?.Complete();
        }

        // Preserve individually verified hashes for a safe retry, never a ready
        // marker: inventory/cache failures below must leave Complete=false.
        profileManifest.ProfileSnapshots=currentProfile;
        SaveProfileState(profileManifestPath,profileManifest);
        using(var finalPhase=OperationTrace.Measure("Validação final da VFS isolada",files.Count,total))
        {
            foreach(string directory in sourceManifest.Directories)
                Directory.CreateDirectory(Child(profileRoot,directory));
            ValidateProfileInventory(targetVfs,profileManifest.Files,sourceManifest.Directories);
            profileManifest.ProfileSnapshots=currentProfile;
            profileManifest.RuntimeCacheFiles=await RefreshRuntimeCacheSnapshots(targetVfs,profileManifest.RuntimeCacheFiles,ct,profileManifest.Files);
            finalPhase?.Complete();
        }
        profileManifest.Complete=true;
        using(var finalization=OperationTrace.Measure("Gravação do manifesto de conclusão da VFS",files.Count,total))
        {
            SaveProfileState(profileManifestPath,profileManifest);
            finalization?.Complete();
        }
        return verification;
    }

    public static void VerifyPreparedExistingRuntimeVfs(string root,string profileRoot)
    {
        ValidatePreparedExistingRuntimeVfsMarker(root,profileRoot);
    }

    public static void ValidatePreparedExistingRuntimeVfsMarker(string root,string profileRoot,CancellationToken ct=default)
    {
        using var diagnostic=OperationTrace.MeasureActivity("profile_validation_total");
        ct.ThrowIfCancellationRequested();
        root=Full(root);profileRoot=Full(profileRoot);
        RuntimeService.ValidateProfileRoot(root,profileRoot);
        ValidateExistingStructureReadOnly(root);
        string sourceManifestHash=OperationTrace.HashAuxiliaryFile(Child(root,"installation.json"));
        string profileManifestPath=Child(profileRoot,"existing-vfs-profile.json");
        if(!File.Exists(profileManifestPath))
            throw new IOException("A VFS ainda não foi preparada no perfil experimental. Clique JOGAR para preparar os dados isolados.");
        var profileManifest=LoadProfileState(profileManifestPath,
            "O manifesto VFS do perfil experimental está inválido; a origem permanece intacta.");
        var sourceIdentity=ReadDirectoryIdentity(root);
        var profileIdentity=ReadDirectoryIdentity(profileRoot);
        long? journalId=ReadUsnJournalId(root,out string journalStatus);
        OperationTrace.RecordDecision("usn_journal",journalStatus);
        var sourceManifest=Load(root);
        var expected=sourceManifest.Files.Where(entry=>entry.Path.StartsWith("vfs/PS3_GAME/",StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry=>entry.Path,StringComparer.OrdinalIgnoreCase).ToList();
        if(profileManifest.Schema is not (1 or 2 or 3)||!profileManifest.Complete||
           !string.Equals(profileManifest.SourceManifestSha256,sourceManifestHash,StringComparison.OrdinalIgnoreCase)||
           !profileManifest.Files.Select(entry=>(entry.Path,entry.Length,entry.Sha256)).SequenceEqual(expected.Select(entry=>(entry.Path,entry.Length,entry.Sha256))))
            throw new IOException("O perfil VFS não corresponde a uma preparação completa da instalação existente.");
        bool macValid=HasValidProfileStateMac(profileManifest);
        bool trustedState=macValid&&sourceIdentity is not null&&profileIdentity is not null&&
            sourceIdentity?.VolumeSerialNumber==profileIdentity?.VolumeSerialNumber&&
            string.Equals(profileManifest.SourceRootPath,root,StringComparison.OrdinalIgnoreCase)&&
            profileManifest.SourceVolumeSerialNumber==sourceIdentity?.VolumeSerialNumber&&
            profileManifest.SourceRootFileId==sourceIdentity?.FileId&&
            string.Equals(profileManifest.ProfileRootPath,profileRoot,StringComparison.OrdinalIgnoreCase)&&
            profileManifest.ProfileVolumeSerialNumber==profileIdentity?.VolumeSerialNumber&&
            profileManifest.ProfileRootFileId==profileIdentity?.FileId&&
            profileManifest.SourceUsnJournalId.HasValue&&profileManifest.SourceUsnJournalId==journalId;
        OperationTrace.RecordDecision("prepared_profile_evidence",JsonSerializer.Serialize(new
        {
            macValid,trustedState,sameVolume=sourceIdentity?.VolumeSerialNumber==profileIdentity?.VolumeSerialNumber,
            storedSourceJournal=profileManifest.SourceUsnJournalId,currentSourceJournal=journalId,
            sourceSnapshots=profileManifest.SourceSnapshots.Count,profileSnapshots=profileManifest.ProfileSnapshots.Count,
            derivedSnapshots=profileManifest.RuntimeCacheFiles.Count
        }));
        OperationTrace.RecordDecision("prepared_profile_state",trustedState
            ?"MAC DPAPI, identidade de origem/perfil e USN Journal válidos"
            :!macValid
                ?"MAC DPAPI ausente/inválido; validação SHA-256 integral do espelho"
                :journalId is null
                    ?"USN Journal indisponível/inválido; validação SHA-256 integral do espelho"
                    :"identidade de raiz/volume/arquivo ou USN Journal mudou; validação SHA-256 integral do espelho");
        string targetVfs=Child(profileRoot,"vfs");
        ValidateProfileInventory(targetVfs,expected,sourceManifest.Directories);
        var prior=profileManifest.ProfileSnapshots.ToDictionary(snapshot=>snapshot.Path,snapshot=>snapshot,StringComparer.OrdinalIgnoreCase);
        var refreshed=new List<IntegritySnapshot>(expected.Count);
        foreach(var entry in expected)
        {
            ct.ThrowIfCancellationRequested();
            string path=Child(profileRoot,entry.Path);
            var state=ReadFileSnapshot(path);
            if(trustedState&&prior.TryGetValue(entry.Path,out var old)&&old.CanReuse(state)&&
               string.Equals(old.Sha256,entry.Sha256,StringComparison.OrdinalIgnoreCase))
            {
                refreshed.Add(old);
                OperationTrace.Observe("profile_hash_reused");
                OperationTrace.RecordReuse(entry.Length);
                continue;
            }
            OperationTrace.Observe(!trustedState?"profile_full_hash_untrusted_state":
                !prior.TryGetValue(entry.Path,out var previous)?"profile_hash_missing_snapshot":
                "profile_hash_required_"+(previous.ToFileSnapshot().ReuseFailureReason(state)??"expected_hash_changed"));
            long lengthBefore=new FileInfo(path).Length;
            string actualHash=Hash(path,ct).GetAwaiter().GetResult();
            var after=ReadFileSnapshot(path);
            long lengthAfter=new FileInfo(path).Length;
            if(lengthBefore!=lengthAfter||(state is not null&&!state.SameObjectState(after)))
                throw new IOException("Arquivo do perfil foi alterado durante a validação: "+entry.Path);
            if(lengthAfter!=entry.Length||!string.Equals(actualHash,entry.Sha256,StringComparison.OrdinalIgnoreCase))
                throw IntegrityMismatch(entry.Path,entry.Length,lengthAfter,entry.Sha256,actualHash,
                    "perfil experimental; o arquivo divergente foi preservado");
            if(after is not null)refreshed.Add(IntegritySnapshot.Create(entry.Path,after,actualHash));
        }
        profileManifest.Schema=3;
        profileManifest.SourceRootPath=root;
        profileManifest.SourceVolumeSerialNumber=sourceIdentity?.VolumeSerialNumber;
        profileManifest.SourceRootFileId=sourceIdentity?.FileId;
        profileManifest.SourceUsnJournalId=journalId;
        profileManifest.ProfileRootPath=profileRoot;
        profileManifest.ProfileVolumeSerialNumber=profileIdentity?.VolumeSerialNumber;
        profileManifest.ProfileRootFileId=profileIdentity?.FileId;
        if(!trustedState)profileManifest.SourceSnapshots=[];
        profileManifest.ProfileSnapshots=refreshed;
        profileManifest.RuntimeCacheFiles=RefreshRuntimeCacheSnapshots(targetVfs,
            trustedState?profileManifest.RuntimeCacheFiles:[],
            ct,expected).GetAwaiter().GetResult();
        ct.ThrowIfCancellationRequested();
        if(!trustedState||!string.Equals(ComputeProfileStateMac(profileManifest),profileManifest.IntegrityMac,StringComparison.Ordinal))
            SaveProfileState(profileManifestPath,profileManifest);
        else
            OperationTrace.RecordDecision("profile_state_unchanged","Inventário, identidades e hashes confirmados; nenhuma gravação do snapshot necessária.");
    }
    static bool MatchesUsnSnapshot(string root,Manifest manifest,out string reason)
    {
        var expected=manifest.Files.ToDictionary(entry=>entry.Path.Replace('\\','/'),entry=>entry,StringComparer.OrdinalIgnoreCase);
        var actual=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Inventory(Child(root,"vfs/PS3_GAME")).Where(file=>!IsQuarantined(file.Relative)))
        {
            string path="vfs/PS3_GAME/"+file.Relative.Replace('\\','/');
            if(!IsGeneratedCache(path,expected))actual.Add(path,file.Length);
        }
        if(Directory.Exists(Child(root,"runtime")))
        foreach(var file in Inventory(Child(root,"runtime")).Where(file=>!IsQuarantined(file.Relative)))
        {
            string path="runtime/"+file.Relative.Replace('\\','/');
            if(!IsGeneratedCache(path,expected))actual.Add(path,file.Length);
        }
        if(actual.Count!=expected.Count)
        {
            reason=$"inventário contém {actual.Count} arquivos, manifesto registra {expected.Count}";
            return false;
        }
        foreach(var (path,length) in actual)
        {
            if(!expected.TryGetValue(path,out var entry))
            {
                reason=$"arquivo inesperado: {path}";
                return false;
            }
            if(length!=entry.Length)
            {
                reason=$"tamanho alterado: {path}";
                return false;
            }
            if(!entry.VolumeSerialNumber.HasValue||!entry.FileId.HasValue||!entry.Usn.HasValue)
            {
                reason=$"snapshot incompleto: {path}";
                return false;
            }
            var state=ReadFileSnapshot(Child(root,path));
            if(state is null)
            {
                reason=$"metadados não puderam ser lidos: {path}";
                return false;
            }
            if(!state.Usn.HasValue)
            {
                reason=$"USN por arquivo indisponível: {path}";
                return false;
            }
            if(state.Length!=entry.Length)
            {
                reason=$"tamanho alterado: {path}";
                return false;
            }
            if(state.LastWriteUtcTicks!=entry.LastWriteUtcTicks)
            {
                reason=$"data de gravação alterada: {path}";
                return false;
            }
            if(state.CreationUtcTicks!=entry.CreationUtcTicks)
            {
                reason=$"data de criação alterada: {path}";
                return false;
            }
            if(state.VolumeSerialNumber!=entry.VolumeSerialNumber)
            {
                reason=$"volume diferente: {path}";
                return false;
            }
            if(state.FileId!=entry.FileId)
            {
                reason=$"identificador de arquivo diferente: {path}";
                return false;
            }
            if(state.Usn!=entry.Usn)
            {
                reason=$"USN alterado: {path}";
                return false;
            }
        }
        reason="todos os IDs, metadados e USNs permanecem iguais";
        return true;
    }
    static readonly System.Text.RegularExpressions.Regex RuntimeCachePath=new(
        @"^vfs/cache/[0-9a-f]{2}/[0-9a-f]{32}$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    static bool IsRuntimeResourceCache(string path) => RuntimeCachePath.IsMatch(path.Replace('\\','/'));
    static void ValidateProfileInventory(string targetVfs,IReadOnlyList<Entry> expected,
        IReadOnlyList<string>? expectedDirectories=null)
    {
        using var diagnostic=OperationTrace.MeasureActivity("profile_inventory_and_rules");
        var expectedFiles=expected.ToDictionary(entry=>entry.Path.Replace('\\','/'),entry=>entry.Length,
            StringComparer.OrdinalIgnoreCase);
        var actualFiles=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        var actualDirectories=new List<string>();
        foreach(var file in Inventory(targetVfs,actualDirectories))
            actualFiles.Add("vfs/"+file.Relative.Replace('\\','/'),file.Length);
        using var rulesDiagnostic=OperationTrace.MeasureActivity("profile_classification_and_directory_rules");
        var missing=expectedFiles.Keys.Except(actualFiles.Keys,StringComparer.OrdinalIgnoreCase).ToArray();
        var unexpected=actualFiles.Where(file=>VfsPolicy.Classify(file.Key,expectedFiles)==VfsFileKind.Unknown)
            .Select(file=>$"{file.Key} ({file.Value:N0} bytes; procedência não reconhecida)").ToArray();
        if(missing.Length>0||unexpected.Length>0)
            throw new IOException("A VFS do perfil experimental diverge do inventário protegido. Ausentes: "+
                string.Join(", ",missing.Take(10))+"; arquivos inesperados não permitidos: "+
                string.Join(", ",unexpected.Take(10))+". Os arquivos foram preservados.");
        var allowedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {"cache"};
        for(int shard=0;shard<256;shard++)allowedDirectories.Add($"cache/{shard:x2}");
        foreach(var entry in expectedFiles.Keys)
        {
            var parts=entry.Split('/');
            for(int i=2;i<parts.Length;i++)allowedDirectories.Add(string.Join('/',parts.Take(i)).Substring(4));
        }
        foreach(string directory in expectedDirectories??[])
            allowedDirectories.Add(directory.StartsWith("vfs/",StringComparison.OrdinalIgnoreCase)?directory[4..]:directory);
        if(actualFiles.Keys.Any(IsRuntimeResourceCache))
        {
            allowedDirectories.Add("cache");
            if(actualFiles.Keys.Any(path=>path.StartsWith("vfs/cache/00/",StringComparison.OrdinalIgnoreCase)))
                allowedDirectories.Add("cache/00");
            if(actualFiles.Keys.Any(path=>path.StartsWith("vfs/cache/01/",StringComparison.OrdinalIgnoreCase)))
                allowedDirectories.Add("cache/01");
        }
        string[] extraDirectories=actualDirectories.Select(directory=>directory.Replace('\\','/'))
            .Except(allowedDirectories,StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missingDirectories=allowedDirectories.Where(directory=>
            expectedDirectories?.Any(source=>source.Equals("vfs/"+directory,StringComparison.OrdinalIgnoreCase))==true||
            expectedFiles.Keys.Any(path=>path.StartsWith("vfs/"+directory+"/",StringComparison.OrdinalIgnoreCase)))
            .Except(actualDirectories.Select(directory=>directory.Replace('\\','/')),StringComparer.OrdinalIgnoreCase).ToArray();
        if(extraDirectories.Length>0||missingDirectories.Length>0)
            throw new IOException("Os diretórios da VFS do perfil não correspondem ao inventário protegido. Ausentes: "+
                string.Join(", ",missingDirectories.Take(10))+"; inesperados: "+string.Join(", ",extraDirectories.Take(10))+
                ". Nenhum conteúdo foi removido.");
    }
    static async Task<List<IntegritySnapshot>> RefreshRuntimeCacheSnapshots(string targetVfs,
        IReadOnlyList<IntegritySnapshot>? previous,CancellationToken ct,IReadOnlyList<Entry> originals)
    {
        using var diagnostic=OperationTrace.MeasureActivity("derived_cache_validation");
        var expected=originals.ToDictionary(e=>e.Path.Replace('\\','/'),e=>e.Length,StringComparer.OrdinalIgnoreCase);
        var known=(previous??[]).ToDictionary(snapshot=>snapshot.Path,snapshot=>snapshot,StringComparer.OrdinalIgnoreCase);
        var refreshed=new List<IntegritySnapshot>();
        foreach(var file in Inventory(targetVfs))
        {
            string relative="vfs/"+file.Relative.Replace('\\','/');
            var kind=VfsPolicy.Classify(relative,expected);
            if(kind is not (VfsFileKind.ResourceCache or VfsFileKind.SdatPlaintext))continue;
            ct.ThrowIfCancellationRequested();
            if(file.Length<=0)
                throw new IOException($"Cache gerado vazio: {relative} (0 bytes). Feche o runtime e inspecione o perfil; o arquivo foi preservado.");
            var before=ReadFileSnapshot(file.Source);
            if(known.TryGetValue(relative,out var old)&&old.CanReuse(before))
            {
                OperationTrace.Observe("derived_hash_reused_"+kind);
                refreshed.Add(old);
                OperationTrace.RecordReuse(file.Length);
                continue;
            }
            OperationTrace.Observe("derived_hash_required_"+kind+"_"+
                (old is null?"missing_snapshot":old.ToFileSnapshot().ReuseFailureReason(before)??"snapshot_changed"));
            string? parent=kind==VfsFileKind.SdatPlaintext?file.Source[..^4]:null;
            if(parent is not null)NoLinks(parent);
            var parentBefore=parent is null?null:ReadFileSnapshot(parent);
            string hash=parent is null?await Hash(file.Source,ct):VfsPolicy.VerifySdat(parent,file.Source,ct);
            if(parentBefore is not null&&!parentBefore.SameObjectState(ReadFileSnapshot(parent!)))
                throw new IOException("Original SDAT alterado durante a validação: "+relative);
            var after=ReadFileSnapshot(file.Source);
            if(before is not null&&!before.SameObjectState(after))
                throw new IOException($"Cache gerado mudou durante a validação: {relative}. Feche o runtime e tente novamente.");
            if(after is not null)refreshed.Add(IntegritySnapshot.Create(relative,after,hash));
        }
        return refreshed;
    }
    static IOException IntegrityMismatch(string path,long expectedLength,long foundLength,string expectedHash,
        string foundHash,string provenance) => new(
        $"Divergência de integridade em '{path}' ({provenance}). "+
        $"Tamanho esperado: {expectedLength:N0} bytes; encontrado: {foundLength:N0} bytes. "+
        $"SHA-256 esperado: {expectedHash}; SHA-256 encontrado: {foundHash}. O arquivo divergente foi preservado.");
    static void ValidateInventory(string root,Manifest manifest)
    {
        var expected=manifest.Files.ToDictionary(e=>e.Path.Replace('\\','/'),e=>e.Length,StringComparer.OrdinalIgnoreCase);
        var actual=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Inventory(Child(root,"vfs/PS3_GAME")).Where(f=>!IsQuarantined(f.Relative)))
        {
            string path="vfs/PS3_GAME/"+file.Relative.Replace('\\','/');
            if(!IsGeneratedCache(path,expected))actual.Add(path,file.Length);
        }
        foreach(var file in Inventory(Child(root,"runtime")).Where(f=>!IsQuarantined(f.Relative)))
        {
            string path="runtime/"+file.Relative.Replace('\\','/');
            if(!IsGeneratedCache(path,expected))actual.Add(path,file.Length);
        }
        if(actual.Count!=expected.Count||actual.Any(e=>!expected.TryGetValue(e.Key,out long length)||length!=e.Value))
            throw new IOException("Os arquivos da instalação não correspondem ao manifesto. Use Reparar com a origem disponível.");
    }
    static bool IsQuarantined(string relative) =>
        System.Text.RegularExpressions.Regex.IsMatch(relative,@"\.corrupt-[0-9a-f]{32}$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static bool IsGeneratedCache<T>(string path,IReadOnlyDictionary<string,T> expected) =>
        VfsPolicy.Classify(path,expected)==VfsFileKind.SdatPlaintext;
    static void VerifyManagedDerivatives(string root,Manifest manifest,CancellationToken ct)
    {
        var expected=manifest.Files.ToDictionary(e=>e.Path.Replace('\\','/'),e=>e.Length,StringComparer.OrdinalIgnoreCase);
        foreach(var file in Inventory(Child(root,"vfs/PS3_GAME")))
            if(VfsPolicy.Classify("vfs/PS3_GAME/"+file.Relative.Replace('\\','/'),expected)==VfsFileKind.SdatPlaintext)
                VfsPolicy.VerifySdat(file.Source[..^4],file.Source,ct);
    }
    public static FileStream TakeLease(string root)
    {
        string locks=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DemonsSoulsLauncher-Locks");NoLinks(locks);Directory.CreateDirectory(locks);
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Full(root).ToUpperInvariant())));
        string path=Child(locks,key+".lock");
        try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){throw new IOException("Outra operação está usando esta instalação. Aguarde a conclusão antes de retomar ou jogar.");}
    }
    public static async Task Import(string selected,string destination,string elf,string runtime,IProgress<Transfer>? progress,CancellationToken ct,bool fixture=false)
    {
        using var lease=TakeLease(destination);
        foreach(var process in System.Diagnostics.Process.GetProcessesByName("des_boot"))
        {
            using(process){string? path=null;try{path=process.MainModule?.FileName;}catch{throw new IOException("Não foi possível confirmar se o jogo está em uso. Feche-o antes de reparar.");}
                if(path!=null&&Full(path).Equals(Full(System.IO.Path.Combine(destination,"runtime/des_boot.exe")),StringComparison.OrdinalIgnoreCase))throw new IOException("Feche o jogo desta instalação antes de importar/reparar recursos.");}
        }
        await ImportCore(selected,destination,elf,runtime,progress,ct,fixture);
    }
    static async Task ImportCore(string selected,string destination,string elf,string runtime,IProgress<Transfer>? progress,CancellationToken ct,bool fixture)
    {
        // The GUI passes the Advanced fields' contents straight into the
        // import; an empty path used to surface as ArgumentException("path")
        // from Path.GetFullPath inside Full(). State the missing field instead.
        if (string.IsNullOrWhiteSpace(selected))
            throw new IOException("Informe em Avançado a origem do jogo (pasta com PS3_GAME) antes de preparar/reparar.");
        if (string.IsNullOrWhiteSpace(destination))
            throw new IOException("Informe o destino da instalação antes de preparar/reparar.");
        if (string.IsNullOrWhiteSpace(elf))
            throw new IOException("Informe o EBOOT.ELF preparado no assistente de preparação (aba Instalação) ou em Avançado antes de preparar/reparar.");
        if (string.IsNullOrWhiteSpace(runtime))
            throw new IOException("Informe em Avançado o caminho do des_boot.exe antes de preparar/reparar.");
        string source,dest;
        List<string> directories=[];
        List<(string Relative,string Source,long Length)> files;
        using(var inventoryPhase=OperationTrace.Measure("Inventário e preparação do plano",0,0))
        {
            source=GameRoot(selected); dest=Full(destination); NoLinks(dest); CheckElf(elf,fixture); NoLinks(runtime);
            if (Under(dest,source) || Under(source,dest) || Full(source)==dest) throw new IOException("Origem e destino precisam ser separados.");
            if(!fixture)
                for (string? p=dest; p!=null; p=System.IO.Path.GetDirectoryName(p))
                    if (Directory.Exists(System.IO.Path.Combine(p,".git")) || File.Exists(System.IO.Path.Combine(p,".git"))) throw new IOException("Importe fora do repositório Git.");
            if (!File.Exists(runtime) || new FileInfo(runtime).Length < 1024) throw new IOException("Executável nativo de desenvolvimento ausente.");
            files=Inventory(source,directories).Select(e=>(Relative:"vfs/PS3_GAME/"+e.Relative.Replace('\\','/'),e.Source,e.Length)).ToList();
            files.Add(("runtime/EBOOT.ELF",Full(elf),new FileInfo(elf).Length));
            files.Add(("runtime/des_boot.exe",Full(runtime),new FileInfo(runtime).Length));
            inventoryPhase?.SetCounts(files.Count,files.Sum(entry=>entry.Length));
            inventoryPhase?.Complete();
        }
        string state=System.IO.Path.Combine(dest,"installation.json");
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any() && !File.Exists(state)) throw new IOException("Destino não vazio sem manifesto do launcher. Escolha outra pasta.");
        Manifest? old=File.Exists(state)?Load(dest):null;
        if (old!=null && (old.Origin != source || !old.Files.Select(e=>(e.Path,e.Length)).SequenceEqual(files.Select(e=>(e.Relative,e.Length))))) throw new IOException("Origem/inventário diferente do plano salvo. Escolha novo destino para não misturar instalações.");
        long total=files.Sum(e=>e.Length), needed=files.Where(e=>!File.Exists(Child(dest,e.Relative))).Sum(e=>e.Length);
        long reserve= Math.Max(256L*1024*1024,files.Max(e=>e.Length));
        var drive = new DriveInfo(System.IO.Path.GetPathRoot(dest)!);
        if (drive.AvailableFreeSpace < checked(needed+reserve)) throw new IOException($"Espaço insuficiente. Necessário: {needed+reserve:N0} bytes livres.");
        Directory.CreateDirectory(dest);
        if(old!=null&&old.Synthetic!=fixture)throw new IOException("Destino de teste não pode ser usado como instalação real.");
        var plan=new Manifest { Origin=source, Complete=false, Synthetic=fixture, Directories=directories.Select(d=>"vfs/PS3_GAME/"+d.Replace('\\','/')).Order().ToList() }; long done=0; int index=0;
        foreach(string directory in plan.Directories)Directory.CreateDirectory(Child(dest,directory));
        // Persist the inventory before expensive hashing so interruption has a
        // recognized, owned destination. Empty hashes are a pending-plan marker.
        var oldHashes=old?.Files.ToDictionary(e=>e.Path,e=>e.Sha256,StringComparer.OrdinalIgnoreCase);
        foreach(var e in files) plan.Files.Add(new(e.Relative,e.Length,oldHashes?.GetValueOrDefault(e.Relative)??new string('0',64)));
        using(var planPhase=OperationTrace.Measure("Gravação do plano retomável",files.Count,total))
        {
            Save(state,plan);
            planPhase?.Complete();
        }
        using(var sourceHashPhase=OperationTrace.Measure("Hash SHA-256 da origem",files.Count,total))
        {
            foreach(var e in files)
            {
                ct.ThrowIfCancellationRequested();
                string hash=await Hash(e.Source,ct);
                if (new FileInfo(e.Source).Length != e.Length) throw new IOException("Origem mudou durante inventário: "+e.Relative);
                if (old!=null && old.Files[index].Sha256 != new string('0',64) && old.Files[index].Sha256 != hash) throw new IOException("Conteúdo da origem mudou desde a importação. Preserve a instalação e escolha novo destino.");
                plan.Files[index++]=new(e.Relative,e.Length,hash); done+=e.Length;
                progress?.Report(new("Verificando origem por SHA-256",e.Relative,done,total,0,index,files.Count));
            }
            sourceHashPhase?.Complete();
        }
        using(var planPhase=OperationTrace.Measure("Gravação dos hashes da preparação retomável",files.Count,total))
        {
            Save(state,plan);
            planPhase?.Complete();
        }
        done=0; long copied=0; index=0;
        long replacementBytes=0;
        var validTargets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var targetCheck=OperationTrace.Measure("Hash SHA-256 do destino para retomada",plan.Files.Count,total))
        {
            foreach(var e in plan.Files)
            {
                ct.ThrowIfCancellationRequested();string target=Child(dest,e.Path);
                if(File.Exists(target)&&new FileInfo(target).Length==e.Length&&
                   string.Equals(await Hash(target,ct),e.Sha256,StringComparison.OrdinalIgnoreCase))
                    validTargets.Add(e.Path);
                else replacementBytes=checked(replacementBytes+e.Length);
                done+=e.Length;index++;
                progress?.Report(new("Verificando arquivos existentes para retomar",e.Path,done,total,0,index,plan.Files.Count));
            }
            targetCheck?.Complete();
        }
        CheckSpace(drive.AvailableFreeSpace,checked(replacementBytes+reserve));
        done=0;index=0;
        using(var copyPhase=OperationTrace.Measure("Cópia, flush durável, fechamento e SHA-256 temporário",plan.Files.Count,total))
        {
            foreach(var e in plan.Files)
            {
                ct.ThrowIfCancellationRequested(); var origin=files[index].Source; string target=Child(dest,e.Path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                if (validTargets.Contains(e.Path))
                {
                    done+=e.Length; index++; progress?.Report(new("Reutilizando arquivo verificado",e.Path,done,total,copied,index,files.Count)); continue;
                }
                string partial=target+".partial"; NoLinks(partial);
                using (var input=new FileStream(origin,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true))
                using (var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true))
                {
                    byte[] buffer=new byte[1024*1024]; int n;
                    while((n=await input.ReadAsync(buffer,ct))>0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0,n),ct); copied+=n;
                        progress?.Report(new("Copiando arquivos",e.Path,done+output.Position,total,copied,index,files.Count));
                    }
                    await output.FlushAsync(ct); output.Flush(true);
                }
                ct.ThrowIfCancellationRequested();
                if(new FileInfo(partial).Length!=e.Length || await Hash(partial,ct)!=e.Sha256) throw new IOException($"Integridade falhou; temporário preservado: {e.Path} ({new FileInfo(partial).Length}/{e.Length} bytes)");
                ct.ThrowIfCancellationRequested(); NoLinks(target);
                if(File.Exists(target)) File.Move(target,target+".corrupt-"+Guid.NewGuid().ToString("N"));
                File.Move(partial,target); done+=e.Length;
                index++; progress?.Report(new("Arquivo transferido e verificado",e.Path,done,total,copied,index,files.Count));
            }
            copyPhase?.Complete();
        }
        // Compare source inventory again. Never certify a copy from a changing tree.
        using(var finalVerification=OperationTrace.Measure("Validação estrutural pós-cópia",plan.Files.Count,total))
        {
            var end=Inventory(source);
            if(!end.Select(e=>("vfs/PS3_GAME/"+e.Relative.Replace('\\','/'),e.Length)).SequenceEqual(files.Take(files.Count-2).Select(e=>(e.Relative,e.Length)))) throw new IOException("Árvore de origem mudou durante a cópia.");
            ct.ThrowIfCancellationRequested();
            ValidateInventory(dest,plan);
            foreach(string directory in plan.Directories)
                if(!Directory.Exists(Child(dest,directory)))throw new IOException("Pasta ausente após a cópia: "+directory);
            GameRoot(Child(dest,"vfs/PS3_GAME"));
            CheckElf(Child(dest,"runtime/EBOOT.ELF"),fixture,verifyHash:false);
            progress?.Report(new("Validação estrutural concluída após hashes SHA-256", "",total,total,copied,plan.Files.Count,plan.Files.Count));
            finalVerification?.Complete();
        }
        using(var finalization=OperationTrace.Measure("Gravação dos metadados e conclusão",plan.Files.Count,total))
        {
            for(int i=0;i<plan.Files.Count;i++)
            {
                string target=Child(dest,plan.Files[i].Path);
                var snapshot=ReadFileSnapshot(target);
                plan.Files[i]=plan.Files[i] with
                {
                    LastWriteUtcTicks=snapshot?.LastWriteUtcTicks??File.GetLastWriteTimeUtc(target).Ticks,
                    CreationUtcTicks=snapshot?.CreationUtcTicks??File.GetCreationTimeUtc(target).Ticks,
                    VolumeSerialNumber=snapshot?.VolumeSerialNumber,
                    FileId=snapshot?.FileId,
                    Usn=snapshot?.Usn
                };
            }
            plan.Complete=true;
            SignManifestSnapshot(dest,plan);
            Save(state,plan);
            finalization?.Complete();
        }
    }
    public static void CheckSpace(long available,long required){if(required<0||available<required)throw new IOException($"Espaço insuficiente: {required:N0} bytes necessários, {available:N0} disponíveis.");}
}
