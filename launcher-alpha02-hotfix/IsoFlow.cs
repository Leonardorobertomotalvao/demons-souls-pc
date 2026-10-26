using System.Diagnostics;
using System.Security.Cryptography;

namespace DemonsSouls.Launcher;

// Automates the ISO preparation steps the project's own pipeline legitimately
// supports: read-only mounting, edition/version identification from PARAM.SFO,
// and ladders for already-prepared local components (EBOOT.ELF, approved
// runtime). The launcher never decrypts EBOOT.BIN/SELF itself and never claims
// a component was generated from the selected ISO unless it really was (this
// ladder only *finds* user-side legitimate artifacts and checks identities).
public static class IsoFlow
{
    public sealed record PreparedIso(string IsoPath,string? DriveRoot,string? GameRoot,long ImageBytes,string Detail,bool MountedByLauncher=false);

    static async Task<(string Output,string Errors,int ExitCode)> RunPowerShell(string script,string isoPath,int seconds,CancellationToken ct)
    {
        var start=new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true
        };
        start.Environment["DS_SELECTED_ISO"]=Installation.Full(isoPath);
        start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var process=Process.Start(start)!;
        Task<string> stdout=process.StandardOutput.ReadToEndAsync(),stderr=process.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        int code;
        try{await process.WaitForExitAsync(timeout.Token);code=process.ExitCode;}
        catch(OperationCanceledException)
        {
            try{process.Kill();}catch(Exception){/* process may have exited first */}
            if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);
            throw new IOException($"O Windows não respondeu ao consultar a ISO em {seconds} segundos. Monte-a com duplo clique no Explorador de Arquivos e selecione a unidade montada.");
        }
        return (await stdout,await stderr,code);
    }

    static string? VolumeWithPs3Game(string output) =>
        output.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(r=>r.Length>=2&&r[1]==':'&&Directory.Exists(Path.Combine(r,"PS3_GAME")));

    // Query only: the drive where this ISO is already mounted (if any).
    public static async Task<string?> MountedVolume(string isoPath,CancellationToken ct)
    {
        if(!File.Exists(isoPath))throw new IOException("A ISO informada não existe em disco.");
        (string output,_,_)=await RunPowerShell(
            "$ErrorActionPreference='Stop'; Get-DiskImage -ImagePath $env:DS_SELECTED_ISO | Get-Volume | Where-Object DriveLetter | ForEach-Object { $_.DriveLetter + ':\\' }",
            isoPath,20,ct);
        return VolumeWithPs3Game(output);
    }

    // Mounts the image read-only when it is not mounted yet and returns the
    // mounted drive root with a PS3_GAME tree. Never writes to the image.
    public static async Task<string?> MountIso(string isoPath,CancellationToken ct)
    {
        if(!File.Exists(isoPath))throw new IOException("A ISO informada não existe em disco.");
        // Note: when the image is already mounted by Explorer, Mount-DiskImage
        // succeeds and returns the existing drive instead of re-mounting.
        (string output,_,int code)=await RunPowerShell(
            "$ErrorActionPreference='Stop'; Mount-DiskImage -ImagePath $env:DS_SELECTED_ISO -Access ReadOnly -StorageType ISO -ErrorAction Stop | Out-Null; Get-DiskImage -ImagePath $env:DS_SELECTED_ISO | Get-Volume | Where-Object DriveLetter | ForEach-Object { $_.DriveLetter + ':\\' }",
            isoPath,60,ct);
        if(code!=0)return null; // policy/admin refusal → caller explains the double-click route.
        return VolumeWithPs3Game(output);
    }

    // Ejects the image we mounted earlier. Used only from the launcher's own
    // preparation flow at the end of an attempt (success, cancel or failure)
    // because a running import keeps source files open until it returns; the
    // wizard only calls this after the import task has fully finished. Never
    // dismounts a volume the user mounted on their own.
    public static async Task<bool> DismountIso(string isoPath,CancellationToken ct)
    {
        if(!FileExistsQuiet(isoPath))return false;
        (string output,_,int code)=await RunPowerShell(
            "$ErrorActionPreference='Stop'; Dismount-DiskImage -ImagePath $env:DS_SELECTED_ISO -ErrorAction Stop | Out-Null; Write-Output 'DISMOUNTED'",
            isoPath,30,ct);
        return code==0&&output.Contains("DISMOUNTED",StringComparison.Ordinal);
    }

    // Full selection step: validate, mount when needed, resolve the PS3_GAME
    // root and describe the edition from PARAM.SFO without copying anything.
    public static async Task<PreparedIso> Prepare(string isoPath,CancellationToken ct=default)
    {
        if(!File.Exists(isoPath))throw new IOException("A ISO informada não existe em disco.");
        if(!string.Equals(Path.GetExtension(isoPath),".iso",StringComparison.OrdinalIgnoreCase))
            throw new IOException("O arquivo selecionado não tem a extensão .iso. Selecione a imagem do jogo no formato ISO.");
        long imageBytes=new FileInfo(isoPath).Length;
        string? drive=await MountedVolume(isoPath,ct);
        bool mountAttempted=false;
        if(drive is null){mountAttempted=true;drive=await MountIso(isoPath,ct);}
        if(drive is null)
            return new(isoPath,null,null,imageBytes,
                "O Windows não montou a imagem"+(mountAttempted?" automaticamente":"")+". Faça duplo clique no arquivo ISO no Explorador de Arquivos para montá-la como unidade virtual e repita a seleção; o launcher nunca altera a imagem.",false);
        string detail;string? gameRoot=null;
        try
        {
            gameRoot=Installation.GameRoot(drive);
            detail=Describe(gameRoot,imageBytes);
        }
        catch(Exception e)
        {
            gameRoot=null;
            detail="A imagem está montada em "+drive+", mas não contém a edição compatível: "+e.Message;
        }
        return new(isoPath,drive,gameRoot,imageBytes,detail,mountAttempted);
    }

    // Only the North-American edition 01.00 carries the approved SHA-256
    // identities the launcher accepts; reject anything else before copying a
    // single file so mistakes surface immediately.
    public const string SupportedTitleId="BLUS30443";
    public static void CheckSupportedEdition(string gameRoot)
    {
        var sfo=Installation.Sfo(Path.Combine(gameRoot,"PARAM.SFO"));
        string edition=sfo.GetValueOrDefault("TITLE_ID")??"";
        string version=sfo.GetValueOrDefault("APP_VER")??"";
        if(!edition.Equals(SupportedTitleId,StringComparison.OrdinalIgnoreCase))
            throw new IOException($"A imagem contém o Title ID {edition}, mas o launcher aceita somente a edição {SupportedTitleId} (norte-americana, versão 1.00). A imagem não foi alterada e nada foi copiado.");
        if(!version.TrimStart('0').StartsWith("1.",StringComparison.Ordinal))
            throw new IOException($"A edição {edition} é compatível, mas a versão informada pelo PARAM.SFO é {version}; o launcher valida a versão 01.00. A imagem não foi alterada e nada foi copiado.");
    }

    public static string Describe(string gameRoot,long imageBytes)
    {
        var sfo=Installation.Sfo(Path.Combine(gameRoot,"PARAM.SFO"));
        string edition=sfo.GetValueOrDefault("TITLE_ID")??"?", version=sfo.GetValueOrDefault("APP_VER")??"?";
        OperationTrace.Observe("iso_identity_"+edition+"_"+version);
        return $"Edição compatível detectada: {edition} · versão {version} · imagem {imageBytes/(1024.0*1024*1024):N1} GiB montada somente leitura pelo Windows.";
    }

    // Already-prepared ELF ladder, in priority order: remembered path
    // (previously approved), ELF beside the source tree (common RPCS3 dump
    // location), ELF already installed in the destination runtime folder.
    // Strict identity check (PPC64 big-endian header + pinned SHA-256); no
    // conversion and no hash relaxation outside the launcher self-test.
    public static IReadOnlyList<string> EbootElfCandidates(string? gameRoot,string? destination,string? remembered)
    {
        var list=new List<string>();
        if(!string.IsNullOrWhiteSpace(remembered))list.Add(remembered);
        if(!string.IsNullOrWhiteSpace(gameRoot))
        {
            string? parent=Directory.GetParent(gameRoot)?.FullName;
            list.Add(Path.Combine(gameRoot,"EBOOT.ELF"));
            list.Add(Path.Combine(gameRoot,"USRDIR","EBOOT.ELF"));
            if(parent!=null)list.Add(Path.Combine(parent,"EBOOT.ELF"));
            if(parent!=null)list.Add(Path.Combine(parent,"PS3_GAME","USRDIR","EBOOT.ELF"));
            if(parent!=null)list.Add(Path.Combine(parent,"work","game","EBOOT.ELF"));
        }
        if(!string.IsNullOrWhiteSpace(destination)&&Directory.Exists(destination))
            list.Add(Path.Combine(destination,"runtime","EBOOT.ELF"));
        return list.Where(p=>!string.IsNullOrWhiteSpace(p)&&FileExistsQuiet(p))
            .Select(p=>GetFullPath(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? FindEbootElf(string? gameRoot,string? destination,string? remembered,bool fixture)
    {
        foreach(string candidate in EbootElfCandidates(gameRoot,destination,remembered))
            try
            {
                Installation.CheckElf(candidate,fixture);
                return candidate;
            }
            catch(Exception){OperationTrace.Observe("elf_candidate_rejected_"+Path.GetFileName(candidate));}
        return null;
    }

    // Approved-runtime ladder: packaged copy beside the launcher, the
    // repository development build, an already-installed copy and the
    // remembered selection. Only identity-approved SHA-256 qualifies — the
    // launcher never treats an unknown build as the ISO's product.
    public static IReadOnlyList<string> RuntimeCandidates(string? destination,string? remembered)
    {
        var list=new List<string> {GetPackagedRuntime()};
        string? repo=FindRepositoryRoot();
        if(repo!=null)list.Add(Path.Combine(repo,"port","build","des_boot.exe"));
        if(!string.IsNullOrWhiteSpace(destination)&&Directory.Exists(destination))
            list.Add(Path.Combine(destination,"runtime","des_boot.exe"));
        if(!string.IsNullOrWhiteSpace(remembered))list.Add(remembered);
        return list.Select(GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>FileExistsQuiet(p)).ToList();
    }

    static string GetPackagedRuntime() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Game","des_boot.exe"));

    public static string? FindApprovedRuntime(string? destination,string? remembered)
    {
        foreach(string candidate in RuntimeCandidates(destination,remembered))
            try
            {
                if(new FileInfo(candidate).Length>=1024&&RuntimeService.IsApproved60FpsRuntimeHash(Sha256Hex(candidate)))
                    return candidate;
            }
            catch(Exception){OperationTrace.Observe("runtime_candidate_rejected_"+Path.GetFileName(candidate));}
        return null;
    }

    // Design limitation stated honestly: the repository distributed to users
    // does not carry the ps3recomp fork nor the game-derived lifted inputs, so
    // per-user des_boot.exe generation is NOT offered. This diagnostic reports
    // which development-pipeline prerequisites exist on a machine.
    public static IReadOnlyList<(string Requirement,bool Present,string Hint)> NativeBuildEnvironment(string? repoRoot=null)
    {
        repoRoot=repoRoot??FindRepositoryRoot();
        string? workGame=repoRoot is null?null:Path.Combine(repoRoot,"work","game","EBOOT.ELF");
        int recSources=0;
        if(repoRoot!=null)
            try{recSources=Directory.EnumerateFiles(Path.Combine(repoRoot,"work","recomp"),"*.cpp",SearchOption.TopDirectoryOnly).Count();}catch{}
        int spuImages=0;
        if(repoRoot!=null)
            try{spuImages=Directory.EnumerateFiles(Path.Combine(repoRoot,"work","spu_images")).Count();}catch{}
        bool ps3recomp=repoRoot!=null&&Directory.Exists(Path.Combine(repoRoot,"ps3recomp"));
        return new[]
        {
            ("Fork ps3recomp (lifter PPU/SPU)",ps3recomp,"clone JonathanDC64/ps3recomp no commit fixado no README.md §3 (EN); ele não vem no repositório do usuário"),
            ("work/recomp com C++ extraído do PPU",recSources>0,$"arquivos extraídos: {recSources} (esperado >0; material derivado do jogo, não distribuído)"),
            ("work/spu_images (trabalhos SPU)",spuImages>0,$"bins: {spuImages} (material derivado do jogo, não distribuído)"),
            ("work/game/EBOOT.ELF preparado",workGame!=null&&FileExistsQuiet(workGame),workGame??""),
            ("port/CMakeLists.txt do runtime",repoRoot!=null&&FileExistsQuiet(Path.Combine(repoRoot,"port","CMakeLists.txt")),"fontes da port permanecem no repositório"),
            ("scripts/apply_boot_patches.py",repoRoot!=null&&FileExistsQuiet(Path.Combine(repoRoot,"scripts","apply_boot_patches.py")),"scripts/ permanece no repositório"),
            ("cmake",ToolOnPath("cmake.exe"),"instale o CMake 4.x"),
            ("ninja",ToolOnPath("ninja.exe"),"instale o Ninja 1.x"),
            ("clang (LLVM)",ToolOnPath("clang.exe"),"instale o LLVM 17+"),
            ("python",ToolOnPath("python.exe"),"instale o Python 3.x"),
        };
    }

    public sealed record ToolProbe(string? Path,string Detail);
    static IEnumerable<string> ScetoolCandidates()
    {
        string pathVariable=Environment.GetEnvironmentVariable("PATH")??"";
        foreach(string directory in pathVariable.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
            yield return Path.Combine(directory,"scetool.exe");
        yield return Path.Combine(AppContext.BaseDirectory,"scetool.exe");
        foreach(string programRoot in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})
            yield return Path.Combine(programRoot,"scetool","scetool.exe");
    }

    // scetool (GPL) is the project-documented local, user-installed option B
    // in docs/03-setup.md; the launcher detects it, never bundles it. A
    // user-configured tool path (Preferences.ScetoolPath) wins when the file
    // really exists, so custom install locations are honored without
    // guessing; a broken configured path is refused instead of silently
    // replaced by an auto-detected copy.
    public static ToolProbe FindScetool(string? preferred=null)
    {
        if(!string.IsNullOrWhiteSpace(preferred))
        {
            if(FileExistsQuiet(preferred))
                return new(preferred,"Ferramenta local de descriptografia configurada pelo usuário: "+preferred);
            return new(null,"A ferramenta local configurada não existe em disco: "+preferred+". Nenhuma outra ferramenta compatível foi localizada e o launcher não descriptografa por conta própria; use o botão FERRAMENTA PARA EBOOT.BIN para reconfigurar ou o dump de executável do RPCS3 (docs/03-setup.md §3).");
        }
        foreach(string candidate in ScetoolCandidates())
            if(FileExistsQuiet(candidate))
                return new(candidate,"Ferramenta local de descriptografia detectada (instalada pelo usuário; licença GPL): "+candidate);
        return new(null,"Nenhuma ferramenta local de descriptografia compatível foi localizada. O launcher não descriptografa arquivos por conta própria. Para gerar o EBOOT.ELF preparado, use o botão FERRAMENTA PARA EBOOT.BIN para apontar sua cópia do scetool (rota B de docs/03-setup.md §3) ou o dump de executável do RPCS3 (rota A).");
    }

    // Runs the selected tool with NO arguments — the project never invents
    // tool parameters (scetool's own launched usage text is what a
    // documentation-respecting probe can rely on). This only answers "does
    // the executable start and respond within a short window"; the decrypt
    // invocation itself remains exactly as documented for the tool.
    public static async Task<(bool Started,string Detail)> ProbeToolRespondsAsync(string toolPath,CancellationToken ct)
    {
        try
        {
            var start=new ProcessStartInfo(toolPath)
            {
                UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardOutput=true,RedirectStandardError=true,
                WorkingDirectory=Path.GetDirectoryName(toolPath)??"."
            };
            using var process=Process.Start(start)!;
            Task<string> stdout=process.StandardOutput.ReadToEndAsync(),stderr=process.StandardError.ReadToEndAsync();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            int code;
            try{await process.WaitForExitAsync(timeout.Token);code=process.ExitCode;}
            catch(OperationCanceledException)
            {
                try{process.Kill();}catch(Exception){/* may have exited first */}
                if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);
                return (false,"O teste de resposta da ferramenta excedeu 8 segundos e o processo foi encerrado: "+toolPath);
            }
            string text=(await stderr+" "+await stdout).Trim();
            bool started=code!=0||text.Length>0;
            OperationTrace.Observe("local_tool_probe_"+(started?"responds":"no_output"));
            return (started,"processo executado, código "+code+(text.Length>0?" · saída da ferramenta: "+text[..Math.Min(240,text.Length)]:""));
        }
        catch(Exception e)
        {
            return (false,"a ferramenta não pôde ser iniciada ("+e.GetBaseException().Message+"): "+toolPath);
        }
    }

    // Runs the user-provided local tool to decrypt (no keys of ours, no crypto
    // code owned by the launcher) and validates the result against the pinned
    // ELF identity. Output stays in the launcher data home, never in-source or
    // in the installation destination.
    public static async Task<string> ScetoolDecryptAsync(string scetoolExe,string gameRoot,string outputDirectory,CancellationToken ct)
    {
        string bin=Path.Combine(gameRoot,"USRDIR","EBOOT.BIN");
        if(!FileExistsQuiet(bin))throw new IOException("EBOOT.BIN de origem ausente: "+bin+". Use SELECIONAR ISO para montar a imagem primeiro.");
        long binBytes=0;try{binBytes=new FileInfo(bin).Length;}catch(Exception){/* evidence only */}
        Directory.CreateDirectory(outputDirectory);
        string output=Path.Combine(outputDirectory,"EBOOT.ELF");
        try{File.Delete(output);}catch(IOException e){throw new IOException("Não foi possível limpar o ELF temporário anterior: "+e.Message);}
        var start=new ProcessStartInfo(scetoolExe)
        {
            UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true,
            WorkingDirectory=Path.GetDirectoryName(scetoolExe)??"."
        };
        start.ArgumentList.Add("-d");start.ArgumentList.Add(bin);start.ArgumentList.Add(output);
        using var process=Process.Start(start)!;
        Task<string> stdout=process.StandardOutput.ReadToEndAsync(),stderr=process.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        int code;
        try{await process.WaitForExitAsync(timeout.Token);code=process.ExitCode;}
        catch(OperationCanceledException)
        {
            try{process.Kill();}catch(Exception){/* tool may have exited first */}
            if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);
            throw new IOException("A ferramenta local de descriptografia excedeu 10 minutos. Processo encerrado.");
        }
        string errors=await stderr,text=await stdout;
        if(code!=0||!FileExistsQuiet(output))
            throw new IOException("A ferramenta local de descriptografia falhou (código "+code+") e o EBOOT.BIN original permanece intacto: "+string.Join(" ",errors,text).Trim());
        try{ValidateDecryptedEboot(output,binBytes);}
        catch(IOException){throw;}
        return output;
    }

    // Post-decryption gate, separable for tests: real non-empty output, strict
    // ELF identity (PPC64 big-endian header + pinned SHA-256) and recorded
    // evidence (size + SHA-256 + verdict). A produced file is never approved
    // merely because it exists; the EBOOT.BIN source is never overwritten.
    internal static void ValidateDecryptedEboot(string outputPath,long sourceBinBytes)
    {
        OperationTrace.Observe("eboot_bin_source_bytes_"+sourceBinBytes);
        var info=new FileInfo(outputPath);
        if(!info.Exists||info.Length==0)
            throw new IOException($"A ferramenta local não produziu o ELF em {outputPath} (arquivo ausente ou vazio). O EBOOT.BIN original não foi alterado.");
        string sha;
        try{sha=Sha256Hex(outputPath).ToLowerInvariant();}catch(Exception){sha="indisponível";}
        OperationTrace.Observe("eboot_elf_produced_bytes_"+info.Length);
        OperationTrace.Observe("eboot_elf_sha256_"+sha);
        try
        {
            Installation.CheckElf(outputPath);
            OperationTrace.Observe("eboot_elf_validated_by_identity");
        }
        catch(Exception e)
        {
            OperationTrace.Observe("eboot_elf_rejected_by_identity");
            throw new IOException($"A ferramenta local concluiu, mas o resultado não tem a identidade exata aprovada pelo projeto (esperado {RuntimeService.ApprovedElfSha256[..16]}…, obtido {sha[..Math.Min(16,sha.Length)]}, {info.Length:N0} bytes). O arquivo foi mantido em {outputPath} para revisão manual e o EBOOT.BIN original permanece intacto. Detalhe: {e.Message}");
        }
    }

    static string GetFullPath(string path)
    {
        try{return Path.GetFullPath(path);}catch(Exception){return path;}
    }
    static string Sha256Hex(string path)
    {
        using var stream=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    static string? FindRepositoryRoot()
    {
        for(string? directory=AppContext.BaseDirectory;directory!=null;directory=Directory.GetParent(directory)?.FullName)
            if(File.Exists(Path.Combine(directory,"README.md"))&&Directory.Exists(Path.Combine(directory,"port")))return directory;
        return null;
    }
    static bool ToolOnPath(string name)
    {
        string pathVariable=Environment.GetEnvironmentVariable("PATH")??"";
        return pathVariable.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Any(d=>FileExistsQuiet(Path.Combine(d,name)));
    }
    static bool FileExistsQuiet(string path)
    {
        try{return File.Exists(path);}catch(Exception){return false;}
    }
}
