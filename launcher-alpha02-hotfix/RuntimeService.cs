using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DemonsSouls.Launcher;

public sealed class Preferences
{
    public string Installation { get; set; } = Path.Combine(UserDataDirectory,"GameData");
    public bool UseExistingInstallation { get; set; }
    public string RuntimeProfile { get; set; } = Path.Combine(UserDataDirectory,"Alpha02-experimental-profile");
    public string RuntimeExecutable { get; set; } = "";
    public string EbootPath { get; set; } = "";
    public string MonitorDevice { get; set; } = "";
    public bool KeyboardMouse { get; set; } = true;
    public bool Vsync { get; set; } = true;
    public bool Mute { get; set; }
    public string WindowResolution { get; set; } = "1280x720";
    public string DisplayMode { get; set; } = "windowed";
    public int FrameRate { get; set; } = 60;
    public static string UserDataDirectory => Path.GetFullPath(Environment.GetEnvironmentVariable("DEMONS_SOULS_LAUNCHER_HOME") is { Length: > 0 } path
        ? path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DemonsSoulsPCLauncher"));
    public static string FilePath => Path.Combine(UserDataDirectory,"settings.json");
    public static Preferences Read() => Read(FilePath);
    public static Preferences Read(string path)
    {
        var preferences=File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new() : new();
        if(preferences.FrameRate != 60)
            throw new InvalidDataException("Esta build Alpha 0.2 oferece somente o perfil aprovado de 60 FPS.");
        RuntimeService.ValidateProfileRoot(preferences.Installation,preferences.RuntimeProfile);
        return preferences;
    }
    public void Write() => Write(FilePath);
    public void Write(string path)
    {
        if(FrameRate != 60)throw new InvalidDataException("Esta build Alpha 0.2 oferece somente o perfil aprovado de 60 FPS.");
        RuntimeService.ValidateProfileRoot(Installation,RuntimeProfile);
        global::DemonsSouls.Launcher.Installation.Save(path,this);
    }
}

public static class RuntimeService
{
    // Approved Alpha 0.2 runtime identities. The first pins the original
    // released build; the second pins the audio-notify startup fix build
    // (same ELF, rebuilt runtime validated by automated WASAPI A/B: 100%
    // of notify events delivered and sustained 0.0% silence with the fix
    // enabled, versus 100% silence with it disabled). Human listening
    // validation is still pending; see docs/34-audio-notify-startup-race.md.
    public const string Approved60FpsRuntimeSha256="88087725d7cb858b644d8d183329eb72e1c820b365ed2b34ebd6e2ac281f0933";
    public const string Approved60FpsRuntimeSha256AudioFix="83b23813254c542575244f394ed31a3be2bbd276e6a1d70372d2c6c12a77ae7e";
    public const string ApprovedElfSha256="90eaa5e9e48b4874292badc311ccea253e6a622ff188bfbdbe76e6f6821269d8";

    public static string ExecutablePath(string root,string? runtimeExecutable=null) =>
        runtimeExecutable is null
            ? Installation.Child(root,"runtime/des_boot.exe")
            : Installation.Full(runtimeExecutable);

    public static string SelectedExecutablePath(string root,bool useExistingInstallation,string? runtimeExecutable) =>
        !string.IsNullOrWhiteSpace(runtimeExecutable)
            ?Installation.Full(runtimeExecutable)
            :Installation.Child(root,"runtime/des_boot.exe");

    public static string SelectedEbootPath(string root,bool useExistingInstallation,string? ebootPath) =>
        !string.IsNullOrWhiteSpace(ebootPath)
            ?Installation.Full(ebootPath)
            :Installation.Child(root,"runtime/EBOOT.ELF");

    public static async Task<bool> IsApproved60FpsRuntimeAsync(string executable,CancellationToken ct=default)
    {
        if(!File.Exists(executable))return false;
        string hash=await Installation.Hash(executable,ct);
        return IsApproved60FpsRuntimeHash(hash);
    }

