using System.Buffers.Binary;

namespace BresSharp;

public sealed class Tex0Texture
{
    public Tex0Texture(BresSubfile subfile)
    {
        if (subfile.Magic != "TEX0") throw new InvalidDataException($"'{subfile.Name}' is not a TEX0.");
        Subfile = subfile;
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    private byte[] D => Subfile.Data;

    public bool IsPaletted => (U32(0x18) & 1) != 0;
    public int Width => BinaryPrimitives.ReadUInt16BigEndian(D.AsSpan(0x1C));
    public int Height => BinaryPrimitives.ReadUInt16BigEndian(D.AsSpan(0x1E));
    public uint GxFormat => U32(0x20);
    public int LevelCount => (int)Math.Max(1, U32(0x24));
    public float MinLod => BinaryPrimitives.ReadSingleBigEndian(D.AsSpan(0x28));
    public float MaxLod => BinaryPrimitives.ReadSingleBigEndian(D.AsSpan(0x2C));
    public bool UsesXtx => Subfile.UsesXtx;

    public ReadOnlySpan<byte> Data
    {
        get
        {
            int offset = (int)U32(0x10);
            return offset > 0 && offset < D.Length ? D.AsSpan(offset) : [];
        }
    }

    private uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(D.AsSpan(at));

    public override string ToString() => UsesXtx ? $"{Name} (XTX)" : $"{Name} {Width}x{Height} GX {GxFormat} ({LevelCount} levels)";
}

public sealed class Plt0Palette
{
    public Plt0Palette(BresSubfile subfile)
    {
        if (subfile.Magic != "PLT0") throw new InvalidDataException($"'{subfile.Name}' is not a PLT0.");
        Subfile = subfile;
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;

    public uint GxPaletteFormat => BinaryPrimitives.ReadUInt32BigEndian(Subfile.Data.AsSpan(0x18));
    public int Count => BinaryPrimitives.ReadUInt16BigEndian(Subfile.Data.AsSpan(0x1C));

    public ReadOnlySpan<byte> Data
    {
        get
        {
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(Subfile.Data.AsSpan(0x10));
            return Subfile.Data.AsSpan(offset, Math.Min(Count * 2, Subfile.Data.Length - offset));
        }
    }

    public override string ToString() => $"{Name} ({Count} colours, format {GxPaletteFormat})";
}
