using System;
using System.IO;
using System.Text;

public static class UnityFsDump
{
    static uint ReadU32BE(BinaryReader r) { var b = r.ReadBytes(4); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    static long ReadI64BE(BinaryReader r) { var b = r.ReadBytes(8); long v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | b[i]; return v; }
    static ushort ReadU16BE(BinaryReader r) { var b = r.ReadBytes(2); return (ushort)(b[0] << 8 | b[1]); }
    static string ReadCStr(BinaryReader r) { var sb = new StringBuilder(); byte c; while ((c = r.ReadByte()) != 0) sb.Append((char)c); return sb.ToString(); }

    public static byte[] Lz4Decode(byte[] src, int dstLen)
    {
        var dst = new byte[dstLen];
        int si = 0, di = 0;
        while (si < src.Length)
        {
            int token = src[si++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[si++]; lit += b; } while (b == 255); }
            Buffer.BlockCopy(src, si, dst, di, lit); si += lit; di += lit;
            if (si >= src.Length) break;
            int off = src[si] | (src[si + 1] << 8); si += 2;
            int ml = token & 15;
            if (ml == 15) { int b; do { b = src[si++]; ml += b; } while (b == 255); }
            ml += 4;
            int from = di - off;
            for (int k = 0; k < ml; k++) dst[di++] = dst[from + k];
        }
        return dst;
    }

    public static string Run(string path, string outPath)
    {
        var log = new StringBuilder();
        using (var fs = File.OpenRead(path))
        using (var r = new BinaryReader(fs))
        {
            string sig = ReadCStr(r);
            uint ver = ReadU32BE(r);
            string uv = ReadCStr(r);
            string rev = ReadCStr(r);
            long size = ReadI64BE(r);
            uint cbi = ReadU32BE(r);
            uint ubi = ReadU32BE(r);
            uint flags = ReadU32BE(r);
            log.AppendLine($"{sig} v{ver} {uv} {rev} size={size} cbi={cbi} ubi={ubi} flags=0x{flags:X}");
            if (ver >= 7) { long pos = fs.Position; long aligned = (pos + 15) & ~15L; fs.Position = aligned; }
            byte[] blocksInfoBytes;
            long dataStart;
            if ((flags & 0x80) != 0)
            {
                long cur = fs.Position;
                fs.Position = fs.Length - cbi;
                blocksInfoBytes = r.ReadBytes((int)cbi);
                fs.Position = cur;
                dataStart = cur;
            }
            else
            {
                blocksInfoBytes = r.ReadBytes((int)cbi);
                dataStart = fs.Position;
            }
            int comp = (int)(flags & 0x3F);
            byte[] bi = comp == 0 ? blocksInfoBytes : Lz4Decode(blocksInfoBytes, (int)ubi);
            using (var br = new BinaryReader(new MemoryStream(bi)))
            {
                br.ReadBytes(16);
                int blockCount = (int)ReadU32BE(br);
                var usz = new uint[blockCount]; var csz = new uint[blockCount]; var bfl = new ushort[blockCount];
                for (int i = 0; i < blockCount; i++) { usz[i] = ReadU32BE(br); csz[i] = ReadU32BE(br); bfl[i] = ReadU16BE(br); }
                int nodeCount = (int)ReadU32BE(br);
                for (int i = 0; i < nodeCount; i++)
                {
                    long off = ReadI64BE(br); long sz = ReadI64BE(br); uint f = ReadU32BE(br); string name = ReadCStr(br);
                    log.AppendLine($"node {name} off={off} size={sz} flags={f}");
                }
                if ((flags & 0x200) != 0) { long p = fs.Position; }
                fs.Position = dataStart;
                if ((flags & 0x200) != 0) { fs.Position = (fs.Position + 15) & ~15L; }
                using (var outFs = File.Create(outPath))
                {
                    for (int i = 0; i < blockCount; i++)
                    {
                        var data = r.ReadBytes((int)csz[i]);
                        int bc = bfl[i] & 0x3F;
                        byte[] dec = bc == 0 ? data : (bc == 2 || bc == 3 ? Lz4Decode(data, (int)usz[i]) : null);
                        if (dec == null) { log.AppendLine($"block {i}: compression {bc} unsupported"); return log.ToString(); }
                        outFs.Write(dec, 0, dec.Length);
                    }
                }
                log.AppendLine($"blocks={blockCount} written");
            }
        }
        return log.ToString();
    }
}
