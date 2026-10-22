using System.Text.Json;
using System.Windows;

namespace DemonsSouls.Launcher;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==0){try{new Application().Run(new MainWindow());return 0;}catch(Exception e){MessageBox.Show(e.Message,"Não foi possível abrir o launcher",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}}
        try{return Command(args).GetAwaiter().GetResult();}
        catch(Exception e){Console.Error.WriteLine(e.ToString());return 1;}
    }
    static async Task<int> Command(string[] args)
    {
        string Get(string key,string? fallback=null){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback??throw new ArgumentException("Argumento necessário: "+key);}
        using var cancellation=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
        DateTime last=DateTime.MinValue;
        var progress=new DirectProgress(p=>{if((DateTime.UtcNow-last).TotalSeconds<1&&p.Done!=p.Total)return;last=DateTime.UtcNow;Console.WriteLine(JsonSerializer.Serialize(p));});
        switch(args[0])
        {
            case "--inspect":
                string source=Installation.GameRoot(Get("--source"));var files=Installation.Inventory(source);
                Console.WriteLine(JsonSerializer.Serialize(new{source,files=files.Count,bytes=files.Sum(e=>e.Length),sfo=Installation.Sfo(Path.Combine(source,"PARAM.SFO"))},Installation.Json));break;
            case "--import":
                await Installation.Import(Get("--source"),Get("--destination"),Get("--elf"),Get("--runtime"),progress,cancellation.Token);break;
            case "--verify":await Installation.Verify(Get("--destination"),progress,cancellation.Token);break;
            case "--self-test":await SelfTest.Run();break;
            case "--fps-compatibility":
            {
                string installation=Get("--installation"),profile=Get("--profile"),runtime=Get("--runtime"),elf=Get("--elf");
                bool useExisting=args.Contains("--existing",StringComparer.OrdinalIgnoreCase);
                string? blocker=await RuntimeService.Get60FpsCompatibilityBlockerAsync(installation,profile,cancellation.Token,
                    useExistingInstallation:useExisting,runtimeExecutable:runtime,ebootPath:elf);
                Console.WriteLine(JsonSerializer.Serialize(new{approved=blocker is null,frameRates=blocker is null?new[]{60}:Array.Empty<int>(),runtime,elf,blocker}));
                if(blocker is not null)return 2;
                break;
            }
            case "--manual-test":
                string testRoot=Get("--destination");
                var manualVerification=await Installation.VerifyForLaunch(testRoot,progress,cancellation.Token);
                using(var process=RuntimeService.Start(testRoot,new Preferences(),manualVerification,diagnostic:true))
                {
                    try{await process.WaitForExitAsync(cancellation.Token);}
                    catch(OperationCanceledException)
                    {
                        if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}
                        throw;
                    }
                    Console.WriteLine($"MANUAL_TEST_EXITED exitCode={process.ExitCode}");
                }break;
            case "--play-test":
                string playRoot=Get("--destination");
                var playVerification=await Installation.VerifyForLaunch(playRoot,progress,cancellation.Token);
                var savedPreferences=Preferences.Read();
                savedPreferences.Installation=playRoot;
                string selectedRuntime=Get("--runtime",savedPreferences.RuntimeExecutable);
                using(var process=RuntimeService.Start(playRoot,savedPreferences,playVerification,isolated:true,
                          runtimeExecutable:string.IsNullOrWhiteSpace(selectedRuntime)?null:selectedRuntime))
                {
                    Console.WriteLine(JsonSerializer.Serialize(new {pid=process.Id,run=Directory.GetDirectories(Path.Combine(playRoot,"tests"),"run-*").Order().Last(),exe=string.IsNullOrWhiteSpace(selectedRuntime)?Path.Combine(playRoot,"runtime/des_boot.exe"):selectedRuntime,windowSettings=RuntimeService.VideoSettings(savedPreferences),verification=playVerification}));
                }break;
            case "--window-test":
            {
                string windowTestRoot=Get("--destination");
                var windowVerification=await Installation.VerifyForLaunch(windowTestRoot,progress,cancellation.Token);
                var preferences=Preferences.Read();
                preferences.Installation=windowTestRoot;
                string runtime=Get("--runtime",preferences.RuntimeExecutable);
                if(args.Contains("--resolution"))preferences.WindowResolution=Get("--resolution");
                if(args.Contains("--mode"))preferences.DisplayMode=Get("--mode");
                if(args.Contains("--monitor"))preferences.MonitorDevice=Get("--monitor");
                if(args.Contains("--keyboard-mouse"))preferences.KeyboardMouse=Get("--keyboard-mouse")=="1";
                int seconds=int.Parse(Get("--seconds","30"));
                if(seconds<5||seconds>180)throw new ArgumentException("Window test: duração permitida de 5–180 segundos.");
                var processStartTimer=System.Diagnostics.Stopwatch.StartNew();
                using var process=RuntimeService.Start(windowTestRoot,preferences,windowVerification,isolated:true,
                    runtimeExecutable:string.IsNullOrWhiteSpace(runtime)?null:runtime);
                processStartTimer.Stop();
                string run=Directory.GetDirectories(Path.Combine(windowTestRoot,"tests"),"run-*").Order().Last();
                string testLog=Path.Combine(run,"logs","window-test.jsonl");
                await File.AppendAllTextAsync(testLog,JsonSerializer.Serialize(new
                {
                    eventName="started",pid=process.Id,exe=string.IsNullOrWhiteSpace(runtime)?Path.Combine(windowTestRoot,"runtime/des_boot.exe"):runtime,
                    seconds,processStartMilliseconds=processStartTimer.ElapsedMilliseconds,
                    startupSinceVerificationMilliseconds=windowVerification.ElapsedMilliseconds+processStartTimer.ElapsedMilliseconds,
                    settings=RuntimeService.VideoSettings(preferences),verification=windowVerification,at=DateTimeOffset.Now
                })+Environment.NewLine);
                Console.WriteLine(JsonSerializer.Serialize(new {pid=process.Id,run,exe=string.IsNullOrWhiteSpace(runtime)?Path.Combine(windowTestRoot,"runtime/des_boot.exe"):runtime,settings=RuntimeService.VideoSettings(preferences)}));
                DateTime until=DateTime.UtcNow.AddSeconds(seconds),lastSample=DateTime.MinValue;
                bool autoTerminated=false;
                try
                {
                    while(DateTime.UtcNow<until&&!process.HasExited&&!cancellation.IsCancellationRequested)
                    {
                        object? sample=NativeWindowProbe.Read(process.Id,1280,720);
                        if(sample!=null&&(DateTime.UtcNow-lastSample).TotalSeconds>=1)
                        {
                            string line=JsonSerializer.Serialize(new {eventName="probe",at=DateTimeOffset.Now,window=sample});
                            await File.AppendAllTextAsync(testLog,line+Environment.NewLine);
                            Console.WriteLine(line);
                            lastSample=DateTime.UtcNow;
                        }
                        await Task.Delay(250);
                    }
                }
                finally
                {
                    if(!process.HasExited){autoTerminated=true;process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}
                }
                await File.AppendAllTextAsync(testLog,JsonSerializer.Serialize(new {eventName="monitor_finished",exited=process.HasExited,autoTerminated,at=DateTimeOffset.Now})+Environment.NewLine);
                Console.WriteLine($"WINDOW_TEST_LOG {testLog}");
                break;
            }
            case "--smoke":
                string root=Get("--destination");var smokeVerification=await Installation.VerifyForLaunch(root,progress,cancellation.Token);
                string save=Get("--save","");using(var process=RuntimeService.Start(root,new Preferences(),smokeVerification,true,save.Length>0?save:null))
                {
                    int seconds=int.Parse(Get("--seconds","110"));if(seconds<10||seconds>180)throw new ArgumentException("Smoke: 10–180 segundos.");
                    string run=Directory.GetDirectories(Path.Combine(root,"tests"),"run-*").Order().Last();
                    try
                    {
                        for(int second=0;second<seconds;second+=2)
                        {
                            await Task.Delay(2000,cancellation.Token);
                            if(process.HasExited)throw new IOException("O jogo encerrou durante o teste: "+process.ExitCode);
                            await File.AppendAllTextAsync(Path.Combine(run,"handles.jsonl"),JsonSerializer.Serialize(HandleAudit.Snapshot(process,Path.Combine(root,"installation.json")))+"\n");
                            if(second is 2 or 6 or 10 or 18 or 28 or 58 or 88 or 106)
                                await File.WriteAllTextAsync(Path.Combine(run,"console.txt"),"setenv CELLMARK_DUMP_NOW="+(second+1)+"\n");
                        }
                    }
                    finally{if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}}
                    Console.WriteLine("SMOKE_COMPLETED "+run);
                }break;
            default:throw new ArgumentException("Comando desconhecido.");
        }
        return 0;
    }
    sealed class DirectProgress(Action<Transfer> action):IProgress<Transfer>{public void Report(Transfer p)=>action(p);}
}
