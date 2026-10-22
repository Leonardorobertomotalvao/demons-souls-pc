using System.Buffers.Binary;
using System.Text;

namespace DemonsSouls.Launcher;

public static class SelfTest
{
    static void Assert(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);}
    static async Task Reject(Func<Task> action,string message){try{await action();}catch(IOException){Console.WriteLine("PASS rejection: "+message);return;}throw new Exception("FAIL accepted: "+message);}
    public static async Task Run()
    {
        string settingsFixture=Path.Combine(Path.GetTempPath(),"DemonsLauncher-settings-"+Guid.NewGuid().ToString("N")+".json");
        var launcherWindow=new MainWindow();
        foreach(var (resolution,width,height) in new[]{("1280x720","1280","720"),("1600x900","1600","900"),("1920x1080","1920","1080")})
        foreach(var (mode,borderless) in new[]{("windowed","0"),("borderless","1"),("fullscreen","1")})
        {
            launcherWindow.SelectVideoOptionsForTest(resolution,mode,settingsFixture);
            var reloaded=Preferences.Read(settingsFixture);
            Assert(reloaded.WindowResolution==resolution&&reloaded.DisplayMode==mode&&!string.IsNullOrWhiteSpace(reloaded.MonitorDevice),"launcher ComboBox selection and saved preferences");
            var video=RuntimeService.VideoSettings(reloaded);
            Assert(video["RSX_WINDOW_W"]==width&&video["RSX_WINDOW_H"]==height&&video["RSX_BORDERLESS"]==borderless&&video["RSX_DISPLAY_MODE"]==mode&&video["RSX_MONITOR_DEVICE"]==reloaded.MonitorDevice,"video setting to runtime environment mapping");
        }
        launcherWindow.Close();File.Delete(settingsFixture);
        var monitors=DisplayMonitorService.Enumerate();
        Assert(monitors.Count>0,"enumerate connected display monitors");
        Console.WriteLine("PASS: real launcher ComboBox selections, settings save/reload, runtime mapping, and monitor enumeration");
        string test=Path.Combine(Path.GetTempPath(),"DemonsLauncher-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(test);
        string source=Path.Combine(test,"source/PS3_GAME"),dest=Path.Combine(test,"installed");Directory.CreateDirectory(source);
        string[] keys={"TITLE_ID","APP_VER","VERSION"},values={"BLUS30443","01.00","01.00"};
        byte[] keyBytes=Encoding.UTF8.GetBytes(string.Join('\0',keys)+'\0'),valueBytes=Encoding.UTF8.GetBytes(string.Join('\0',values)+'\0');
        byte[] sfo=new byte[68+keyBytes.Length+valueBytes.Length];sfo[1]=80;sfo[2]=83;sfo[3]=70;
        void U(int p,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(p,4),n);
        U(8,68);U(12,(uint)(68+keyBytes.Length));U(16,3);int ko=0,vo=0;
        for(int i=0;i<3;i++){int p=20+i*16;BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(p,2),(ushort)ko);BinaryPrimitives.WriteUInt16LittleEndian(sfo.AsSpan(p+2,2),0x204);U(p+4,(uint)values[i].Length+1);U(p+8,(uint)values[i].Length+1);U(p+12,(uint)vo);ko+=keys[i].Length+1;vo+=values[i].Length+1;}
        keyBytes.CopyTo(sfo,68);valueBytes.CopyTo(sfo,68+keyBytes.Length);File.WriteAllBytes(Path.Combine(source,"PARAM.SFO"),sfo);
        foreach(var folder in new[]{"map","chr","obj","shader","sound","menu","script"})Directory.CreateDirectory(Path.Combine(source,"USRDIR",folder));
        File.WriteAllText(Path.Combine(source,"USRDIR/EBOOT.BIN"),"synthetic, not a game executable");
        byte[] payload=new byte[3*1024*1024];new Random(7).NextBytes(payload);string asset=Path.Combine(source,"USRDIR/map/synthetic.asset");File.WriteAllBytes(asset,payload);
        string elf=Path.Combine(test,"test.elf"),runtime=Path.Combine(test,"test.exe");byte[] header=new byte[64];new byte[]{127,69,76,70,2,2}.CopyTo(header,0);header[17]=2;header[19]=21;File.WriteAllBytes(elf,header);File.WriteAllBytes(runtime,new byte[2048]);
        using var cts=new CancellationTokenSource();var progress=new CancelProgress(cts);
        await Reject(()=>Task.Run(()=>Installation.CheckElf(elf)),"ELF header without supported build identity");
        bool cancelled=false;try{await Installation.Import(source,dest,elf,runtime,progress,cts.Token,true);}catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled&&!Installation.Load(dest).Complete,"cancel leaves incomplete installation");
        await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true);await Installation.Verify(dest,null,CancellationToken.None,fixture:true);
        Console.WriteLine("PASS: cancelled mid-copy, resumed with verified files, full SHA256 integrity");
        var fastVerification=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(fastVerification.UsedMetadataFastPath,"unchanged installation uses metadata-only launch verification");
        string installed=Installation.Child(dest,"vfs/PS3_GAME/USRDIR/map/synthetic.asset");
        File.SetLastWriteTimeUtc(installed,File.GetLastWriteTimeUtc(installed).AddSeconds(2));
        var changedMetadata=await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true);
        Assert(!changedMetadata.UsedMetadataFastPath,"changed file timestamp triggers full-hash fallback");
        Assert((await Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true)).UsedMetadataFastPath,"successful fallback refreshes the metadata snapshot");
        Console.WriteLine("PASS: unchanged launch uses fast metadata check; changed metadata forces SHA-256 validation");
        payload[0]^=255;File.WriteAllBytes(installed,payload);
        await Reject(()=>Installation.VerifyForLaunch(dest,null,CancellationToken.None,fixture:true),"modified file detected by full-hash fallback");
        await Reject(()=>Installation.Verify(dest,null,CancellationToken.None,fixture:true),"same-size corruption");
        await Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true);await Installation.Verify(dest,null,CancellationToken.None,fixture:true);
        Assert(Directory.GetFiles(Path.GetDirectoryName(installed)!,"*.corrupt-*").Length==1,"repair preserves corrupt copy");
        Console.WriteLine("PASS: repair and quarantine");
        await Reject(()=>Task.Run(()=>Installation.Child(dest,"../escape")),"path traversal");
        await Reject(()=>Task.Run(()=>Installation.Child(dest,"vfs/file:ads")),"ADS");
        await Reject(()=>Task.Run(()=>Installation.Child(dest,"vfs/CON.txt")),"device path");
        string bad=Path.Combine(test,"bad.sfo");File.WriteAllBytes(bad,new byte[20]);await Reject(()=>Task.Run(()=>Installation.Sfo(bad)),"invalid SFO");
        await Reject(()=>Task.Run(()=>Installation.GameRoot(Path.Combine(test,"missing"))),"absent media");
        await Reject(()=>Discovery.MountedIso(Path.Combine(test,"missing.iso")),"absent ISO");
        await Reject(()=>Task.Run(()=>Installation.CheckSpace(10,100)),"insufficient space");
        await Reject(()=>Task.Run(()=>RuntimeService.Start(dest,new Preferences(),new LaunchVerification(false,0,0))),"synthetic installation cannot launch");
        using(var lease=Installation.TakeLease(dest))await Reject(()=>Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true),"simultaneous import/launch lease");
        string originalHash=Installation.Load(dest).Files.First(e=>e.Path.EndsWith("synthetic.asset")).Sha256;
        byte[] changed=File.ReadAllBytes(asset);changed[0]^=1;File.WriteAllBytes(asset,changed);
        await Reject(()=>Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true),"changed source on resume");
        Assert(Installation.Load(dest).Files.First(e=>e.Path.EndsWith("synthetic.asset")).Sha256==originalHash,"prior hashes preserved after rejected resume");
        await Reject(()=>Installation.Import(source,dest,elf,runtime,null,CancellationToken.None,true),"changed source remains rejected on second retry");
        string outside=Path.Combine(test,"outside"),link=Path.Combine(source,"USRDIR/map/link");Directory.CreateDirectory(outside);
        var cmd=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        cmd.ArgumentList.Add("/c");cmd.ArgumentList.Add("mklink");cmd.ArgumentList.Add("/J");cmd.ArgumentList.Add(link.Replace('/','\\'));cmd.ArgumentList.Add(outside.Replace('/','\\'));
        using(var p=System.Diagnostics.Process.Start(cmd)!){await p.WaitForExitAsync();Assert(p.ExitCode==0,"create fixture junction without admin: "+await p.StandardError.ReadToEndAsync());}
        await Reject(()=>Task.Run(()=>Installation.Inventory(source)),"source descendant junction");
        await Reject(()=>Task.Run(()=>Installation.Child(link,"escape")),"destination ancestor junction");
        Console.WriteLine("SELF_TEST_PASS "+test);
    }
    sealed class CancelProgress(CancellationTokenSource cts):IProgress<Transfer>{public void Report(Transfer p){if(p.Phase=="Copiando"&&p.Copied>=1024*1024)cts.Cancel();}}
}