    public static async Task<string?> Get60FpsCompatibilityBlockerAsync(string installationRoot,string profileRoot,CancellationToken ct=default,bool useExistingInstallation=false,string? runtimeExecutable=null,string? ebootPath=null)
    {
        using var ownTrace=OperationTrace.IsActive?null:OperationTrace.Start("Confirmação 60 FPS antes de salvar");
        using var activation=ownTrace?.Activate();
        using var validationPhase=OperationTrace.Measure("Validação de compatibilidade do runtime e EBOOT",2,0);
        string? Block(string message)
        {
            validationPhase?.Complete();
            if(ownTrace is not null)OperationTrace.RecordDecision("compatibility_blocker",message);
            ownTrace?.Complete();
            return message;
        }
        if(string.IsNullOrWhiteSpace(installationRoot))
            return Block("selecione uma instalação preparada na aba Instalação.");
        if(!File.Exists(Path.Combine(installationRoot,"installation.json")))
            return Block("prepare ou selecione uma instalação experimental completa na aba Instalação.");
        try
        {
            installationRoot=Installation.Full(installationRoot);
            profileRoot=Installation.Full(profileRoot);
            var manifest=useExistingInstallation
                ?Installation.ValidateExistingSelection(installationRoot)
                :Installation.Load(installationRoot);
            if(!manifest.Complete||manifest.Synthetic)
                return Block("a instalação está incompleta ou é apenas fixture; conclua PREPARAR / REPARAR com arquivos locais legítimos.");
            if(useExistingInstallation)Installation.ValidateExistingStructureReadOnly(installationRoot);
            else Installation.GameRoot(Installation.Child(installationRoot,"vfs/PS3_GAME"));
            var runtimeEntry=manifest.Files.SingleOrDefault(entry=>entry.Path.Equals("runtime/des_boot.exe",StringComparison.OrdinalIgnoreCase));
            var elfEntry=manifest.Files.SingleOrDefault(entry=>entry.Path.Equals("runtime/EBOOT.ELF",StringComparison.OrdinalIgnoreCase));
            if(!useExistingInstallation&&(runtimeEntry is null||elfEntry is null))
                return "o manifesto não contém runtime/des_boot.exe e runtime/EBOOT.ELF; repare a instalação aprovada.";
            bool explicitRuntime=!string.IsNullOrWhiteSpace(runtimeExecutable);
            string executable=SelectedExecutablePath(installationRoot,useExistingInstallation,runtimeExecutable);
            Installation.NoLinks(executable);
            if(!File.Exists(executable))
                return Block("des_boot.exe está ausente; selecione o executável aprovado em Avançado.");
            string runtimeHash=await Installation.Hash(executable,ct);
            if(!IsApproved60FpsRuntimeHash(runtimeHash)||(!explicitRuntime&&!useExistingInstallation&&!string.Equals(runtimeEntry!.Sha256,runtimeHash,StringComparison.OrdinalIgnoreCase)))
                return Block("o SHA-256 do runtime selecionado não corresponde à build Alpha 0.2 aprovada.");
            bool explicitElf=!string.IsNullOrWhiteSpace(ebootPath);
            string elf=SelectedEbootPath(installationRoot,useExistingInstallation,ebootPath);
            Installation.NoLinks(elf);
            if(!File.Exists(elf))return Block("EBOOT.ELF está ausente; selecione o arquivo preparado em Avançado.");
            Installation.CheckElf(elf,verifyHash:false);
            string elfHash=await Installation.Hash(elf,ct);
            if(!string.Equals(elfHash,ApprovedElfSha256,StringComparison.OrdinalIgnoreCase)||
               (!explicitElf&&!useExistingInstallation&&!string.Equals(elfEntry!.Sha256,elfHash,StringComparison.OrdinalIgnoreCase)))
                return Block("o SHA-256 do EBOOT.ELF não corresponde à build Alpha 0.2 aprovada; repare a instalação.");
            ValidateProfileRoot(installationRoot,profileRoot);
            if(Directory.Exists(profileRoot))Installation.NoLinks(profileRoot);
            validationPhase?.Complete();
            ownTrace?.Complete();
            return null;
        }
        catch(OperationCanceledException e){ownTrace?.Fail(e);throw;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ownTrace?.Fail(e);
            return e.GetBaseException().Message;
        }
    }

