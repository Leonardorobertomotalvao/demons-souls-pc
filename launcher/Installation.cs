using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DemonsSouls.Launcher;

public record Entry(string Path, long Length, string Sha256, long? LastWriteUtcTicks = null, long? CreationUtcTicks = null);
public record Transfer(string Phase, string File, long Done, long Total, long Copied, int Files, int Count);
public record LaunchVerification(bool UsedMetadataFastPath, long ElapsedMilliseconds, int FilesChecked);
public sealed class Manifest
{
    public int Schema { get; set; } = 1;
    public string TitleId { get; set; } = "BLUS30443";
    public string AppVersion { get; set; } = "01.00";
    public string Origin { get; set; } = "";
    public bool Complete { get; set; }
    public bool Synthetic { get; set; }
    public List<Entry> Files { get; set; } = [];
    public List<string> Directories { get; set; } = [];
}

public static class Installation
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Full(string path) => System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
    public static bool Under(string path, string root) => Full(path).StartsWith(Full(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void NoLinks(string path)
    {
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
        byte[] data = File.ReadAllBytes(file);
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
        if (!File.Exists(System.IO.Path.Combine(root,"PARAM.SFO"))) root = System.IO.Path.Combine(root,"PS3_GAME");
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
        NoLinks(path);
        using var f = File.OpenRead(path); Span<byte> h = stackalloc byte[64];
        if (f.Read(h) != 64 || !h[..6].SequenceEqual(new byte[]{127,69,76,70,2,2}) || BinaryPrimitives.ReadUInt16BigEndian(h[18..20]) != 21 || BinaryPrimitives.ReadUInt16BigEndian(h[16..18]) != 2)
            throw new IOException("Selecione o EBOOT.ELF PPC64 big-endian já preparado legitimamente. EBOOT.BIN/SELF criptografado não é aceito aqui.");
        if(!fixture&&verifyHash)
        {
            f.Position=0;
            if(f.Length!=28222408 || Convert.ToHexString(SHA256.HashData(f))!="90EAA5E9E48B4874292BADC311CCEA253E6A622FF188BFBDBE76E6F6821269D8")
                throw new IOException("O ELF não corresponde à build recompilada BLUS30443 01.00 validada neste projeto. Nenhuma conversão/descriptografia foi executada.");
        }
    }
    public static async Task<string> Hash(string path, CancellationToken ct)
    {
        using var f = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
        return Convert.ToHexString(await SHA256.HashDataAsync(f,ct));
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
        NoLinks(root);
        string path = Child(root,"installation.json");
        if (new FileInfo(path).Length > 16*1024*1024) throw new IOException("Manifesto excede limite.");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) ?? throw new IOException("Manifesto inválido.");
        if (manifest.Schema != 1 || manifest.TitleId != "BLUS30443" || manifest.AppVersion != "01.00" || manifest.Files.Count == 0 || manifest.Files.Count > 100000) throw new IOException("Instalação incompatível.");
        foreach (var entry in manifest.Files)
            if (entry.Length < 0 || entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit) || !(entry.Path.StartsWith("vfs/PS3_GAME/",StringComparison.Ordinal) || entry.Path is "runtime/EBOOT.ELF" or "runtime/des_boot.exe")) throw new IOException("Entrada de manifesto inválida.");
            else Child(root,entry.Path);
        if (manifest.Files.Select(e=>e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count) throw new IOException("Manifesto duplicado.");
        foreach(string directory in manifest.Directories) {if(!directory.StartsWith("vfs/PS3_GAME/",StringComparison.Ordinal))throw new IOException("Pasta de manifesto inválida.");Child(root,directory);}
        return manifest;
    }
    public static async Task Verify(string root, IProgress<Transfer>? progress, CancellationToken ct,bool allowIncomplete=false,bool fixture=false)
    {
        var m = Load(root); if ((!m.Complete&&!allowIncomplete)||m.Synthetic!=fixture) throw new IOException("Importação incompleta ou fixture de teste. Não é uma instalação jogável.");
        ValidateInventory(root,m);
        foreach(string directory in m.Directories) if(!Directory.Exists(Child(root,directory)))throw new IOException("Pasta ausente: "+directory);
        long done=0, total=m.Files.Sum(e=>e.Length); int n=0;
        foreach(var e in m.Files)
        {
            ct.ThrowIfCancellationRequested(); string file=Child(root,e.Path);
            if (!File.Exists(file) || new FileInfo(file).Length != e.Length || await Hash(file,ct) != e.Sha256) throw new IOException("Arquivo ausente ou corrompido: " + e.Path + ". Use Reparar com a origem disponível.");
            done += e.Length; n++; progress?.Report(new("Verificando",e.Path,done,total,0,n,m.Files.Count));
        }
        GameRoot(Child(root,"vfs/PS3_GAME")); CheckElf(Child(root,"runtime/EBOOT.ELF"),fixture);
    }
    public static async Task<LaunchVerification> VerifyForLaunch(string root,IProgress<Transfer>? progress,CancellationToken ct,bool fixture=false)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        root=Full(root);
        var manifest=Load(root);
        if(!manifest.Complete||manifest.Synthetic!=fixture)
            throw new IOException("Instalação incompleta ou incompatível. Prepare ou repare os arquivos antes de jogar.");

        bool fast=manifest.Files.All(e=>e.LastWriteUtcTicks is > 0&&e.CreationUtcTicks is > 0)
            &&MatchesMetadataSnapshot(root,manifest);
        if(fast)
        {
            GameRoot(Child(root,"vfs/PS3_GAME"));
            CheckElf(Child(root,"runtime/EBOOT.ELF"),fixture,verifyHash:false);
            timer.Stop();
            return new(true,timer.ElapsedMilliseconds,manifest.Files.Count);
        }

        await Verify(root,progress,ct,fixture:fixture);
        for(int i=0;i<manifest.Files.Count;i++)
        {
            string path=Child(root,manifest.Files[i].Path);
            manifest.Files[i]=manifest.Files[i] with
            {
                LastWriteUtcTicks=File.GetLastWriteTimeUtc(path).Ticks,
                CreationUtcTicks=File.GetCreationTimeUtc(path).Ticks
            };
        }
        Save(Child(root,"installation.json"),manifest);
        timer.Stop();
        return new(false,timer.ElapsedMilliseconds,manifest.Files.Count);
    }
    static bool MatchesMetadataSnapshot(string root,Manifest manifest)
    {
        var expected=manifest.Files.ToDictionary(e=>e.Path.Replace('\\','/'),StringComparer.OrdinalIgnoreCase);
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
        if(actual.Count!=expected.Count)return false;
        foreach(var (path,length) in actual)
        {
            if(!expected.TryGetValue(path,out var entry)||entry.Length!=length)return false;
            string full=Child(root,path);
            if(File.GetLastWriteTimeUtc(full).Ticks!=entry.LastWriteUtcTicks||
               File.GetCreationTimeUtc(full).Ticks!=entry.CreationUtcTicks)return false;
        }
        return true;
    }
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
        path.EndsWith(".dec",StringComparison.OrdinalIgnoreCase)&&expected.ContainsKey(path[..^4]);
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
        string source=GameRoot(selected), dest=Full(destination); NoLinks(dest); CheckElf(elf,fixture); NoLinks(runtime);
        if (Under(dest,source) || Under(source,dest) || Full(source)==dest) throw new IOException("Origem e destino precisam ser separados.");
        for (string? p=dest; p!=null; p=System.IO.Path.GetDirectoryName(p))
            if (Directory.Exists(System.IO.Path.Combine(p,".git")) || File.Exists(System.IO.Path.Combine(p,".git"))) throw new IOException("Importe fora do repositório Git.");
        if (!File.Exists(runtime) || new FileInfo(runtime).Length < 1024) throw new IOException("Executável nativo de desenvolvimento ausente.");
        List<string> directories=[];
        var files=Inventory(source,directories).Select(e=>(Relative:"vfs/PS3_GAME/"+e.Relative.Replace('\\','/'),e.Source,e.Length)).ToList();
        files.Add(("runtime/EBOOT.ELF",Full(elf),new FileInfo(elf).Length));
        files.Add(("runtime/des_boot.exe",Full(runtime),new FileInfo(runtime).Length));
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
        Save(state,plan);
        foreach(var e in files)
        {
            ct.ThrowIfCancellationRequested();
            string hash=await Hash(e.Source,ct);
            if (new FileInfo(e.Source).Length != e.Length) throw new IOException("Origem mudou durante inventário: "+e.Relative);
            if (old!=null && old.Files[index].Sha256 != new string('0',64) && old.Files[index].Sha256 != hash) throw new IOException("Conteúdo da origem mudou desde a importação. Preserve a instalação e escolha novo destino.");
            plan.Files[index++]=new(e.Relative,e.Length,hash); done+=e.Length;
            progress?.Report(new("Verificando origem",e.Relative,done,total,0,index,files.Count));
        }
        Save(state,plan); done=0; long copied=0; index=0;
        long replacementBytes=0;
        foreach(var e in plan.Files)
        {
            ct.ThrowIfCancellationRequested();string target=Child(dest,e.Path);
            if(!File.Exists(target)||new FileInfo(target).Length!=e.Length||await Hash(target,ct)!=e.Sha256)replacementBytes=checked(replacementBytes+e.Length);
        }
        CheckSpace(drive.AvailableFreeSpace,checked(replacementBytes+reserve));
        foreach(var e in plan.Files)
        {
            ct.ThrowIfCancellationRequested(); var origin=files[index].Source; string target=Child(dest,e.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            if (File.Exists(target) && new FileInfo(target).Length==e.Length && await Hash(target,ct)==e.Sha256)
            { done+=e.Length; index++; progress?.Report(new("Retomando: arquivo íntegro",e.Path,done,total,copied,index,files.Count)); continue; }
            string partial=target+".partial"; NoLinks(partial);
            using (var input=new FileStream(origin,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true))
            using (var output=new FileStream(partial,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true))
            {
                byte[] buffer=new byte[1024*1024]; int n;
                while((n=await input.ReadAsync(buffer,ct))>0)
                {
                    await output.WriteAsync(buffer.AsMemory(0,n),ct); copied+=n;
                    progress?.Report(new("Copiando",e.Path,done+output.Position,total,copied,index,files.Count));
                }
                await output.FlushAsync(ct); output.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            if(new FileInfo(partial).Length!=e.Length || await Hash(partial,ct)!=e.Sha256) throw new IOException($"Integridade falhou; temporário preservado: {e.Path} ({new FileInfo(partial).Length}/{e.Length} bytes)");
            ct.ThrowIfCancellationRequested(); NoLinks(target);
            if(File.Exists(target)) File.Move(target,target+".corrupt-"+Guid.NewGuid().ToString("N"));
            File.Move(partial,target); done+=e.Length;
            index++; progress?.Report(new("Arquivo verificado",e.Path,done,total,copied,index,files.Count));
        }
        // Compare source inventory again. Never certify a copy from a changing tree.
        var end=Inventory(source);
        if(!end.Select(e=>("vfs/PS3_GAME/"+e.Relative.Replace('\\','/'),e.Length)).SequenceEqual(files.Take(files.Count-2).Select(e=>(e.Relative,e.Length)))) throw new IOException("Árvore de origem mudou durante a cópia.");
        await Verify(dest,progress,ct,true,fixture);
        for(int i=0;i<plan.Files.Count;i++)
        {
            string target=Child(dest,plan.Files[i].Path);
            plan.Files[i]=plan.Files[i] with
            {
                LastWriteUtcTicks=File.GetLastWriteTimeUtc(target).Ticks,
                CreationUtcTicks=File.GetCreationTimeUtc(target).Ticks
            };
        }
        plan.Complete=true; Save(state,plan);
    }
    public static void CheckSpace(long available,long required){if(required<0||available<required)throw new IOException($"Espaço insuficiente: {required:N0} bytes necessários, {available:N0} disponíveis.");}
}
