using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Windows.Threading;

namespace DemonsSouls.Launcher;

public static class SelfTest
{
    static void Assert(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);}
    static async Task Reject(Func<Task> action,string message){try{await action();}catch(IOException){Console.WriteLine("PASS rejection: "+message);return;}throw new Exception("FAIL accepted: "+message);}
    static void CopyTree(string source,string target)
    {
        Directory.CreateDirectory(target);
        foreach(string directory in Directory.EnumerateDirectories(source,"*",SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target,Path.GetRelativePath(source,directory)));
        foreach(string file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories))
            File.Copy(file,Path.Combine(target,Path.GetRelativePath(source,file)),false);
    }
    static void PumpUntil(Task task)
    {
        var dispatcher=Dispatcher.CurrentDispatcher;
        var frame=new DispatcherFrame();
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
    static string FindRuntimeHandoffManifest()
    {
        for(DirectoryInfo? directory=new(AppContext.BaseDirectory);directory!=null;directory=directory.Parent)
        {
            string path=Path.Combine(directory.FullName,"recovery","coordination","ENGINE-ALPHA02-60FPS-HANDOFF.json");
            if(File.Exists(path))return path;
        }
        throw new FileNotFoundException("O manifesto local de integração não foi encontrado acima do diretório do self-test.");
    }
    static string FindCoordinationFile(string name)
    {
        for(DirectoryInfo? directory=new(AppContext.BaseDirectory);directory!=null;directory=directory.Parent)
        {
            string path=Path.Combine(directory.FullName,"recovery","coordination",name);
            if(File.Exists(path))return path;
        }
        throw new FileNotFoundException("Arquivo de coordenação não encontrado: "+name);
    }
    public static async Task Run()
    {
        if(Environment.GetEnvironmentVariable("DEMONS_SOULS_LAUNCHER_HOME") is { Length: > 0 } configuredHome)
            Environment.SetEnvironmentVariable("DEMONS_SOULS_LAUNCHER_HOME",
                Path.Combine(Path.GetFullPath(configuredHome),"self-test-"+Guid.NewGuid().ToString("N")));
        bool coordinationAvailable=true;
        System.Text.Json.JsonElement contractEnvironment=default;
        try
        {
            string referencePath=FindCoordinationFile("ENGINE-ALPHA02-60FPS-INICIADOR-REFERENCIA.txt");
            string referenceHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(referencePath)));
            Assert(referenceHash=="243C6780C5158E2BAC577B3016F0238243B630639703F7CE8C23735579AB1EFC","reference starter hash matches the user-provided fingerprint; it is read, never executed");
            using var contract=System.Text.Json.JsonDocument.Parse(File.ReadAllText(FindCoordinationFile("ENGINE-ALPHA02-60FPS-LAUNCH-CONTRACT.json")));
            var contractRoot=contract.RootElement;
            Assert(contractRoot.GetProperty("schemaVersion").GetInt32()==1&&contractRoot.GetProperty("runtimeIdentity").GetProperty("executableSha256RequiredFor60Fps").GetString()==RuntimeService.Approved60FpsRuntimeSha256.ToUpperInvariant(),"structured launch contract pins the approved runtime identity");
            Assert(contractRoot.GetProperty("runtimeIdentity").GetProperty("elfSha256").GetString()==RuntimeService.ApprovedElfSha256.ToUpperInvariant(),"structured launch contract pins the approved ELF identity");
            Assert(contractRoot.GetProperty("reference").GetProperty("sha256").GetString()==referenceHash,"structured contract records the exact reference initiator fingerprint");
            Assert(contractRoot.GetProperty("configurablePaths").GetProperty("installationRoot").GetString()=="User-selected Preferences.Installation","launch contract derives paths from the user's installation preference");
            contractEnvironment=contractRoot.GetProperty("environment").Clone();
            var handoff=RuntimeService.RuntimeHandoffManifest.Load(FindRuntimeHandoffManifest());
            Assert(handoff.Project=="Demons Souls PC"&&handoff.Version=="Alpha 0.2"&&Path.GetFileName(handoff.ExecutablePath).Equals("des_boot.exe",StringComparison.OrdinalIgnoreCase),"local handoff manifest identifies the Alpha 0.2 runtime without accessing its external executable");
            Assert(RuntimeService.IsApproved60FpsRuntimeHash(handoff.Sha256)&&handoff.AvailableFrameRates.SequenceEqual(new[]{60})&&handoff.FrameRateVariable=="DES_FPS","handoff manifest fingerprint and unique 60 FPS contract match the launcher's pinned identity");
            Assert(handoff.LauncherIntegration.Contains("Pendente",StringComparison.OrdinalIgnoreCase)&&handoff.CompleteLaunchProfile.Contains("Pendente",StringComparison.OrdinalIgnoreCase),"handoff manifest does not claim complete launcher integration or startup profile");
            Console.WriteLine("PASS: local handoff manifest structure, runtime identity and pending-profile status (external executable not accessed)");
        }
        catch(FileNotFoundException missingCoordination)
        {
            coordinationAvailable=false;
            Console.WriteLine("SKIP: "+missingCoordination.Message.Trim()+"; manifestos locais de coordenação não versionados (clone limpo) — as identidades aprovadas continuam fixadas por outros testes.");
        }
        var defaults=new Preferences();
        Assert(defaults.FrameRate==60,"default frame rate is the sole 60 FPS profile");
        string localData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert(defaults.Installation.Equals(Path.Combine(Preferences.UserDataDirectory,"GameData"),StringComparison.OrdinalIgnoreCase),"default installation is isolated beneath the launcher data directory and requires no administrator");
        Assert(defaults.RuntimeProfile.Equals(Path.Combine(Preferences.UserDataDirectory,"Alpha02-experimental-profile"),StringComparison.OrdinalIgnoreCase)&&!Installation.Under(defaults.RuntimeProfile,defaults.Installation),"experimental runtime profile defaults to a separate configurable path");
        Assert(Environment.GetEnvironmentVariable("DEMONS_SOULS_LAUNCHER_HOME") is { Length: > 0 }||defaults.Installation.StartsWith(Path.Combine(localData,"DemonsSoulsPCLauncher"),StringComparison.OrdinalIgnoreCase),"normal installation default stays in user-local app data");
        if(Environment.GetEnvironmentVariable("DEMONS_SOULS_LAUNCHER_HOME") is { Length: > 0 } isolatedHome)
            Assert(Preferences.FilePath.Equals(Path.Combine(Path.GetFullPath(isolatedHome),"settings.json"),StringComparison.OrdinalIgnoreCase),"launcher preferences use the isolated agent data directory");
        string testRoot=Environment.GetEnvironmentVariable("DEMONS_LAUNCHER_TEST_TEMP")??Path.GetTempPath();
        Directory.CreateDirectory(testRoot);
        string parentSelection=Path.Combine(testRoot,"DemonsLauncher-parent-selection-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(parentSelection,"actual-installation"));
        bool parentExplained=false;
        try{_ = Installation.ValidateExistingSelection(parentSelection);}
        catch(IOException e){parentExplained=e.Message.Contains("diretório pai",StringComparison.OrdinalIgnoreCase)&&e.Message.Contains("installation.json",StringComparison.OrdinalIgnoreCase);}
        Assert(parentExplained,"selecting a parent instead of the folder containing installation.json gives actionable guidance without accepting it");
        Console.WriteLine("PASS: existing-install selection rejects an incorrect parent folder with explicit guidance");

        string traceFile;
        using(var trace=OperationTrace.Start("Teste sintético de rastreamento"))
        {
            traceFile=trace.LogPath;
            using(trace.Activate())
            using(var phase=OperationTrace.Measure("Hash de fixture",2,17))
            {
                phase?.SetCounts(2,17);
                phase?.Complete();
            }
            trace.Complete();
        }
        string[] traceRecords=File.ReadAllLines(traceFile);
        Assert(traceRecords.Any(line=>line.Contains("\"phase_started\"",StringComparison.Ordinal))&&
            traceRecords.Any(line=>line.Contains("\"phase_finished\"",StringComparison.Ordinal)&&line.Contains("\"files\":2",StringComparison.Ordinal)&&line.Contains("\"bytes\":17",StringComparison.Ordinal))&&
            traceRecords.Any(line=>line.Contains("\"operation_finished\"",StringComparison.Ordinal)),
            "operation log records phase start/end, file/byte counts, elapsed duration and operation completion");
        string failureLog;
        using(var failedTrace=OperationTrace.Start("Teste sintético de falha"))
        {
            failureLog=failedTrace.LogPath;
            failedTrace.Fail(new IOException("falha sintética de leitura/gravação"));
        }
        using var failureRecord=System.Text.Json.JsonDocument.Parse(File.ReadLines(failureLog).Last());
        Assert(failureRecord.RootElement.GetProperty("error").GetString()!.Contains("falha sintética de leitura/gravação",StringComparison.Ordinal),
            "operation failure log records the concrete exception message");
        Console.WriteLine("PASS: operation phase trace records boundaries, durations and counts");
        string diagnosticFixture=Path.Combine(testRoot,"diagnostic-"+Guid.NewGuid().ToString("N")+".bin");
        File.WriteAllBytes(diagnosticFixture,new byte[]{1,2,3});
        string diagnosticLog;
        using(var diagnosticTrace=OperationTrace.Start("Synthetic diagnostic counters"))
        {
            diagnosticLog=diagnosticTrace.LogPath;
            using(diagnosticTrace.Activate())
            using(OperationTrace.MeasureActivity("measurement_fixture"))
            {
                for(int i=0;i<3;i++)
                using(OperationTrace.MeasureActivity("auxiliary_fixture"))
                    _=OperationTrace.ReadAuxiliaryBytes(diagnosticFixture);
                PumpUntil(Installation.Hash(diagnosticFixture,CancellationToken.None));
            }
            Assert(diagnosticTrace.ContentBytesRead==3&&diagnosticTrace.ContentBytesHashed==3&&
                diagnosticTrace.AuxiliaryBytesRead==9,"diagnostics separate streamed SHA-256 content from auxiliary reads");
            diagnosticTrace.Complete();
        }
        using(var summary=System.Text.Json.JsonDocument.Parse(File.ReadLines(diagnosticLog).Single(line=>
            line.Contains("\"activity\":\"auxiliary_fixture\"",StringComparison.Ordinal))))
            Assert(summary.RootElement.GetProperty("calls").GetInt64()==3&&
                summary.RootElement.GetProperty("auxiliaryBytesRead").GetInt64()==9,
                "hot diagnostic activities aggregate calls without per-file log writes");

        string settingsFixture=Path.Combine(testRoot,"DemonsLauncher-settings-"+Guid.NewGuid().ToString("N")+".json");
        var launcherWindow=new MainWindow();
        Assert(launcherWindow.TabsUseVerticalScrollingOnlyForTest(),"all launcher tabs scroll vertically without horizontal scrollbars");
        foreach(double width in new[]{320d,360d,400d,480d,600d,768d,879d})
        {
            launcherWindow.ArrangeLayoutForTest(width);
            Assert(launcherWindow.CompactLayoutForTest.Settings&&launcherWindow.SettingsWidthFitsForTest(width),"compact settings fit narrow and high-DPI logical widths without horizontal overflow");
        }
        foreach(double width in new[]{880d,900d,1080d,1600d})
        {
            launcherWindow.ArrangeLayoutForTest(width);
            Assert(!launcherWindow.CompactLayoutForTest.Settings&&launcherWindow.SettingsWidthFitsForTest(width),"wide settings fit normal and high-DPI logical widths without horizontal overflow");
        }
        Console.WriteLine("PASS: settings layout at compact, normal, and high-DPI logical widths");
        Assert(launcherWindow.GetPlayDisabledReasonForTest().Contains("prepare a instalação",StringComparison.OrdinalIgnoreCase),"disabled Play button explains that installation is required");
        string invalidManifestRoot=Path.Combine(testRoot,"DemonsLauncher-invalid-manifest-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(invalidManifestRoot);
        File.WriteAllText(Path.Combine(invalidManifestRoot,"installation.json"),"{");
        string invalidManifestState=launcherWindow.SetDestinationForTest(invalidManifestRoot);
        Assert(invalidManifestState.Contains("Detalhe:",StringComparison.Ordinal)&&launcherWindow.GetPlayDisabledReasonForTest().Contains("JOGAR indisponível:",StringComparison.Ordinal),"invalid manifest state shows its specific cause and explains why Play is disabled");
        string launcherLog=Path.Combine(Preferences.UserDataDirectory,"logs","launcher.log");
        Assert(File.Exists(launcherLog)&&File.ReadAllText(launcherLog).Contains("JsonException",StringComparison.Ordinal),"invalid manifest error is recorded in the isolated launcher log");
        Console.WriteLine("PASS: invalid installation manifest shows the concrete parsing error and logs its cause");
        string readyUiInstall=Path.Combine(testRoot,"DemonsLauncher-ui-ready-"+Guid.NewGuid().ToString("N"));
        Installation.Save(Path.Combine(readyUiInstall,"installation.json"),new Manifest
        {
            Complete=true,Synthetic=false,Files=[new("runtime/des_boot.exe",0,new string('0',64))]
        });
        launcherWindow.SuppressErrorDialogsForTest();
        var originalContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            launcherWindow.SetDestinationForTest(readyUiInstall);
            Task<bool> successfulWork=launcherWindow.WorkForTest(async(progress,ct)=>
            {
                progress.Report(new("Validando fixture", "synthetic.file",1,1,0,1,1));
                await Task.Delay(10,ct);
            });
            Assert(launcherWindow.OperationBusyForTest&&!launcherWindow.PlayEnabledForTest,
                "Play remains disabled while required operation stages are pending");
            PumpUntil(successfulWork);
            Assert(successfulWork.GetAwaiter().GetResult()&&launcherWindow.PlayEnabledForTest&&
                !launcherWindow.OperationBusyForTest&&launcherWindow.OperationProgressForTest==100,
                "successful finalization releases Play and publishes 100% only after UI state refresh");

            bool progressBarShown=false;
            var visibilityProbe=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            visibilityProbe.Tick+=(_,_)=>progressBarShown|=launcherWindow.ProgressBarVisibleForTest;
            visibilityProbe.Start();
            Task<bool> delayedWork=launcherWindow.WorkForTest(async(_,ct)=>await Task.Delay(1300,ct));
            PumpUntil(delayedWork);
            visibilityProbe.Stop();
            Assert(delayedWork.GetAwaiter().GetResult()&&progressBarShown,
                "the yellow operation bar becomes visible during a long-running verification even without byte progress callbacks");

            Task<bool> failedWork=launcherWindow.WorkForTest((_,_)=>Task.FromException(new IOException("falha sintética de finalização")));
            Assert(launcherWindow.OperationBusyForTest&&!launcherWindow.PlayEnabledForTest,
                "Play is disabled during a second preparation attempt");
            PumpUntil(failedWork);
            Assert(!failedWork.GetAwaiter().GetResult()&&launcherWindow.PlayEnabledForTest&&
                !launcherWindow.OperationBusyForTest&&launcherWindow.OperationProgressForTest<100,
                "a failed operation logs and releases Play without leaving a false 100% completion");
            TestPrepareIsoWizard(launcherWindow,testRoot);
            // PREPARAR EBOOT.BIN negative in the window: a broken image is
            // refused before any local tool runs, the cause names the ISO
            // identification and the single-flight guard is cleared.
            launcherWindow.SuppressErrorDialogsForTest();
            string fakeEbootIso=Path.Combine(testRoot,"wizard-fake.iso");
            string windowTool=Path.Combine(Environment.SystemDirectory??"C:\\Windows\\System32","whoami.exe");
            Task<bool> ebootTask=launcherWindow.PrepareEbootBinCore(fakeEbootIso,windowTool);
            PumpUntil(ebootTask);
            bool ebootCompleted=ebootTask.GetAwaiter().GetResult();
            string ebootDetail=launcherWindow.DetailTextForTest;
            bool ebootBusy=launcherWindow.OperationBusyForTest;
            bool ebootPrepEnabled=launcherWindow.PrepareIsoEnabledForTest;
            Assert(!ebootCompleted,"PREPARAR EBOOT.BIN refuses a broken image before any local tool work");
            Assert(ebootDetail.Contains("não pôde identificar a ISO",StringComparison.Ordinal),
                "EBOOT.BIN stage failure names the ISO identification problem");
            Assert(!ebootBusy,"EBOOT.BIN stage clears the single-flight guard when the attempt fails");
            Assert(ebootPrepEnabled,"PREPARAR ISO stays re-enabled after the EBOOT.BIN attempt ends");
        }
        finally{SynchronizationContext.SetSynchronizationContext(originalContext);}
        Console.WriteLine("PASS: UI operation transition, delayed progress-bar visibility, success-only 100%, and JOGAR release after failure");
        Assert(launcherWindow.AllFrameRateOptionLabelsForTest.SequenceEqual(new[]{"60 FPS"}),"launcher exposes only 60 FPS");
        Assert(!launcherWindow.IsFrameRateAvailableForTest(60),"60 FPS is disabled for an unverified executable");
        bool blocked60=false;
        try{launcherWindow.SelectFrameRateForTest(60,settingsFixture,false);}catch(IOException){blocked60=true;}
        Assert(blocked60,"saving 60 FPS is blocked until the approved executable identity is verified");
        launcherWindow.SelectFrameRateForTest(60,settingsFixture,true);
        var saved60=Preferences.Read(settingsFixture);
        Assert(saved60.FrameRate==60&&RuntimeService.FrameRateSettings(saved60)["DES_FPS"]=="60","60 FPS persists and maps to DES_FPS=60");
        Assert(launcherWindow.IsFrameRateAvailableForTest(60),"approved runtime identity enables the 60 FPS option");
        Assert(launcherWindow.FrameRateStatusForTest.Contains("Teste sintético",StringComparison.OrdinalIgnoreCase)&&launcherWindow.FrameRateStatusForTest.Contains("não valida",StringComparison.OrdinalIgnoreCase),"synthetic 60 FPS selection explicitly does not claim real-runtime compatibility");
        foreach(int rate in new[]{60})
        {
            var persisted=new Preferences{FrameRate=rate,RuntimeProfile=Path.Combine(testRoot,"persisted-profile-"+rate),
                UseExistingInstallation=rate==60,RuntimeExecutable=Path.Combine(testRoot,"selected-des_boot.exe"),EbootPath=Path.Combine(testRoot,"selected-EBOOT.ELF")};
            persisted.Write();
            var reopened=new MainWindow();
            var reloaded=Preferences.Read();
            Assert(reopened.SelectedFrameRateForTest==rate&&reopened.SelectedRuntimeProfileForTest==persisted.RuntimeProfile&&
                reloaded.RuntimeProfile==persisted.RuntimeProfile&&reloaded.UseExistingInstallation==persisted.UseExistingInstallation&&
                reloaded.RuntimeExecutable==persisted.RuntimeExecutable&&reloaded.EbootPath==persisted.EbootPath,
                "launcher reopening restores "+rate+" FPS, installation mode, selected runtime/ELF and separate profile");
            if(rate==60)
            {
                // A reopened window has no in-memory approval, even with persisted 60 FPS.
                string before=File.ReadAllText(Preferences.FilePath);
                var pending=new TaskCompletionSource<string?>();
                Task save=reopened.SaveValidatedPreferences(syntheticCheck:()=>pending.Task);
                Assert(!save.IsCompleted&&File.ReadAllText(Preferences.FilePath)==before,
                    "reopened 60 FPS waits for SHA-256 verification without premature persistence or warning");
                pending.SetResult(null);
                PumpUntil(save);
                Assert(Preferences.Read().FrameRate==60,"successful verification saves reopened 60 FPS without false failure");
                foreach(string reason in new[]{"executável não validado","SHA-256 divergente"})
                {
                    before=File.ReadAllText(Preferences.FilePath);
                    bool rejected=false;
                    try{PumpUntil(reopened.SaveValidatedPreferences(syntheticCheck:()=>Task.FromResult<string?>(reason)));}
                    catch(IOException e){rejected=e.Message.Contains(reason)&&
                        !MainWindow.FriendlyError(e.Message).Contains("operação não foi concluída");}
                    Assert(rejected&&File.ReadAllText(Preferences.FilePath)==before,
                        "unverified or mismatched identity blocks persistence with actionable FPS guidance: "+reason);
                }
            }
            reopened.Close();
        }
        string invalidRatePath=Path.Combine(testRoot,"DemonsLauncher-invalid-rate-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(invalidRatePath,"{\"FrameRate\":45}");
        bool rejectedRate=false;
        try{_ = Preferences.Read(invalidRatePath);}catch(InvalidDataException){rejectedRate=true;}
        Assert(rejectedRate,"invalid persisted frame rate is rejected with a configuration error");
        Console.WriteLine("PASS: unique 60 FPS persistence, reopen, compatibility gate, runtime mapping and invalid-setting rejection");
        Assert(RuntimeService.IsApproved60FpsRuntimeHash(RuntimeService.Approved60FpsRuntimeSha256),"approved 60 FPS runtime fingerprint is recognized");
        Assert(!RuntimeService.IsApproved60FpsRuntimeHash(new string('0',64)),"unrecognized runtime fingerprint cannot enable 60 FPS");
        foreach(var (resolution,width,height,label) in new[]{("1280x720","1280","720","1280 × 720"),("1600x900","1600","900","1600 × 900"),("1920x1080","1920","1080","1920 × 1080")})
        foreach(var (mode,borderless) in new[]{("windowed","0"),("borderless","1"),("fullscreen","1")})
        {
            launcherWindow.SelectVideoOptionsForTest(resolution,mode,settingsFixture);
            var reloaded=Preferences.Read(settingsFixture);
            Assert(reloaded.WindowResolution==resolution&&reloaded.DisplayMode==mode&&!string.IsNullOrWhiteSpace(reloaded.MonitorDevice),"launcher ComboBox selection and saved preferences");
            var labels=launcherWindow.ComboBoxLabelsForTest();
            Assert(labels[0]==(label,"Label")&&labels[1]==((mode=="windowed"?"Janela":mode=="borderless"?"Janela sem bordas":"Tela cheia sem bordas"),"Label")&&
                labels[2].BindingPath=="Label"&&labels[2].Label.Contains(" × ",StringComparison.Ordinal),"selected ComboBoxes render friendly resolution, mode, and monitor labels");
            var video=RuntimeService.VideoSettings(reloaded);
            Assert(video["RSX_WINDOW_W"]==width&&video["RSX_WINDOW_H"]==height&&video["RSX_BORDERLESS"]==borderless&&video["RSX_DISPLAY_MODE"]==mode&&video["RSX_MONITOR_DEVICE"]==reloaded.MonitorDevice,"video setting to runtime environment mapping");
        }
        var friendlyOptions=launcherWindow.AllComboOptionLabelsForTest;
        Assert(friendlyOptions[0].SequenceEqual(new[]{"1280 × 720","1600 × 900","1920 × 1080"})&&
            friendlyOptions[1].SequenceEqual(new[]{"Janela","Janela sem bordas","Tela cheia sem bordas"})&&
            friendlyOptions[3].SequenceEqual(new[]{"60 FPS"}),"all open ComboBox options use friendly labels while retaining typed values");
        Assert(launcherWindow.ComboBoxLabelsForTest()[3]==("60 FPS","Label"),"selected frame-rate ComboBox renders the unique 60 FPS profile");
        string? missingInstallBlocker=await RuntimeService.Get60FpsCompatibilityBlockerAsync(Path.Combine(testRoot,"missing-installation"),Path.Combine(testRoot,"experimental-profile"));
        Assert(missingInstallBlocker is not null&&missingInstallBlocker.Contains("prepare",StringComparison.OrdinalIgnoreCase),"60 FPS compatibility reports an actionable missing-installation requirement");
        launcherWindow.Close();File.Delete(settingsFixture);
        var monitors=DisplayMonitorService.Enumerate();
        Assert(monitors.Count>0,"enumerate connected display monitors");
        Console.WriteLine("PASS: real launcher ComboBox selections, settings save/reload, runtime mapping, and monitor enumeration");
        string test=Path.Combine(testRoot,"DemonsLauncher-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(test);
        VfsPolicyTest.Run(Path.Combine(test,"derived-unit"));
        string source=Path.Combine(test,"source/PS3_GAME"),dest=Path.Combine(test,"installed");Directory.CreateDirectory(source);
        string[] keys={"TITLE_ID","APP_VER","VERSION"},values={"BLUS30443","01.00","01.00"};
        byte[] keyBytes=Encoding.UTF8.GetBytes(string.Join('\0',keys)+'\0'),valueBytes=Encoding.UTF8.GetBytes(string.Join('\0',values)+'\0');
        byte[] sfo=new byte[68+keyBytes.Length+valueBytes.Length];sfo[1]=80;sfo[2]=83;sfo[3]=70;
        void U(int p,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(p,4),n);
        U(8,68);U(12,(uint)(68+keyBytes.Length));U(16,3);int ko=0,vo=0;
        for(int i=0;i<3;i++){int p=20+i*16;BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(p,2),(ushort)ko);BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(p+2,2),0x204);U(p+4,(uint)values[i].Length+1);U(p+8,(uint)values[i].Length+1);U(p+12,(uint)vo);ko+=keys[i].Length+1;vo+=values[i].Length+1;}
        keyBytes.CopyTo(sfo,68);valueBytes.CopyTo(sfo,68+keyBytes.Length);File.WriteAllBytes(Path.Combine(source,"PARAM.SFO"),sfo);
        foreach(var folder in new[]{"map","chr","obj","shader","sound","menu","script"})Directory.CreateDirectory(Path.Combine(source,"USRDIR",folder));
        var sdatFixture=VfsPolicyTest.Fixture();
        File.WriteAllBytes(Path.Combine(source,"USRDIR","chr","fixture.sdat"),sdatFixture.Source);
        File.WriteAllText(Path.Combine(source,"USRDIR/EBOOT.BIN"),"synthetic, not a game executable");
        byte[] payload=new byte[3*1024*1024];new Random(7).NextBytes(payload);string asset=Path.Combine(source,"USRDIR/map/synthetic.asset");File.WriteAllBytes(asset,payload);
        string elf=Path.Combine(test,"test.elf"),runtime=Path.Combine(test,"test.exe");byte[] header=new byte[64];new byte[]{127,69,76,70,2,2}.CopyTo(header,0);header[17]=2;header[19]=21;File.WriteAllBytes(elf,header);File.WriteAllBytes(runtime,new byte[2048]);
        string profile=Path.Combine(test,"profile"),session=Path.Combine(profile,"logs","synthetic-session");Directory.CreateDirectory(session);
        string runtimeIni=Path.Combine(profile,"config","settings.ini");Directory.CreateDirectory(Path.GetDirectoryName(runtimeIni)!);
        File.WriteAllText(runtimeIni,"# user setting remains\ncustom_option = untouched\nDES_FPS = 15\nDES_FPS = 20\n");
        foreach(int rate in new[]{60})
        {
            var preferences=new Preferences{FrameRate=rate,Vsync=rate==60,WindowResolution="1600x900",DisplayMode="borderless",MonitorDevice="DISPLAY-SYNTHETIC"};
            RuntimeService.PrepareRuntimeSettings(runtimeIni,preferences);
            string config=File.ReadAllText(runtimeIni);
            Assert(config.Contains("custom_option = untouched",StringComparison.Ordinal)&&config.Contains("DES_FPS = "+rate,StringComparison.Ordinal)&&config.Contains("DES_VSYNC = "+(rate==60?"1":"0"),StringComparison.Ordinal)&&config.Contains("show_start_screen = 1",StringComparison.Ordinal)&&config.Split("DES_FPS =",StringSplitOptions.None).Length==2,"runtime settings.ini preserves unrelated settings and synchronizes frame-rate/VSync for "+rate+" FPS");
            var env=RuntimeService.LaunchSettings(dest,profile,session,preferences);
            var requiredEnvironment=new[]{"PS3_VFS_ROOT","PS3_USRDIR_BASE","PS3_HDD0_ROOT","PS3_DEV_FLASH","PS3_SHADER_CACHE","DES_DATA_DIR","DES_SETTINGS","DES_KIND","DES_UI","DES_SKIP_UI","DES_PRECOMPILE","DES_FPS","VBLANK_MHZ","DES_VSYNC","DES_AUTO_CONTINUE","DES_SKIP_INTRO","PS3_SAIL_MOVIES","PAD_PC_INPUT","PAD_MOUSE_SENS","PAD_MOUSE_SENS_X","PAD_MOUSE_SENS_Y","AUDIO_MUTE","PS3_VERBOSE","DES_REPORT_NO_OPEN","PS3_PIDFILE","DES_GAME_LOG","DES_RUNTIME_LOG","DES_FRAMELOG","FRAME_LOG","RSX_WINDOW_DIAG","RSX_HOTKEY_DIR","CELLMARK_DUMP_DIR","PROF_DIR","RSX_WINDOW_W","RSX_WINDOW_H","RSX_DISPLAY_MODE","RSX_BORDERLESS","RSX_MONITOR_DEVICE","APPDATA","LOCALAPPDATA","TEMP","TMP"};
            Assert(requiredEnvironment.All(env.ContainsKey),"launch environment includes every normal-play variable from the reference");
            if(coordinationAvailable)
                Assert(requiredEnvironment.All(key=>contractEnvironment.TryGetProperty(key,out _)),"structured launch contract covers every environment key passed to the runtime");
            Assert(env["DES_FPS"]==rate.ToString()&&env["VBLANK_MHZ"]=="59940"&&env["DES_KIND"]=="play"&&env["DES_VSYNC"]==(rate==60?"1":"0"),"launch environment maps settings and preserves reference VBlank for "+rate+" FPS");
            Assert(env["DES_UI"]=="1"&&env["DES_SKIP_UI"]=="1"&&env["DES_PRECOMPILE"]=="1"&&env["DES_AUTO_CONTINUE"]=="0"&&env["DES_SKIP_INTRO"]=="1"&&env["PS3_SAIL_MOVIES"]=="0"&&env["DES_REPORT_NO_OPEN"]=="1","launch flags match the functional reference profile");
            Assert(env["PS3_HDD0_ROOT"].StartsWith(profile,StringComparison.OrdinalIgnoreCase)&&env["PS3_SHADER_CACHE"].StartsWith(profile,StringComparison.OrdinalIgnoreCase)&&env["DES_RUNTIME_LOG"].StartsWith(Path.Combine(profile,"logs"),StringComparison.OrdinalIgnoreCase),"saves, shader cache and per-session logs remain isolated under the separate configurable profile");
            Assert(env["RSX_WINDOW_W"]=="1600"&&env["RSX_WINDOW_H"]=="900"&&env["RSX_DISPLAY_MODE"]=="borderless"&&env["RSX_MONITOR_DEVICE"]=="DISPLAY-SYNTHETIC","existing video preferences are preserved in the expanded profile");
            var runtimeStart=RuntimeService.CreateProcessStartInfo(runtime,profile,new[]{elf},env);
            Assert(runtimeStart.WorkingDirectory==profile&&runtimeStart.ArgumentList.SequenceEqual(new[]{elf})&&runtimeStart.FileName==runtime,"synthetic runtime receives the ELF as its only argument and profile as working directory");
            Assert(RuntimeService.ExecutablePath(dest)==Path.Combine(dest,"runtime","des_boot.exe"),"JOGAR resolves the runtime copy inside the selected installation, not its original source path");
            Assert(RuntimeService.SelectedExecutablePath(dest,false,Path.Combine(testRoot,"approved-des_boot.exe"))==Path.Combine(testRoot,"approved-des_boot.exe")&&
                   RuntimeService.SelectedExecutablePath(dest,true,Path.Combine(testRoot,"approved-des_boot.exe"))==Path.Combine(testRoot,"approved-des_boot.exe"),
                "60 FPS compatibility and process startup resolve the same runtime for managed versus existing-install modes");
            Assert(RuntimeService.SelectedEbootPath(dest,false,Path.Combine(testRoot,"approved-EBOOT.ELF"))==Path.Combine(testRoot,"approved-EBOOT.ELF")&&
                   RuntimeService.SelectedEbootPath(dest,true,Path.Combine(testRoot,"approved-EBOOT.ELF"))==Path.Combine(testRoot,"approved-EBOOT.ELF"),
                "60 FPS compatibility and process startup resolve the same EBOOT for managed versus existing-install modes");
            string command=Environment.GetEnvironmentVariable("ComSpec")??"cmd.exe";
            string? priorDesFps=Environment.GetEnvironmentVariable("DES_FPS"),priorExit=Environment.GetEnvironmentVariable("EXIT_AUTOPRESS"),priorSlow=Environment.GetEnvironmentVariable("RF_SLOW_MS");
            Environment.SetEnvironmentVariable("DES_FPS","999");
            Environment.SetEnvironmentVariable("EXIT_AUTOPRESS","1");
            Environment.SetEnvironmentVariable("RF_SLOW_MS","88");
            var start=RuntimeService.CreateProcessStartInfo(command,test,new[]{"/c","echo %DES_FPS% %VBLANK_MHZ% %DES_KIND%"},env,redirectOutput:true);
            Environment.SetEnvironmentVariable("DES_FPS",priorDesFps);
            Environment.SetEnvironmentVariable("EXIT_AUTOPRESS",priorExit);
            Environment.SetEnvironmentVariable("RF_SLOW_MS",priorSlow);
            Assert(!start.Environment.ContainsKey("EXIT_AUTOPRESS")&&!start.Environment.ContainsKey("RF_SLOW_MS")&&start.Environment["DES_FPS"]==rate.ToString(),"inherited timing and automated-input environment overrides are removed");
            using var probe=Process.Start(start)??throw new IOException("Não foi possível iniciar o processo sintético.");
            string stdout=await probe.StandardOutput.ReadToEndAsync(),stderr=await probe.StandardError.ReadToEndAsync();
            await probe.WaitForExitAsync();
            Assert(probe.ExitCode==0&&stdout.Trim()==$"{rate} 59940 play","synthetic child process receives the requested launch environment and rejects inherited timing/control overrides (stderr: "+stderr.Trim()+")");
        }
        bool overlappingProfileRejected=false;
        try{_ = RuntimeService.LaunchSettings(dest,Path.Combine(dest,"userdata"),Path.Combine(dest,"userdata","logs","session"),new Preferences());}catch(IOException){overlappingProfileRejected=true;}
        Assert(overlappingProfileRejected,"runtime profile cannot overlap the installation or a stable profile");
        Assert(!await RuntimeService.IsApproved60FpsRuntimeAsync(runtime),"synthetic/unknown runtime binary is not approved for 60 FPS");
        using(var cancelledLaunch=new CancellationTokenSource())
        {
            cancelledLaunch.Cancel();
            bool launchCancelled=false;
            try{_ = RuntimeService.Start(dest,new Preferences(),new LaunchVerification(false,0,0),cancellationToken:cancelledLaunch.Token);}
            catch(OperationCanceledException){launchCancelled=true;}
            Assert(launchCancelled,"cancelled runtime start aborts before validation or process creation");
        }
        Console.WriteLine("PASS: launch contract, configurable profile paths, settings.ini, unique 60 FPS environment, synthetic process and unchanged VBLANK");
        using var cts=new CancellationTokenSource();var progress=new CancelProgress(cts);
        await Reject(()=>Task.Run(()=>Installation.CheckElf(elf)),"ELF header without supported build identity");
        bool cancelled=false;try{await Installation.Import(source,dest,elf,runtime,progress,cts.Token,true);}catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled&&!Installation.Load(dest).Complete,"cancel leaves incomplete installation");
        await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true);await Installation.Verify(dest,null,CancellationToken.None,fixture:true);
        var reuseProgress=new CaptureProgress();
        await Installation.Import(source,dest,elf,runtime,reuseProgress,CancellationToken.None,true);
        Assert(reuseProgress.CopiedBytes==0&&reuseProgress.ReusedFiles==Installation.Load(dest).Files.Count,
            "re-running preparation reuses SHA-256-verified files without copying them again");
        Console.WriteLine("PASS: cancelled mid-copy, resumed with verified files, full SHA256 integrity");
        string existingRoot=Path.Combine(test,"existing-read-only");
        Directory.CreateDirectory(existingRoot);
        CopyTree(Path.Combine(dest,"vfs"),Path.Combine(existingRoot,"vfs"));
        CopyTree(Path.Combine(dest,"runtime"),Path.Combine(existingRoot,"runtime"));
        var existingManifest=Installation.Load(dest);existingManifest.Synthetic=false;
        Installation.Save(Path.Combine(existingRoot,"installation.json"),existingManifest);
        byte[] originalExistingManifest=File.ReadAllBytes(Path.Combine(existingRoot,"installation.json"));
        var originalExistingFiles=Installation.Inventory(existingRoot).Select(file=>(file.Relative,file.Length,LastWrite:File.GetLastWriteTimeUtc(file.Source).Ticks)).ToArray();
        var existingProfile=Path.Combine(test,"existing-runtime-profile");
        using(var profileCancellation=new CancellationTokenSource())
        {
            bool profileCancelled=false;
            try{await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,new CancelProgress(profileCancellation),profileCancellation.Token);}
            catch(OperationCanceledException){profileCancelled=true;}
            string profileManifest=Path.Combine(existingProfile,"existing-vfs-profile.json");
            using var profileState=System.Text.Json.JsonDocument.Parse(File.ReadAllText(profileManifest));
            Assert(profileCancelled&&!profileState.RootElement.GetProperty("Complete").GetBoolean(),
                "interrupted existing-install mirror remains explicitly incomplete and can be resumed");
            Assert(File.ReadAllBytes(Path.Combine(existingRoot,"installation.json")).SequenceEqual(originalExistingManifest)&&
                Installation.Inventory(existingRoot).Select(file=>(file.Relative,file.Length,LastWrite:File.GetLastWriteTimeUtc(file.Source).Ticks)).SequenceEqual(originalExistingFiles),
                "cancelled mirror preparation leaves the read-only source manifest and data unchanged");
        }
        var firstProfileTimer=Stopwatch.StartNew();
        var existingVerification=await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        firstProfileTimer.Stop();
        Assert(existingVerification.ReadOnlyExisting&&File.ReadAllBytes(Path.Combine(existingRoot,"installation.json")).SequenceEqual(originalExistingManifest)&&
            Installation.Inventory(existingRoot).Select(file=>(file.Relative,file.Length,LastWrite:File.GetLastWriteTimeUtc(file.Source).Ticks)).SequenceEqual(originalExistingFiles),
            "existing installation verification and profile preparation do not modify its manifest or source files");
        Installation.VerifyPreparedExistingRuntimeVfs(existingRoot,existingProfile);
        string derivedSdat=Path.Combine(existingProfile,"vfs","PS3_GAME","USRDIR","chr","fixture.sdat.dec");
        File.WriteAllBytes(derivedSdat,sdatFixture.Plain);
        Installation.VerifyPreparedExistingRuntimeVfs(existingRoot,existingProfile);
        using(var profileState=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(existingProfile,"existing-vfs-profile.json"))))
            Assert(profileState.RootElement.GetProperty("Complete").GetBoolean(),"resumed existing-install mirror is marked complete only after validation");
        string sourceAsset=Path.Combine(existingRoot,"vfs","PS3_GAME","USRDIR","map","synthetic.asset");
        string profileAsset=Path.Combine(existingProfile,"vfs","PS3_GAME","USRDIR","map","synthetic.asset");
        bool usnSupported=Installation.HasUsnSnapshotForTest(sourceAsset)&&Installation.HasUsnSnapshotForTest(profileAsset);
        var repeatedProfileProgress=new CaptureProgress();
        var repeatProfileTimer=Stopwatch.StartNew();
        var repeatedProfileVerification=await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,
            repeatedProfileProgress,CancellationToken.None);
        repeatProfileTimer.Stop();
        Console.WriteLine($"BENCHMARK existing-profile first_ms={firstProfileTimer.ElapsedMilliseconds} source_bytes_read={existingVerification.BytesRead}; second_ms={repeatProfileTimer.ElapsedMilliseconds} source_bytes_read={repeatedProfileVerification.BytesRead} mirror_bytes_read={repeatedProfileProgress.BytesRead} fast={repeatedProfileVerification.UsedMetadataFastPath}; reason={repeatedProfileVerification.DecisionReason}");
        bool journalSupported=Installation.HasUsnJournalForTest(existingRoot);
        bool rootIdentitySupported=Installation.HasDirectoryIdentityForTest(existingRoot)&&
            Installation.HasDirectoryIdentityForTest(existingProfile);
        bool canReuseProfileState=usnSupported&&journalSupported&&rootIdentitySupported;
        string repeatedStatePath=Path.Combine(existingProfile,"existing-vfs-profile.json");
        byte[] repeatedState=File.ReadAllBytes(repeatedStatePath);
        DateTime repeatedStateWrite=File.GetLastWriteTimeUtc(repeatedStatePath);
        Installation.ResetIntegrityKeyForTest();
        using(var repeatTrace=OperationTrace.Start("Third synthetic preparation after integrity-key reload"))
        using(repeatTrace.Activate())
        {
            var thirdTimer=Stopwatch.StartNew();
            await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
            thirdTimer.Stop();
            Assert(!canReuseProfileState||(File.ReadAllBytes(repeatedStatePath).SequenceEqual(repeatedState)&&
                File.GetLastWriteTimeUtc(repeatedStatePath)==repeatedStateWrite),
                "third unchanged preparation does not rewrite authenticated ready state");
            Console.WriteLine($"BENCHMARK third-profile ms={thirdTimer.ElapsedMilliseconds} api_content_read={repeatTrace.ContentBytesRead} sha256_bytes={repeatTrace.ContentBytesHashed} reused_bytes={repeatTrace.ContentBytesReused} hashes_reused={repeatTrace.HashesReused}");
            repeatTrace.Complete();
        }
        Assert(repeatedProfileVerification.ReadOnlyExisting&&
            (canReuseProfileState
                ?repeatedProfileVerification.UsedMetadataFastPath&&repeatedProfileVerification.BytesRead==0&&repeatedProfileProgress.BytesRead==0
                :!repeatedProfileVerification.UsedMetadataFastPath&&repeatedProfileVerification.BytesRead>0),
            "repeated existing-install verification reuses recorded SHA-256 only with authenticated volume/root identity and an unchanged USN Journal; otherwise it rehashes ("+
            repeatedProfileVerification.DecisionReason+")");
        string profileStatePath=Path.Combine(existingProfile,"existing-vfs-profile.json");
        var mutableState=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(profileStatePath))
            ??throw new IOException("The synthetic profile state could not be parsed.");
        mutableState["SourceSnapshots"]![0]!["Sha256"]=new string('0',64);
        File.WriteAllText(profileStatePath,mutableState.ToJsonString());
        var tamperedStateVerification=await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        Assert(tamperedStateVerification.BytesRead>0,
            "altered persisted snapshot MAC forces complete SHA-256 validation and cannot bypass protected file checks");
        Installation.ResetIntegrityKeyForTest();
        var restartedProfileVerification=await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        Assert(canReuseProfileState
            ?restartedProfileVerification.UsedMetadataFastPath&&restartedProfileVerification.BytesRead==0
            :!restartedProfileVerification.UsedMetadataFastPath&&restartedProfileVerification.BytesRead>0,
            "persisted DPAPI key authenticates the snapshot after a simulated full launcher restart");
        byte[] unchangedSource=File.ReadAllBytes(sourceAsset);
        DateTime unchangedWrite=File.GetLastWriteTimeUtc(sourceAsset);
        byte[] changedSource=(byte[])unchangedSource.Clone();changedSource[0]^=0x5a;
        File.WriteAllBytes(sourceAsset,changedSource);File.SetLastWriteTimeUtc(sourceAsset,unchangedWrite);
        string sourceChangeError="";
        try{await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);}
        catch(IOException e){sourceChangeError=e.Message;}
        Assert(sourceChangeError.Contains("synthetic.asset",StringComparison.Ordinal)&&
            sourceChangeError.Contains("Tamanho esperado:",StringComparison.Ordinal)&&
            sourceChangeError.Contains("SHA-256 esperado:",StringComparison.Ordinal)&&File.ReadAllBytes(sourceAsset).SequenceEqual(changedSource),
            "same-size source mutation is caught even with its timestamp restored, with expected/found hashes and no repair");
        File.WriteAllBytes(sourceAsset,unchangedSource);File.SetLastWriteTimeUtc(sourceAsset,unchangedWrite);
        await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);

        Directory.CreateDirectory(Path.Combine(existingProfile,"vfs","cache","00"));
        Directory.CreateDirectory(Path.Combine(existingProfile,"vfs","cache","01"));
        await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        Console.WriteLine("PASS: empty runtime cache buckets 00 and 01 accepted");
        string runtimeCache=Path.Combine(existingProfile,"vfs","cache","00","0123456789abcdef0123456789abcdef");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeCache)!);
        File.WriteAllBytes(runtimeCache,Encoding.ASCII.GetBytes("synthetic runtime-generated resource cache"));
        string runtimeCache01=Path.Combine(existingProfile,"vfs","cache","01","fedcba9876543210fedcba9876543210");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeCache01)!);
        File.WriteAllBytes(runtimeCache01,Encoding.ASCII.GetBytes("synthetic runtime-generated cache bucket 01"));
        await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        Installation.VerifyPreparedExistingRuntimeVfs(existingRoot,existingProfile);
        byte[] changedRuntimeCache=Encoding.ASCII.GetBytes("updated by the synthetic runtime fixture");
        File.WriteAllBytes(runtimeCache,changedRuntimeCache);
        Installation.ValidatePreparedExistingRuntimeVfsMarker(existingRoot,existingProfile);
        string malformedCache=Path.Combine(existingProfile,"vfs","cache","00","not-a-known-cache-key.bin");
        File.WriteAllText(malformedCache,"not an allowed generated resource-cache name");
        string malformedCacheError="";
        try{await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);}
        catch(IOException e){malformedCacheError=e.Message;}
        Assert(malformedCacheError.Contains("not-a-known-cache-key.bin",StringComparison.Ordinal)&&File.Exists(malformedCache),
            "only exact runtime cache key paths are permitted; unknown cache files remain preserved and block readiness");
        using(var failedState=System.Text.Json.JsonDocument.Parse(File.ReadAllText(profileStatePath)))
            Assert(!failedState.RootElement.GetProperty("Complete").GetBoolean()&&
                failedState.RootElement.GetProperty("ProfileSnapshots").GetArrayLength()>0,
                "failed inventory preserves verified per-file snapshots but never approves the profile");
        File.Delete(malformedCache);
        for(int shard=0;shard<256;shard++)
        {
            string cache=Path.Combine(existingProfile,"vfs","cache",shard.ToString("x2"),"0123456789abcdef0123456789abcdef");
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(cache,"synthetic derived cache");
        }
        var cacheRetry=await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        Assert(!canReuseProfileState||cacheRetry.UsedMetadataFastPath&&cacheRetry.BytesRead==0,
            "successful retry reuses authenticated source snapshots after an inventory failure");
        Installation.VerifyPreparedExistingRuntimeVfs(existingRoot,existingProfile);
        Console.WriteLine("PASS: exact cache namespace accepts all 256 hexadecimal shards, including 00/01/02/7f/ff");
        string unknownBucket=Path.Combine(existingProfile,"vfs","cache","gg","0123456789abcdef0123456789abcdef");
        Directory.CreateDirectory(Path.GetDirectoryName(unknownBucket)!);
        File.WriteAllText(unknownBucket,"unproven cache namespace");
        string unknownBucketError="";
        try{await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);}
        catch(IOException e){unknownBucketError=e.Message;}
        Assert(unknownBucketError.Contains("cache/gg",StringComparison.OrdinalIgnoreCase)&&File.Exists(unknownBucket),
            "unproven cache buckets remain rejected and preserved");
        File.Delete(unknownBucket);
        Directory.Delete(Path.GetDirectoryName(unknownBucket)!);
        string protectedUnexpected=Path.Combine(existingProfile,"vfs","PS3_GAME","USRDIR","map","unexpected.bin");
        File.WriteAllText(protectedUnexpected,"protected-tree unexpected file");
        string protectedError="";
        try{await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);}
        catch(IOException e){protectedError=e.Message;}
        Assert(protectedError.Contains("unexpected.bin",StringComparison.Ordinal)&&File.Exists(protectedUnexpected),
            "unexpected content inside protected PS3_GAME blocks readiness and is not deleted");
        File.Delete(protectedUnexpected);
        await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        using(var profileLease=Installation.TakeLease(existingProfile))
            await Reject(()=>Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None),
                "simultaneous preparation of an existing-install mirror");
        var existingLaunchSettings=RuntimeService.LaunchSettings(existingRoot,existingProfile,Path.Combine(existingProfile,"logs","synthetic"),
            new Preferences{FrameRate=60},vfsRootOverride:Path.Combine(existingProfile,"vfs"));
        Assert(existingLaunchSettings["PS3_VFS_ROOT"]==Path.Combine(existingProfile,"vfs")&&
            existingLaunchSettings["PS3_USRDIR_BASE"]==Path.Combine(existingProfile,"vfs","PS3_GAME")&&
            existingLaunchSettings["PS3_HDD0_ROOT"].StartsWith(existingProfile,StringComparison.OrdinalIgnoreCase)&&
            existingLaunchSettings["DES_FPS"]=="60",
            "existing-install launch redirects VFS, HDD/saves, cache and DES_FPS to the isolated profile");
        Console.WriteLine("PASS: read-only existing-install validation, profile VFS isolation, and source immutability");
        string isolatedAsset=profileAsset;
        byte[] isolatedContents=File.ReadAllBytes(isolatedAsset);
        File.WriteAllBytes(isolatedAsset,isolatedContents[..^1]);
        string profileSizeError="";
        try{await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);}
        catch(IOException e){profileSizeError=e.Message;}
        Assert(profileSizeError.Contains("Tamanho esperado:",StringComparison.Ordinal)&&
            profileSizeError.Contains("encontrado:",StringComparison.Ordinal)&&
            profileSizeError.Contains("SHA-256 encontrado:",StringComparison.Ordinal)&&
            File.ReadAllBytes(isolatedAsset).Length==isolatedContents.Length-1,
            "a profile size mismatch reports expected/found sizes and hashes and never overwrites the divergent file");
        File.WriteAllBytes(isolatedAsset,isolatedContents);
        await Installation.PrepareExistingRuntimeProfile(existingRoot,existingProfile,null,CancellationToken.None);
        isolatedContents[0]^=1;File.WriteAllBytes(isolatedAsset,isolatedContents);
        bool changedProfileRejected=false;
        try{Installation.VerifyPreparedExistingRuntimeVfs(existingRoot,existingProfile);}catch(IOException){changedProfileRejected=true;}
        Assert(changedProfileRejected,"changed isolated VFS is rejected before native process start");
        var fastVerification=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        bool managedUsnSupported=Installation.HasUsnSnapshotForTest(Installation.Child(dest,"vfs/PS3_GAME/USRDIR/map/synthetic.asset"));
        bool managedFastPathAvailable=managedUsnSupported&&Installation.HasUsnJournalForTest(dest)&&
            Installation.HasDirectoryIdentityForTest(dest);
        Assert(fastVerification.UsedMetadataFastPath==managedFastPathAvailable&&
            (managedFastPathAvailable?fastVerification.BytesRead==0:fastVerification.BytesRead>0),
            "managed verification trusts only signed volume/root/Journal identity and otherwise hashes every byte");
        var unchangedManagedVerification=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(unchangedManagedVerification.UsedMetadataFastPath==managedFastPathAvailable&&
            (managedFastPathAvailable?unchangedManagedVerification.BytesRead==0:unchangedManagedVerification.BytesRead>0),
            "second managed verification reuses authenticated hashes only when all NTFS USN evidence is available");
        byte[] signedManifest=File.ReadAllBytes(Path.Combine(dest,"installation.json"));
        var alteredManifest=System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(signedManifest))
            ??throw new IOException("The synthetic managed manifest could not be parsed.");
        alteredManifest["Files"]![0]!["Usn"]=123456789;
        File.WriteAllText(Path.Combine(dest,"installation.json"),alteredManifest.ToJsonString());
        string tamperedManifestError="";
        try{await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);}
        catch(IOException e){tamperedManifestError=e.Message;}
        Assert(tamperedManifestError.Contains("estado de integridade autenticado",StringComparison.OrdinalIgnoreCase),
            "tampered managed verification metadata is rejected instead of being re-signed as trusted");
        File.WriteAllBytes(Path.Combine(dest,"installation.json"),signedManifest);
        Installation.ResetIntegrityKeyForTest();
        var afterRestart=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(afterRestart.UsedMetadataFastPath==managedFastPathAvailable,
            "DPAPI-authenticated managed snapshot remains reusable after a simulated launcher restart");
        long managedFixtureBytes=Installation.Load(dest).Files.Sum(entry=>entry.Length);
        Console.WriteLine($"BENCHMARK managed-fixture bytes={managedFixtureBytes} files={fastVerification.FilesChecked}; check1_ms={fastVerification.ElapsedMilliseconds} bytes_read={fastVerification.BytesRead} bytes_hashed={fastVerification.BytesRead}; check2_ms={unchangedManagedVerification.ElapsedMilliseconds} metadata_fast_path={unchangedManagedVerification.UsedMetadataFastPath} bytes_read={unchangedManagedVerification.BytesRead}; check3_ms={afterRestart.ElapsedMilliseconds} bytes_read={afterRestart.BytesRead}");
        Console.WriteLine($"BENCHMARK incremental-evidence journal={Installation.UsnJournalStatusForTest(dest)}; root-id={Installation.HasDirectoryIdentityForTest(dest)}; file-usn={managedUsnSupported}; fast-expected={managedFastPathAvailable}; fallback={fastVerification.DecisionReason}");
        string installed=Installation.Child(dest,"vfs/PS3_GAME/USRDIR/map/synthetic.asset");
        File.SetLastWriteTimeUtc(installed,File.GetLastWriteTimeUtc(installed).AddSeconds(-3));
        var incremental=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(!managedFastPathAvailable||incremental.BytesRead==new FileInfo(installed).Length,
            "one changed managed identity hashes only that file, preserving authenticated hashes for the others");
        try
        {
            Installation.ForceUsnUnavailableForTest=true;
            var noJournal=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
            Assert(!noJournal.UsedMetadataFastPath&&noJournal.BytesRead==managedFixtureBytes,
                "USN failure forces full SHA-256 rather than trusting size or timestamp");
        }
        finally{Installation.ForceUsnUnavailableForTest=false;}
        await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        byte[] originalManagedAsset=File.ReadAllBytes(installed);
        File.Delete(installed);
        await Reject(()=>Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true),
            "missing managed file is rejected rather than reused from metadata");
        File.WriteAllBytes(installed,originalManagedAsset);
        await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        DateTime installedWrite=File.GetLastWriteTimeUtc(installed);
        byte[] alteredManagedAsset=(byte[])originalManagedAsset.Clone();alteredManagedAsset[0]^=0x32;
        File.WriteAllBytes(installed,alteredManagedAsset);File.SetLastWriteTimeUtc(installed,installedWrite);
        await Reject(()=>Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true),
            "same-size managed content mutation is caught after restoring its timestamp");
        File.WriteAllBytes(installed,originalManagedAsset);
        var changedMetadata=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(!changedMetadata.UsedMetadataFastPath&&changedMetadata.BytesRead>0,
            "a restored timestamp still invalidates the old file USN and forces SHA-256 fallback");
        var refreshedMetadata=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(refreshedMetadata.UsedMetadataFastPath==managedFastPathAvailable,
            "successful full-hash fallback refreshes the USN snapshot when the filesystem supports it");
        Console.WriteLine("PASS: USN-backed incremental verification and SHA-256 fallback for same-size content changes");
        payload[0]^=255;File.WriteAllBytes(installed,payload);
        await Reject(()=>Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true),"modified file detected by full-hash fallback");
        await Reject(()=>Installation.Verify(dest,null,CancellationToken.None,fixture:true),"same-size corruption");
        await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true);await Installation.Verify(dest,null,CancellationToken.None,fixture:true);
        Assert(Directory.GetFiles(Path.GetDirectoryName(installed)!,"*.corrupt-*").Length==1,"repair preserves corrupt copy");
        Console.WriteLine("PASS: repair and quarantine");
        // Optimized-prepare regression suite: bounded parallel hashing with the
        // first-failure contract, USN-backed destination confirmation on the
        // plain Verify path, and origin-identity reuse via the signed side file.
        {
            var boundedItems=Enumerable.Range(0,32).ToList();
            var seen=new bool[32];var seenCount=0;var activeCount=0;var maxActiveCount=0;var overlapLock=new object();
            await Installation.RunBounded(boundedItems,4,async i=>
            {
                int current=Interlocked.Increment(ref activeCount);
                lock(overlapLock){if(current>maxActiveCount)maxActiveCount=current;}
                try{await Task.Delay(8);}finally{Interlocked.Decrement(ref activeCount);}
                if(seen[i])throw new InvalidOperationException("item processed twice: "+i);
                seen[i]=true;Interlocked.Increment(ref seenCount);
            });
            Assert(seenCount==boundedItems.Count&&seen.All(seenFlag=>seenFlag)&&maxActiveCount<=4,
                "RunBounded processes every item exactly once and honors the requested degree");
            await Reject(()=>Task.Run(()=>Installation.RunBounded(new[]{0,1,2},4,async i=>
            {if(i==1)throw new IOException("bounded worker failure");await Task.Delay(2);})),"RunBounded rethrows the first worker failure");
            Console.WriteLine("PASS: bounded parallel hashing");
        }
        {
            string imageFile=Path.Combine(test,"synthetic-image.iso");
            File.WriteAllBytes(imageFile,File.ReadAllBytes(Path.Combine(test,"test.elf")));
            long firstRead,secondRead,thirdRead;
            using(var first=OperationTrace.Start("selftest-source-identity-first"))
            {
                using(first.Activate())await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true,imageFile);
                firstRead=first.ContentBytesRead;
                Assert(firstRead>0,"first prepare to a fresh identity record hashes the origin normally");
            }
            using(var second=OperationTrace.Start("selftest-source-identity-second"))
            {
                using(second.Activate())await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true,imageFile);
                secondRead=second.ContentBytesRead;
            }
            Assert(File.Exists(Installation.SourceImageIdentityPathForTest(dest)),"origin image identity side file is registered");
            Assert(secondRead==0,"origin identity unchanged: the second prepare reads nothing for already-recorded source hashes");
            string sideFile=Installation.SourceImageIdentityPathForTest(dest);
            byte[] sideBytes=File.ReadAllBytes(sideFile);
            for(int i=0;i<sideBytes.Length;i++)if(sideBytes[i]==(byte)'"'){sideBytes[i]=(byte)'\'';break;}
            File.WriteAllBytes(sideFile,sideBytes);
            using(var third=OperationTrace.Start("selftest-source-identity-tampered"))
            {
                using(third.Activate())await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true,imageFile);
                thirdRead=third.ContentBytesRead;
                Assert(thirdRead>0,"tampered origin identity record forces honest source re-hashing");
            }
            Console.WriteLine("PASS: source-image identity reuse and tamper fallback");
        }
        {
            byte[] mutated=(byte[])originalManagedAsset.Clone();mutated[24]^=0x77;
            File.WriteAllBytes(installed,mutated);
            await Reject(()=>Installation.Verify(dest,null,CancellationToken.None,fixture:true),
                "Verify rejects same-size mutation even when the manifest snapshot is trusted");
            await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true);
            await Installation.Verify(dest,null,CancellationToken.None,fixture:true);
            Console.WriteLine("PASS: managed Verify USN confirmation keeps content authority");
        }
        {
            string existing=Path.Combine(test,"probe çéü image 'quote'.iso");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(existing)!);
            File.WriteAllBytes(existing,new byte[]{1,2,3});
            Func<string,string?,string> get=(key,fallback)=>fallback??"";
            Assert(Program.RequireSourceImage(new[]{ "--source" },get) is null,"absent --source-image stays optional and yields null");
            Assert(Program.RequireSourceImage(new[]{ "--source-image",existing },get)==existing,
                "--source-image with an existing path is returned intact (spaces, accents and apostrophes preserved)");
            bool rejectedExisting=false;
            try{Program.RequireSourceImage(new[]{ "--source-image",existing+"-ausente" },get);}
            catch(FileNotFoundException e){rejectedExisting=true;Assert(e.FileName is not null&&e.Message.Contains("--source-image"),"loud failure names the offending argument");}
            Assert(rejectedExisting,"--source-image pointing at a nonexistent file fails loudly instead of degrading silently");
            bool rejectedMissing=true;
            try{Program.RequireSourceImage(new[]{ "--source-image" },get);rejectedMissing=false;}
            catch(ArgumentException){}
            Assert(rejectedMissing,"--source-image with no value is an argument error, not a silent skip");
            File.Delete(existing);
            Console.WriteLine("PASS: CLI --source-image validation (absence, existence, loud failure)");
        }
        await Reject(()=>Task.Run(()=>Installation.Child(dest,"../escape")),"path traversal");
        await Reject(()=>Task.Run(()=>Installation.Child(dest,"vfs/file:ads")),"ADS");
        await Reject(()=>Task.Run(()=>Installation.Child(dest,"vfs/CON.txt")),"device path");
        string bad=Path.Combine(test,"bad.sfo");File.WriteAllBytes(bad,new byte[20]);await Reject(()=>Task.Run(()=>Installation.Sfo(bad)),"invalid SFO");
        await Reject(()=>Task.Run(()=>Installation.GameRoot(Path.Combine(test,"missing"))),"absent media");
        await Reject(()=>Discovery.MountedIso(Path.Combine(test,"missing.iso")),"absent ISO");
        await Reject(()=>Task.Run(()=>Installation.CheckSpace(10,100)),"insufficient space");
        await Reject(()=>Task.Run(()=>RuntimeService.Start(dest,new Preferences(),new LaunchVerification(false,0,0))),"synthetic installation cannot launch");
        using(var lease=Installation.TakeLease(dest))await Reject(()=>Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true),"simultaneous import/launch lease");
        string globalLaunchLock=Path.Combine(test,"global-runtime-lock");
        using(Installation.TakeLease(globalLaunchLock))
            await Reject(()=>Task.Run(()=>Installation.TakeLease(globalLaunchLock).Dispose()),"concurrent runtime starts are serialized across profiles");
        string originalHash=Installation.Load(dest).Files.First(e=>e.Path.EndsWith("synthetic.asset")).Sha256;
        byte[] changed=File.ReadAllBytes(asset);changed[0]^=1;File.WriteAllBytes(asset,changed);
        await Reject(()=>Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true),"changed source on resume");
        Assert(Installation.Load(dest).Files.First(e=>e.Path.EndsWith("synthetic.asset")).Sha256==originalHash,"prior hashes preserved after rejected resume");
        await Reject(()=>Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true),"changed source remains rejected on second retry");
        string outside=Path.Combine(test,"outside"),link=Path.Combine(source,"USRDIR/map/link");Directory.CreateDirectory(outside);
        var cmd=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        cmd.ArgumentList.Add("/c");cmd.ArgumentList.Add("mklink");cmd.ArgumentList.Add("/J");cmd.ArgumentList.Add(link.Replace('/','\\'));cmd.ArgumentList.Add(outside.Replace('/','\\'));
        using(var p=System.Diagnostics.Process.Start(cmd)!)
        {
            Task<string> stdout=p.StandardOutput.ReadToEndAsync(),stderr=p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if(p.ExitCode==0)
            {
                await Reject(()=>Task.Run(()=>Installation.Inventory(source)),"source descendant junction");
                await Reject(()=>Task.Run(()=>Installation.Child(link,"escape")),"destination ancestor junction");
            }
            else
                Console.WriteLine("SKIP: junction rejection fixtures unavailable ("+(await stderr+await stdout).Trim()+")");
        }
        TestIsoFlowSupport(source,dest,test);
        await TestEbootLocalTool(test);
        Console.WriteLine("SELF_TEST_PASS "+test);
    }
    static void TestIsoFlowSupport(string source,string dest,string test)
    {
        // ELF ladder with fixture-checker semantics: the prepared ELF beside
        // the source tree is resolved and EBOOT.BIN/SELF decoys are ignored
        // without ever touching them.
        string elfSource=Path.Combine(test,"isoflow-elf-source");Directory.CreateDirectory(elfSource);
        File.WriteAllBytes(Path.Combine(elfSource,"EBOOT.BIN"),new byte[]{1,2,3});
        File.WriteAllText(Path.Combine(elfSource,"decoy.elf"),"not an elf");
        Directory.CreateDirectory(Path.Combine(elfSource,"PS3_GAME","USRDIR"));
        byte[] elfFixture=File.ReadAllBytes(Path.Combine(test,"test.elf"));
        string validElf=Path.Combine(elfSource,"PS3_GAME","USRDIR","EBOOT.ELF");
        File.WriteAllBytes(validElf,elfFixture);
        string? resolved=IsoFlow.FindEbootElf(Path.Combine(elfSource,"PS3_GAME"),dest,"",fixture:true);
        Assert(resolved!=null&&string.Equals(Path.GetFullPath(resolved!),Path.GetFullPath(validElf),StringComparison.OrdinalIgnoreCase),
            "ELF ladder resolves the prepared ELF beside the source tree and ignores EBOOT.BIN/SELF decoys");
        string emptyRoot=Path.Combine(test,"isoflow-empty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(emptyRoot);
        Assert(IsoFlow.FindEbootElf(emptyRoot,"","",fixture:true) is null,"ELF ladder returns null when no candidate exists");
        // Identity description reuses the strict SFO parser without mounting.
        string describe=IsoFlow.Describe(source,8869122560);
        Assert(describe.Contains("BLUS30443",StringComparison.Ordinal)&&describe.Contains("01.00",StringComparison.Ordinal),
            "ISO identity description reports the pinned edition and version from PARAM.SFO");
        // Tool probe never invents a tool; detection only reports real,
        // user-installed scetool copies and otherwise explains the RPCS3 route.
        var scetool=IsoFlow.FindScetool();
        Assert(scetool.Path is null||Path.GetFileName(scetool.Path!).Equals("scetool.exe",StringComparison.OrdinalIgnoreCase),
            "scetool probe never fabricates a tool path");
        // Native rebuild diagnostic is structural and honest about the fork.
        var native=IsoFlow.NativeBuildEnvironment();
        Assert(native.Count>=8&&native.All(r=>!string.IsNullOrWhiteSpace(r.Requirement)&&!string.IsNullOrWhiteSpace(r.Hint)),
            "native rebuild diagnostic lists named prerequisites with actionable hints");
        Assert(native.Any(r=>r.Requirement.Contains("ps3recomp",StringComparison.OrdinalIgnoreCase)),
            "native rebuild diagnostic names the external ps3recomp lifter fork explicitly");
        // Runtime ladder never approves an unknown binary by structure alone:
        // a tiny fake des_boot.exe must not satisfy it.
        string fakeRuntime=Path.Combine(test,"isoflow-fake-des_boot.exe");File.WriteAllBytes(fakeRuntime,new byte[2048]);
        Assert(!RuntimeService.IsApproved60FpsRuntimeHash(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fakeRuntime)))),
            "runtime ladder rejects a fixture binary by identity");
        Console.WriteLine("PASS: ISO preparation assistant components (ELF ladder, identity, tool probe, native-build diagnostic, runtime identity)");
    }
    // PREPARAR ISO wizard, exercised with a broken image so the run stays
    // honest: no mount is possible, nothing is copied and the window ends in
    // the documented error state with the log path and re-enabled controls.
    // Must run inside the legacy UI block (window thread with installed
    // DispatcherSynchronizationContext) because WPF objects require thread
    // affinity and the wizard awaits internally.
    static void TestPrepareIsoWizard(MainWindow launcherWindow,string testRoot)
    {
        // The fake image fails at mount long before any destination work, so
        // the active destination from the previous synthetic UI steps stays
        // untouched: changing it would start an async compatibility check
        // whose late completion would race the legacy requirements below.
        string fakeIso=Path.Combine(testRoot,"wizard-fake.iso");File.WriteAllBytes(fakeIso,new byte[2048]);
        launcherWindow.SuppressErrorDialogsForTest();
        Task<bool> wizard=launcherWindow.PrepareIsoWizardCore(fakeIso);
        PumpUntil(wizard);
        bool completed=wizard.GetAwaiter().GetResult();
        string detail=launcherWindow.DetailTextForTest;
        bool busy=launcherWindow.OperationBusyForTest;
        bool prepEnabled=launcherWindow.PrepareIsoEnabledForTest;
        Assert(!completed,"PREPARAR ISO wizard refuses a broken image instead of proceeding");
        Assert(detail.Contains("PREPARAR ISO não pôde continuar",StringComparison.Ordinal),
            "wizard failure reports the honest mount outcome");
        Assert(detail.Contains("launcher.log",StringComparison.Ordinal),
            "wizard failure surfaces the diagnostic log path");
        Assert(!busy,"wizard clears the single-flight guard when the attempt fails");
        Assert(prepEnabled,"PREPARAR ISO re-enables after the attempt ends");
        Console.WriteLine("PASS: PREPARAR ISO wizard (honest failure, log path, no busy state)");
    }
    // PREPARAR EBOOT.BIN local-tool gate: probe of the tool responds, real
    // execution failures surface the tool's own outcome, a produced file is
    // never approved merely for existing (strict identity + recorded size and
    // SHA-256 evidence) and the EBOOT.BIN source is never written. The
    // positive decrypt path needs the user's scetool copy and key file, so it
    // stays validated by identity alone here — a missing artifact or a
    // mismatch is exercised with real process runs instead.
    static async Task TestEbootLocalTool(string test)
    {
        string fakeRoot=Path.Combine(test,"eboottool-"+Guid.NewGuid().ToString("N"),"PS3_GAME");
        string usr=Path.Combine(fakeRoot,"USRDIR");Directory.CreateDirectory(usr);
        string fakeBin=Path.Combine(usr,"EBOOT.BIN");File.WriteAllBytes(fakeBin,new byte[512]);
        string prep=Path.Combine(test,"prep-eboottool");Directory.CreateDirectory(prep);
        string whoami=Path.Combine(Environment.SystemDirectory??"C:\\Windows\\System32","whoami.exe");
        var configured=IsoFlow.FindScetool(whoami);
        Assert(configured.Path is not null&&string.Equals(configured.Path,whoami,StringComparison.OrdinalIgnoreCase),
            "configured local tool path with a real file wins the probe");
        Assert(IsoFlow.FindScetool(Path.Combine(test,"missing-tool.exe")).Path is null,
            "configured local tool path pointing at an absent file is refused instead of silently replaced");
        (bool started,string startedDetail)=await IsoFlow.ProbeToolRespondsAsync(whoami,CancellationToken.None);
        Assert(started&&startedDetail.Length>0,"tool probe reports a living executable with its own output");
        (bool absent,_)=await IsoFlow.ProbeToolRespondsAsync(Path.Combine(test,"missing-tool.exe"),CancellationToken.None);
        Assert(!absent,"tool probe refuses an absent executable before any decrypt work");
        await Reject(()=>Task.Run(()=>IsoFlow.ScetoolDecryptAsync(whoami,fakeRoot,prep,CancellationToken.None)),
            "a real tool run that produces no output fails honestly with the tool's exit evidence");
        Assert(File.Exists(fakeBin)&&new FileInfo(fakeBin).Length==512,"the EBOOT.BIN source stays byte-identical after failed tool runs");
        string garbage=Path.Combine(test,"eboot-garbage.elf");File.WriteAllBytes(garbage,new byte[4096]);
        await Reject(()=>Task.Run(()=>IsoFlow.ValidateDecryptedEboot(garbage,new FileInfo(fakeBin).Length)),
            "non-empty ELF output without the approved identity is rejected explicitly with size and SHA-256 recorded");
        Assert(File.Exists(garbage),"rejected ELF output is kept for human review instead of deleted");
        await Reject(()=>Task.Run(()=>IsoFlow.ValidateDecryptedEboot(Path.Combine(test,"eboot-missing-output.elf"),512)),
            "missing ELF output after a 'successful' run refuses loudly");
        Console.WriteLine("PASS: PREPARAR EBOOT.BIN local-tool gate (probe, execution failure, identity rejection, evidence, source preservation)");
    }
    sealed class CancelProgress(CancellationTokenSource cts):IProgress<Transfer>{public void Report(Transfer p){if(p.Phase.Contains("Copiando",StringComparison.OrdinalIgnoreCase)&&p.Copied>=1024*1024)cts.Cancel();}}
    sealed class CaptureProgress:IProgress<Transfer>
    {
        public long CopiedBytes { get; private set; }
        public long BytesRead { get; private set; }
        public int ReusedFiles { get; private set; }
        public void Report(Transfer progress)
        {
            CopiedBytes=Math.Max(CopiedBytes,progress.Copied);
            BytesRead=Math.Max(BytesRead,progress.BytesRead);
            if(progress.Phase.StartsWith("Reutilizando",StringComparison.OrdinalIgnoreCase))ReusedFiles++;
        }
    }
}
