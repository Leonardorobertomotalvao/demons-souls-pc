using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DemonsSouls.Launcher;

internal static class VfsPolicyTest
{
    internal static (byte[] Source,byte[] Plain) Fixture(uint version=2,uint extraFlags=0)
    {
        byte[] plain=Enumerable.Range(0,37).Select(i=>(byte)(i*7)).ToArray();
        int meta=(extraFlags&32)!=0?32:16;
        byte[] source=new byte[256+2*meta+48];
        new byte[]{78,80,68,0}.CopyTo(source,0);
        void U(int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(source.AsSpan(at,4),value);
        U(4,version);U(128,0x01000000|extraFlags);U(132,32);
        BinaryPrimitives.WriteUInt64BigEndian(source.AsSpan(136),37);
        for(int i=0;i<16;i++){source[64+i]=(byte)(i+1);source[96+i]=(byte)(i+20);}
        byte[] key=Convert.FromHexString("0D655EF8E674A98AB8505CFA7D012933");
        for(int i=0;i<16;i++)key[i]^=source[96+i];
        using var aes=Aes.Create();aes.Key=key;
        using var wrapper=Aes.Create();wrapper.Key=Convert.FromHexString(version==4?
            "4CA9C14B01C95309969BEC68AA0BC081":"BE959CA8308DEFA2E5E180C63712A9AE");
        for(uint b=0;b<2;b++)
        {
            byte[] seed=new byte[16];if(version>1)source.AsSpan(96,12).CopyTo(seed);
            BinaryPrimitives.WriteUInt32BigEndian(seed.AsSpan(12),b);
            byte[] blockKey=aes.EncryptEcb(seed,PaddingMode.None);
            if((extraFlags&8)!=0)blockKey=wrapper.DecryptCbc(blockKey,new byte[16],PaddingMode.None);
            using var block=Aes.Create();block.Key=blockKey;
            byte[] padded=new byte[b==0?32:16];plain.AsSpan((int)b*32,b==0?32:5).CopyTo(padded);
            byte[] data=block.EncryptCbc(padded,version>1?source.AsSpan(64,16):new byte[16],PaddingMode.None);
            int offset=(extraFlags&32)!=0?256+(int)b*(meta+32)+meta:256+2*meta+(int)b*32;
            data.CopyTo(source,offset);
        }
        return (source,plain);
    }
    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        string parent=Path.Combine(directory,"fixture.sdat"),derived=parent+".dec";
        foreach(uint version in new uint[]{0,1,2,3,4})foreach(uint flags in new uint[]{0,8,32,40})
        {
            var fixture=Fixture(version,flags);File.WriteAllBytes(parent,fixture.Source);File.WriteAllBytes(derived,fixture.Plain);
            if(VfsPolicy.VerifySdat(parent,derived,CancellationToken.None)!=Convert.ToHexString(SHA256.HashData(fixture.Plain)))
                throw new Exception("FAIL: SDAT plaintext verification");
            fixture.Plain[0]^=1;File.WriteAllBytes(derived,fixture.Plain);
            bool rejected=false;try{VfsPolicy.VerifySdat(parent,derived,CancellationToken.None);}catch(IOException){rejected=true;}
            if(!rejected)throw new Exception("FAIL: same-size corrupt SDAT derivative accepted");
        }
        var originals=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase){{"vfs/PS3_GAME/USRDIR/chr/a.sdat",1}};
        if(VfsPolicy.Classify("vfs/PS3_GAME/USRDIR/chr/a.sdat.dec",originals)!=VfsFileKind.SdatPlaintext||
           VfsPolicy.Classify("vfs/PS3_GAME/USRDIR/chr/unknown.sdat.dec",originals)!=VfsFileKind.Unknown||
           VfsPolicy.Classify("vfs/PS3_GAME/USRDIR/chr/a.dec",originals)!=VfsFileKind.Unknown||
           VfsPolicy.Classify("vfs/PS3_GAME/USRDIR/chr/a.sdat",originals)!=VfsFileKind.Protected)
            throw new Exception("FAIL: VFS classification");
        if(!MainWindow.FriendlyError("VFS diverge do inventário: vfs/PS3_GAME/extra").Contains("VFS")||
           MainWindow.FriendlyError("Integridade do derivado SDAT: PS3_GAME/a").Contains("localizar"))
            throw new Exception("FAIL: error classification");
        Console.WriteLine("PASS: SDAT versions/layouts, same-size derivative corruption, orphan/unknown rejection and VFS error classification");
    }
}
