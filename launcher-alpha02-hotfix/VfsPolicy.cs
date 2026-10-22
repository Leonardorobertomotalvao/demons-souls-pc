using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DemonsSouls.Launcher;

internal enum VfsFileKind { Protected, ResourceCache, SdatPlaintext, Unknown }
internal static class VfsPolicy
{
    internal static VfsFileKind Classify<T>(string path,IReadOnlyDictionary<string,T> originals)
    {
        path=path.Replace('\\','/');
        if(originals.ContainsKey(path))return VfsFileKind.Protected;
        if(System.Text.RegularExpressions.Regex.IsMatch(path,@"^vfs/cache/[0-9a-f]{2}/[0-9a-f]{32}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))return VfsFileKind.ResourceCache;
        if(path.StartsWith("vfs/PS3_GAME/USRDIR/",StringComparison.OrdinalIgnoreCase)&&
           path.EndsWith(".sdat.dec",StringComparison.OrdinalIgnoreCase)&&originals.ContainsKey(path[..^4]))
            return VfsFileKind.SdatPlaintext;
        return VfsFileKind.Unknown;
    }

    // Same SDAT data transform as ps3recomp/libs/filesystem/edat.c.
    // The encrypted parent must already have passed manifest verification.
    internal static string VerifySdat(string parent,string derived,CancellationToken ct)
    {
        using var input=File.OpenRead(parent);
        using var output=File.OpenRead(derived);
        byte[] header=new byte[256];input.ReadExactly(header);OperationTrace.RecordIo(256,0);
        uint U(int offset)=>BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset,4));
        uint version=U(4),flags=U(128),block=U(132);
        ulong size=BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(136,8));
        if(!header.AsSpan(0,4).SequenceEqual(new byte[]{78,80,68,0})||version>4||
           (flags&0x01000000)==0||(flags&0x80000001)!=0||block==0||block>16*1024*1024||
           size>(ulong)long.MaxValue||output.Length!=(long)size)
            throw new IOException("Derivado SDAT inválido ou formato não suportado: "+derived);
        byte[] key=Convert.FromHexString("0D655EF8E674A98AB8505CFA7D012933");
        for(int i=0;i<16;i++)key[i]^=header[96+i];
        using var aes=Aes.Create();aes.Key=key;
        using var unwrap=Aes.Create();unwrap.Key=Convert.FromHexString(version==4?
            "4CA9C14B01C95309969BEC68AA0BC081":"BE959CA8308DEFA2E5E180C63712A9AE");
        ulong blocks=(size+block-1)/block,meta=(flags&32)!=0?32UL:16UL;
        if(blocks>uint.MaxValue)throw new IOException("SDAT excede o limite de blocos.");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for(uint b=0;b<blocks;b++)
        {
            ct.ThrowIfCancellationRequested();
            int length=(int)Math.Min(block,size-(ulong)b*block),padded=(length+15)&~15;
            ulong offset=(flags&32)!=0?256+(ulong)b*(meta+block)+meta:256+blocks*meta+(ulong)b*block;
            if(offset>(ulong)input.Length||(ulong)padded>(ulong)input.Length-offset)
                throw new IOException("SDAT truncado: "+parent);
            input.Position=(long)offset;byte[] encrypted=new byte[padded];input.ReadExactly(encrypted);
            byte[] seed=new byte[16];if(version>1)header.AsSpan(96,12).CopyTo(seed);
            BinaryPrimitives.WriteUInt32BigEndian(seed.AsSpan(12),b);
            byte[] blockKey=aes.EncryptEcb(seed,PaddingMode.None);
            if((flags&8)!=0)blockKey=unwrap.DecryptCbc(blockKey,new byte[16],PaddingMode.None);
            using var cipher=Aes.Create();cipher.Key=blockKey;
            byte[] plain=cipher.DecryptCbc(encrypted,version>1?header.AsSpan(64,16):new byte[16],PaddingMode.None);
            byte[] actual=new byte[length];output.ReadExactly(actual);
            OperationTrace.RecordIo(padded+length,0);OperationTrace.RecordHash(length);
            if(!CryptographicOperations.FixedTimeEquals(plain.AsSpan(0,length),actual))
                throw new IOException("Integridade do derivado SDAT diverge do original protegido: "+derived);
            hash.AppendData(actual);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
