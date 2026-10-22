using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DemonsSouls.Launcher;

public sealed class Preferences
{
    public string Installation { get; set; } = @"F:\DemonSouls-PC\GameData";
    public string RuntimeExecutable { get; set; } = "";
    public string MonitorDevice { get; set; } = "";
    public bool KeyboardMouse { get; set; } = true;
    public bool Vsync { get; set; } = true;
    public bool Mute { get; set; }
    public string WindowResolution { get; set; } = "1280x720";
    public string DisplayMode { get; set; } = "windowed";
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DemonsSoulsPCLauncher","settings.json");
    public static Preferences Read() => Read(FilePath);
    public static Preferences Read(string path) => File.Exists(path) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new() : new();
    public void Write() => Write(FilePath);
    public void Write(string path) => global::DemonsSouls.Launcher.Installation.Save(path,this);
}

public static class RuntimeService
{
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

    public static Process Start(string root, Preferences preferences, LaunchVerification verification, bool diagnostic=false, string? developmentSave=null, bool isolated=false, string? runtimeExecutable=null)
    {
        root=Installation.Full(root); var m=Installation.Load(root);
        using var lease=Installation.TakeLease(root);
        if(!m.Complete||m.Synthetic) throw new IOException("Instalação incompleta ou sintética; inicialização bloqueada.");
        Installation.GameRoot(Installation.Child(root,"vfs/PS3_GAME"));
        string run = diagnostic || isolated ? Installation.Child(root,"tests/run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")) : Installation.Child(root,"userdata");
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
        string hdd=Installation.Child(run,"gamedata/dev_hdd0"), cache=Installation.Child(run,"cache/graphics");
        Directory.CreateDirectory(hdd); Directory.CreateDirectory(cache);
        string exe=runtimeExecutable is null
            ? Installation.Child(root,"runtime/des_boot.exe")
            : Installation.Full(runtimeExecutable);
        if(!File.Exists(exe)) throw new IOException("Executável nativo não encontrado: "+exe);
        string elf=Installation.Child(root,"runtime/EBOOT.ELF");
        Installation.CheckElf(elf,verifyHash:false);
        var start=new ProcessStartInfo(exe) { WorkingDirectory=run,UseShellExecute=false };
        start.ArgumentList.Add(elf);
        foreach(string key in start.Environment.Keys.ToArray())
            if(Regex.IsMatch(key,@"^(PAD_|DES_|PS3_|RSX_|SPU_|SPURS_|PPU_|CELLMARK_|CELLGCM_|AUDIO_|FS_|LIBSRE_|GCM|VBLANK|HLE_|SEM_|LBP_|CONST|TEX|FIFO_|PROF_|YZ_|FP_|VP_|RTT_)",RegexOptions.IgnoreCase)) start.Environment.Remove(key);
        var settings=new Dictionary<string,string>
        {
            ["PS3_VFS_ROOT"]=Installation.Child(root,"vfs"),
            ["PS3_USRDIR_BASE"]=Installation.Child(root,"vfs/PS3_GAME"),
            ["PS3_HDD0_ROOT"]=hdd,["PS3_SHADER_CACHE"]=cache,
            ["PS3_DEV_FLASH"]=Installation.Child(run,"dev_flash"),
            ["DES_UI"]=diagnostic?"0":"1",["DES_FPS"]="30",["VBLANK_MHZ"]="59940",
            ["DES_PRECOMPILE"]=diagnostic?"0":"1",["PS3_VERBOSE"]="0",
            ["DES_VSYNC"]=preferences.Vsync?"1":"0",["AUDIO_MUTE"]=preferences.Mute?"1":"0",
        };
        foreach(var (key,value) in VideoSettings(preferences)) settings[key]=value;
        if(preferences.KeyboardMouse) settings["PAD_PC_INPUT"]="1"; else settings["PAD_NO_KEYBOARD"]="1";
        if(!diagnostic) settings["DES_SKIP_UI"]="1";
        if(diagnostic)
        {
            settings["PAD_PC_INPUT"]="0"; settings["PAD_FILE"]=Path.Combine(run,"pad.txt"); File.WriteAllText(settings["PAD_FILE"],"");
            settings["DES_AUTO_CONTINUE"]=developmentSave!=null?"1":"0"; settings["DES_SKIP_INTRO"]="1";
            settings["PS3_SAIL_MOVIES"]="0"; settings["PS3_DEBUG"]=Path.Combine(run,"console.txt");
            settings["CELLMARK_DUMP_DIR"]=run; settings["DES_FRAMELOG"]=Path.Combine(run,"framelog.txt");
        }
        string logs=Installation.Child(run,"logs"); Directory.CreateDirectory(logs);
        string stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        settings["DES_GAME_LOG"]=Path.Combine(logs,"game-"+stamp+".log");
        settings["DES_RUNTIME_LOG"]=Path.Combine(logs,"runtime-"+stamp+".log");
        settings["RSX_WINDOW_DIAG"]=Path.Combine(logs,"window-state.jsonl");
        foreach(var (key,value) in settings) start.Environment[key]=value;
        Installation.Save(Path.Combine(logs,"launch-"+stamp+".json"),new { exe,elf,cwd=run,settings,verification,version="V14 launcher hotfix v2",at=DateTimeOffset.Now });
        // A tiny cmd wrapper is unnecessary: redirected streams are drained by
        // this process during diagnostic runs only. Normal gameplay inherits
        // no pipe, so the launcher can exit without blocking the game.
        start.RedirectStandardOutput=diagnostic; start.RedirectStandardError=diagnostic;
        var process=diagnostic ? Process.Start(start) ?? throw new IOException("O Windows não iniciou o executável.") : NativeStart.Start(start,logs,stamp);
        if(diagnostic)
        {
            var stdout=new StreamWriter(Path.Combine(run,"stdout.log")){AutoFlush=true};
            var stderr=new StreamWriter(Path.Combine(run,"stderr.log")){AutoFlush=true};
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