    public sealed class RuntimeHandoffManifest
    {
        [JsonPropertyName("projeto")]
        public string Project { get; set; }="";
        [JsonPropertyName("versao")]
        public string Version { get; set; }="";
        [JsonPropertyName("executavelLocal")]
        public string ExecutablePath { get; set; }="";
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }="";
        [JsonPropertyName("fpsDisponiveis")]
        public int[] AvailableFrameRates { get; set; }=[];
        [JsonPropertyName("variavelFPS")]
        public string FrameRateVariable { get; set; }="";
        [JsonPropertyName("testeHumano60FPS")]
        public string Human60FpsTest { get; set; }="";
        [JsonPropertyName("integracaoLauncher")]
        public string LauncherIntegration { get; set; }="";
        [JsonPropertyName("perfilCompletoDeInicializacao")]
        public string CompleteLaunchProfile { get; set; }="";
        [JsonPropertyName("distribuicaoPublica")]
        public string PublicDistribution { get; set; }="";

        public static RuntimeHandoffManifest Load(string path)
        {
            if(!File.Exists(path))throw new FileNotFoundException("Manifesto local de integração não encontrado.",path);
            if(new FileInfo(path).Length>64*1024)throw new InvalidDataException("Manifesto local de integração excede 64 KiB.");
            var manifest=JsonSerializer.Deserialize<RuntimeHandoffManifest>(File.ReadAllText(path))
                ??throw new InvalidDataException("Manifesto local de integração vazio ou inválido.");
            manifest.Validate();
            return manifest;
        }

