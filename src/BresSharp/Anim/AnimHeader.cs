using System.Buffers.Binary;

namespace BresSharp.Anim;

internal static class AnimHeader
{
    public static byte[] Open(BresSubfile subfile, string magic, int version, out Dictionary<int, string> names)
    {
        if (subfile.Magic != magic) throw new InvalidDataException($"'{subfile.Name}' is not a {magic}.");
        byte[] d = subfile.Data;
        int actual = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x08));
        if (actual != version) throw new NotSupportedException($"{magic} version {actual} is not supported (only {version}).");
        names = subfile.StringRefs.ToDictionary(r => r.Position, r => r.Value);
        return d;
    }

    public static ushort U16(byte[] d, int at) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
    public static uint U32(byte[] d, int at) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
    public static int I32(byte[] d, int at) => BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at));
    public static float F32(byte[] d, int at) => BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at));
}