        public void Validate()
        {
            if(Project!="Demons Souls PC"||Version!="Alpha 0.2")
                throw new InvalidDataException("Manifesto de integração não identifica Demon's Souls PC Alpha 0.2.");
            if(string.IsNullOrWhiteSpace(ExecutablePath)||!Path.IsPathFullyQualified(ExecutablePath)||!Path.GetFileName(ExecutablePath).Equals("des_boot.exe",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Manifesto não identifica um caminho local absoluto para des_boot.exe.");
            if(string.IsNullOrWhiteSpace(Sha256)||Sha256.Length!=64||!Sha256.All(Uri.IsHexDigit)||!RuntimeService.IsApproved60FpsRuntimeHash(Sha256))
                throw new InvalidDataException("SHA-256 do manifesto não corresponde à identidade aprovada do runtime Alpha 0.2.");
            if(AvailableFrameRates is null||!AvailableFrameRates.SequenceEqual(new[]{60})||FrameRateVariable!="DES_FPS")
                throw new InvalidDataException("Manifesto declara taxas ou variável de FPS incompatíveis.");
            if(string.IsNullOrWhiteSpace(Human60FpsTest)||!Human60FpsTest.Contains("Aprovado",StringComparison.OrdinalIgnoreCase)||
               string.IsNullOrWhiteSpace(LauncherIntegration)||!LauncherIntegration.Contains("Pendente",StringComparison.OrdinalIgnoreCase)||
               string.IsNullOrWhiteSpace(CompleteLaunchProfile)||!CompleteLaunchProfile.Contains("Pendente",StringComparison.OrdinalIgnoreCase)||
               string.IsNullOrWhiteSpace(PublicDistribution)||!Normalize(PublicDistribution).Contains("Nao autorizada",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Estado de aprovação/perfil do manifesto não corresponde a uma integração ainda pendente.");
        }

        static string Normalize(string value) => string.Concat(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(character=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)!=System.Globalization.UnicodeCategory.NonSpacingMark));
    }

    public static bool IsApproved60FpsRuntimeHash(string hash) =>
        string.Equals(hash,Approved60FpsRuntimeSha256,StringComparison.OrdinalIgnoreCase) ||
        string.Equals(hash,Approved60FpsRuntimeSha256AudioFix,StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string,string> FrameRateSettings(Preferences preferences)
    {
        if(preferences.FrameRate != 60)
            throw new InvalidDataException("Esta build Alpha 0.2 oferece somente o perfil aprovado de 60 FPS.");
        return new(){["DES_FPS"]=preferences.FrameRate.ToString(CultureInfo.InvariantCulture)};
    }

    internal static ProcessStartInfo CreateProcessStartInfo(string executable,string workingDirectory,IEnumerable<string> arguments,IReadOnlyDictionary<string,string> settings,bool redirectOutput=false)
    {
        var start=new ProcessStartInfo(executable){WorkingDirectory=workingDirectory,UseShellExecute=false};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        foreach(string key in start.Environment.Keys.ToArray())
            if(Regex.IsMatch(key,@"^(PAD_|DES_|PS3_|RSX_|SPU_|SPURS_|PPU_|CELLMARK_|CELLGCM_|AUDIO_|FS_|LIBSRE_|GCM|VBLANK|FRAME_LOG$|PERF$|RF_SLOW_MS$|FIFO_KICK_MS$|HLE_|SEM_|LBP_|CONST|TEX|FIFO_|PROF_|YZ_|FP_|VP_|RTT_|EXIT_)",RegexOptions.IgnoreCase))
                start.Environment.Remove(key);
        foreach(var (key,value) in settings)start.Environment[key]=value;
        start.RedirectStandardOutput=redirectOutput;
        start.RedirectStandardError=redirectOutput;
        return start;
    }

    public static Dictionary<string,string> VideoSettings(Preferences preferences)
    {
        var (width,height)=preferences.WindowResolution switch
        {
            "1600x900"=>(1600,900),
            "1920x1080"=>(1920,1080),
            _=>(1280,720),
        };
        var settings=new Dictionary<string,string>
        {
            ["RSX_WINDOW_W"]=width.ToString(),
            ["RSX_WINDOW_H"]=height.ToString(),
            ["RSX_BORDERLESS"]=preferences.DisplayMode=="windowed"?"0":"1",
            ["RSX_DISPLAY_MODE"]=preferences.DisplayMode switch
            {
                "borderless"=>"borderless",
                "fullscreen"=>"fullscreen",
                _=>"windowed",
            },
        };
        if(!string.IsNullOrWhiteSpace(preferences.MonitorDevice))
            settings["RSX_MONITOR_DEVICE"]=preferences.MonitorDevice;
        return settings;
    }

    public static Dictionary<string,string> LaunchSettings(string installationRoot,string profileRoot,string sessionRoot,Preferences preferences,bool diagnostic=false,string? vfsRootOverride=null)
    {
        installationRoot=Installation.Full(installationRoot);profileRoot=Installation.Full(profileRoot);sessionRoot=Installation.Full(sessionRoot);
        ValidateProfileRoot(installationRoot,profileRoot);
        if(!Installation.Under(sessionRoot,profileRoot))
            throw new IOException("A sessão do runtime deve permanecer dentro do perfil configurado.");
        string vfsRoot=string.IsNullOrWhiteSpace(vfsRootOverride)?Installation.Child(installationRoot,"vfs"):Installation.Full(vfsRootOverride);
        if(!string.IsNullOrWhiteSpace(vfsRootOverride)&&!Installation.Under(vfsRoot,profileRoot))
            throw new IOException("A VFS redirecionada deve permanecer dentro do perfil experimental.");
        Installation.NoLinks(vfsRoot);
        _=FrameRateSettings(preferences);
        var settings=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS3_VFS_ROOT"]=vfsRoot,
            ["PS3_USRDIR_BASE"]=Installation.Child(vfsRoot,"PS3_GAME"),
            ["PS3_HDD0_ROOT"]=Installation.Child(profileRoot,"gamedata/dev_hdd0"),
            ["PS3_DEV_FLASH"]=Installation.Child(profileRoot,"dev_flash"),
            ["PS3_SHADER_CACHE"]=Installation.Child(profileRoot,"shader_cache"),
            ["DES_DATA_DIR"]=Installation.Child(profileRoot,"interface"),
            ["DES_SETTINGS"]=Installation.Child(profileRoot,"config/settings.ini"),
            ["DES_KIND"]="play",
            ["DES_UI"]=diagnostic?"0":"1",
            ["DES_PRECOMPILE"]=diagnostic?"0":"1",
            ["DES_FPS"]=preferences.FrameRate.ToString(CultureInfo.InvariantCulture),
            ["VBLANK_MHZ"]="59940",
            ["DES_VSYNC"]=preferences.Vsync?"1":"0",
            ["DES_AUTO_CONTINUE"]="0",
            ["DES_SKIP_INTRO"]="1",
            ["PS3_SAIL_MOVIES"]="0",
            ["AUDIO_MUTE"]=preferences.Mute?"1":"0",
            ["PS3_VERBOSE"]="0",
            ["DES_REPORT_NO_OPEN"]="1",
            ["PS3_PIDFILE"]=Installation.Child(sessionRoot,"runtime-pid.txt"),
            ["DES_GAME_LOG"]=Installation.Child(sessionRoot,"game.log"),
            ["DES_RUNTIME_LOG"]=Installation.Child(sessionRoot,"runtime.log"),
            ["DES_FRAMELOG"]=Installation.Child(sessionRoot,"framelog.txt"),
            ["FRAME_LOG"]=Installation.Child(sessionRoot,"presents.txt"),
            ["RSX_WINDOW_DIAG"]=Installation.Child(sessionRoot,"window-state.jsonl"),
            ["RSX_HOTKEY_DIR"]=Installation.Child(sessionRoot,"capturas"),
            ["CELLMARK_DUMP_DIR"]=Installation.Child(sessionRoot,"capturas"),
            ["PROF_DIR"]=Installation.Child(sessionRoot,"perfil"),
            ["APPDATA"]=Installation.Child(profileRoot,"appdata/roaming"),
            ["LOCALAPPDATA"]=Installation.Child(profileRoot,"appdata/local"),
            ["TEMP"]=Installation.Child(profileRoot,"temp"),
            ["TMP"]=Installation.Child(profileRoot,"temp"),
        };
        foreach(var (key,value) in VideoSettings(preferences))settings[key]=value;
        if(preferences.KeyboardMouse)
        {
            settings["PAD_PC_INPUT"]="1";
            settings["PAD_MOUSE_SENS"]="6";
            settings["PAD_MOUSE_SENS_X"]="6";
            settings["PAD_MOUSE_SENS_Y"]="6";
        }
        else settings["PAD_NO_KEYBOARD"]="1";
        if(!diagnostic)settings["DES_SKIP_UI"]="1";
        if(diagnostic)
        {
            settings["PAD_PC_INPUT"]="0";
            settings["PAD_FILE"]=Installation.Child(sessionRoot,"pad.txt");
            settings["DES_AUTO_CONTINUE"]="0";
            settings["PS3_DEBUG"]=Installation.Child(sessionRoot,"console.txt");
        }
        return settings;
    }

    public static void ValidateProfileRoot(string installationRoot,string profileRoot)
    {
        if(string.IsNullOrWhiteSpace(installationRoot)||string.IsNullOrWhiteSpace(profileRoot))
            throw new IOException("Selecione uma instalação e um perfil experimental válidos.");
        installationRoot=Installation.Full(installationRoot);profileRoot=Installation.Full(profileRoot);
        if(profileRoot.Equals(installationRoot,StringComparison.OrdinalIgnoreCase)||
           Installation.Under(profileRoot,installationRoot)||Installation.Under(installationRoot,profileRoot))
            throw new IOException("O perfil experimental deve ser separado da instalação para preservar os dados estáveis.");
    }

    public static void PrepareRuntimeSettings(string path,Preferences preferences)
    {
        _=FrameRateSettings(preferences);
        Installation.NoLinks(path);
        string[] lines=File.Exists(path)?File.ReadAllLines(path):[];
        Set("DES_FPS",preferences.FrameRate.ToString(CultureInfo.InvariantCulture));
        Set("DES_VSYNC",preferences.Vsync?"1":"0");
        SetDefault("show_start_screen","1");
        string temp=path+".new";Installation.NoLinks(temp);
        File.WriteAllLines(temp,lines,new System.Text.UTF8Encoding(false));
        File.Move(temp,path,true);

        void Set(string key,string value)
        {
            var matcher=new Regex(@"^\s*"+Regex.Escape(key)+@"\s*=.*$",RegexOptions.IgnoreCase);
            int first=Array.FindIndex(lines,matcher.IsMatch);
            if(first<0)lines=lines.Append(key+" = "+value).ToArray();
            else
            {
                lines[first]=key+" = "+value;
                lines=lines.Where((line,index)=>index==first||!matcher.IsMatch(line)).ToArray();
            }
        }
        void SetDefault(string key,string value)
        {
            if(!lines.Any(line=>Regex.IsMatch(line,@"^\s*"+Regex.Escape(key)+@"\s*=",RegexOptions.IgnoreCase)))
                lines=lines.Append(key+" = "+value).ToArray();
        }
    }

    internal static void EnsureNoRuntimeAlreadyRunning()
    {
        var running=Process.GetProcessesByName("des_boot");
        try
        {
            if(running.Length>0)
                throw new IOException("Feche qualquer sessão existente do runtime des_boot.exe antes de iniciar outra. PID(s): "+string.Join(", ",running.Select(process=>process.Id)));
        }
        finally{foreach(var process in running)process.Dispose();}
    }

    public static Process Start(string root, Preferences preferences, LaunchVerification verification, bool diagnostic=false, string? developmentSave=null, bool isolated=false, string? runtimeExecutable=null,string? runtimeProfile=null,bool useExistingInstallation=false,string? ebootPath=null,bool existingVfsPreparedAndVerified=false,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        root=Installation.Full(root); var m=Installation.Load(root);
        using var lease=Installation.TakeLease(root);
        if(!m.Complete||m.Synthetic) throw new IOException("Instalação incompleta ou sintética; inicialização bloqueada.");
        if(verification.ReadOnlyExisting!=useExistingInstallation)
            throw new IOException("O tipo de validação da instalação mudou antes do início. Verifique novamente.");
        if(useExistingInstallation)Installation.ValidateExistingStructureReadOnly(root);
        else Installation.GameRoot(Installation.Child(root,"vfs/PS3_GAME"));
        using var runtimeStartLease=Installation.TakeLease(Path.Combine(Path.GetTempPath(),"DemonsSoulsLauncher-global-runtime"));
        EnsureNoRuntimeAlreadyRunning();
        string profile=Installation.Full(string.IsNullOrWhiteSpace(runtimeProfile)?preferences.RuntimeProfile:runtimeProfile);
        ValidateProfileRoot(root,profile);
        Installation.NoLinks(profile);
        using var profileLease=Installation.TakeLease(profile);
        if(useExistingInstallation)
        {
            using var profileValidation=OperationTrace.Measure("Revalidação VFS imediatamente antes do processo");
            if(existingVfsPreparedAndVerified)Installation.ValidatePreparedExistingRuntimeVfsMarker(root,profile);
            else Installation.VerifyPreparedExistingRuntimeVfs(root,profile);
            profileValidation?.Complete();
        }
        else if(existingVfsPreparedAndVerified)
            throw new IOException("A VFS somente leitura foi marcada como preparada para uma instalação gerenciada.");
        string run = diagnostic || isolated ? Installation.Child(profile,"tests/run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")) : profile;
        Directory.CreateDirectory(run); Installation.NoLinks(run);
        if(developmentSave!=null)
        {
            if(!diagnostic) throw new IOException("Save de desenvolvimento exige sessão isolada.");
            foreach(var f in Installation.Inventory(developmentSave))
            {
                string target=Installation.Child(run,Path.Combine("gamedata",f.Relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(f.Source,target,false);
            }
        }
        string exe,elf,executableHash,elfHash;
        using(var runtimeValidation=OperationTrace.Measure("Validação SHA-256 do runtime e EBOOT",2,0))
        {
            exe=SelectedExecutablePath(root,useExistingInstallation,runtimeExecutable);
            if(!File.Exists(exe)) throw new IOException("Executável nativo não encontrado: "+exe);
            Installation.NoLinks(exe);
            executableHash=Installation.Hash(exe,cancellationToken).GetAwaiter().GetResult();
            if(preferences.FrameRate==60&&!IsApproved60FpsRuntimeHash(executableHash))
                throw new IOException("60 FPS bloqueado: o executável nativo não corresponde ao SHA-256 da build aprovada.");
            elf=SelectedEbootPath(root,useExistingInstallation,ebootPath);
            Installation.NoLinks(elf);
            Installation.CheckElf(elf,verifyHash:false);
            elfHash=Installation.Hash(elf,cancellationToken).GetAwaiter().GetResult();
            if(!string.Equals(elfHash,ApprovedElfSha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("O EBOOT.ELF não corresponde ao SHA-256 da build Alpha 0.2 aprovada.");
            runtimeValidation?.Complete();
        }
        string stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..8];
        string activeProfile=diagnostic||isolated?run:profile;
        string logs=Installation.Child(run,"logs");Directory.CreateDirectory(logs);
        string session=Installation.Child(logs,stamp);
        foreach(string directory in new[]{"capturas","perfil"})Directory.CreateDirectory(Installation.Child(session,directory));
        foreach(string directory in new[]{"gamedata/dev_hdd0","dev_flash","shader_cache","interface","config","appdata/roaming","appdata/local","temp"})Directory.CreateDirectory(Installation.Child(run,directory));
        if(diagnostic)
        {
            string padFile=Installation.Child(session,"pad.txt");File.WriteAllText(padFile,"");
        }
        string settingsPath=Installation.Child(run,"config/settings.ini");
        PrepareRuntimeSettings(settingsPath,preferences);
        var settings=LaunchSettings(root,activeProfile,session,preferences,diagnostic,useExistingInstallation?Installation.Child(profile,"vfs"):null);
        Installation.Save(Path.Combine(session,"launch.json"),new { exe,elf,cwd=run,profile=activeProfile,session,settings,verification,executableSha256=executableHash,elfSha256=elfHash,version="0.2.0-alpha.2",at=DateTimeOffset.Now });
        // A tiny cmd wrapper is unnecessary: redirected streams are drained by
        // this process during diagnostic runs only. Normal gameplay inherits
        // no pipe, so the launcher can exit without blocking the game.
        var start=CreateProcessStartInfo(exe,run,new[]{elf},settings,diagnostic);
        cancellationToken.ThrowIfCancellationRequested();
        Process process;
        using(var processStart=OperationTrace.Measure("Inicialização do processo nativo",1,0))
        {
            process=diagnostic ? Process.Start(start) ?? throw new IOException("O Windows não iniciou o executável.") : NativeStart.Start(start,session,stamp);
            if(cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if(!process.HasExited){process.Kill(entireProcessTree:true);process.WaitForExit();}
                }
                catch(Exception e)
                {
                    process.Dispose();
                    throw new IOException("O cancelamento foi solicitado após iniciar o runtime, mas o processo não pôde ser encerrado com segurança.",e);
                }
                process.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
            processStart?.Complete();
        }
        Installation.Save(Path.Combine(session,"launch.json"),new { pid=process.Id,exe,elf,cwd=run,profile=activeProfile,session,settings,verification,executableSha256=executableHash,elfSha256=elfHash,version="0.2.0-alpha.2",at=DateTimeOffset.Now });
        if(diagnostic)
        {
            var stdout=new StreamWriter(Path.Combine(session,"stdout.log")){AutoFlush=true};
            var stderr=new StreamWriter(Path.Combine(session,"stderr.log")){AutoFlush=true};
            process.OutputDataReceived+=(_,e)=>{if(e.Data!=null) stdout.WriteLine(e.Data);};
            process.ErrorDataReceived+=(_,e)=>{if(e.Data!=null) stderr.WriteLine(e.Data);};
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            process.EnableRaisingEvents=true; process.Exited+=(_,_)=>{ /* Streams remain owned until diagnostic controller exits. */ };
            Console.WriteLine(JsonSerializer.Serialize(new {pid=process.Id,run,exe,elf}));
        }
        return process;
    }
    public static void OpenFolder(string path)
    {
        Installation.NoLinks(path); Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={path},UseShellExecute=false});
    }
}
