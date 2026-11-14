using System.Buffers.Binary;
using System.Numerics;

namespace BresSharp.Mdl0;

public enum Mdl0ArrayKind { Position, Normal, TexCoord }

public enum Mdl0ComponentType : uint { U8, S8, U16, S16, Float }

public enum Mdl0ColorFormat : uint { Rgb565, Rgb8, Rgbx8, Rgba4, Rgba6, Rgba8 }

public sealed class Mdl0VertexArray
{
    private readonly Mdl0Model _model;

    internal Mdl0VertexArray(Mdl0Model model, int at, Mdl0ArrayKind kind)
    {
        _model = model;
        Offset = at;
        Kind = kind;
    }

    public int Offset { get; }
    public Mdl0ArrayKind Kind { get; }

    public string Name => _model.NameAt(Offset + 0x0C) ?? $"{Kind}{Index}";
    public int Index => _model.I32(Offset + 0x10);
    public uint ComponentCount => _model.U32(Offset + 0x14);
    public Mdl0ComponentType Type => (Mdl0ComponentType)_model.U32(Offset + 0x18);
    public int Divisor => _model.Data[Offset + 0x1C];
    public int Stride => _model.Data[Offset + 0x1D];
    public int Count => _model.U16(Offset + 0x1E);
    public int DataOffset => Offset + _model.I32(Offset + 0x08);

    public int Components => Kind switch
    {
        Mdl0ArrayKind.Position => ComponentCount == 0 ? 2 : 3,
        Mdl0ArrayKind.Normal => ComponentCount == 1 ? 9 : 3,
        _ => ComponentCount == 0 ? 1 : 2,
    };

    public Vector3 BoundsMin => Kind == Mdl0ArrayKind.Position ? _model.Vec3(Offset + 0x20) : default;
    public Vector3 BoundsMax => Kind == Mdl0ArrayKind.Position ? _model.Vec3(Offset + 0x2C) : default;

    public float[] Read(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        float[] values = new float[Components];
        int size = ComponentSize;
        int at = DataOffset + index * Stride;
        float scale = Type == Mdl0ComponentType.Float ? 1 : 1f / (1 << Divisor);
        for (int c = 0; c < values.Length; c++) values[c] = ReadComponent(at + c * size) * scale;
        return values;
    }

    public Vector3 ReadVector3(int index)
    {
        float[] v = Read(index);
        return new Vector3(v[0], v.Length > 1 ? v[1] : 0, v.Length > 2 ? v[2] : 0);
    }

    public Vector2 ReadVector2(int index)
    {
        float[] v = Read(index);
        return new Vector2(v[0], v.Length > 1 ? v[1] : 0);
    }

    public IEnumerable<Vector3> AllVector3() => Enumerable.Range(0, Count).Select(ReadVector3);

    public IEnumerable<Vector2> AllVector2() => Enumerable.Range(0, Count).Select(ReadVector2);

    private int ComponentSize => Type switch
    {
        Mdl0ComponentType.U8 or Mdl0ComponentType.S8 => 1,
        Mdl0ComponentType.U16 or Mdl0ComponentType.S16 => 2,
        _ => 4,
    };

    private float ReadComponent(int at)
    {
        byte[] d = _model.Data;
        return Type switch
        {
            Mdl0ComponentType.U8 => d[at],
            Mdl0ComponentType.S8 => (sbyte)d[at],
            Mdl0ComponentType.U16 => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at)),
            Mdl0ComponentType.S16 => BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(at)),
            _ => BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at)),
        };
    }

    public override string ToString() => $"{Name} ({Kind}, {Count} x {Components} {Type})";
}

public sealed class Mdl0ColorArray
{
    private readonly Mdl0Model _model;

    internal Mdl0ColorArray(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }

    public string Name => _model.NameAt(Offset + 0x0C) ?? $"Color{Index}";
    public int Index => _model.I32(Offset + 0x10);
    public bool HasAlpha => _model.U32(Offset + 0x14) == 1;
    public Mdl0ColorFormat Format => (Mdl0ColorFormat)_model.U32(Offset + 0x18);
    public int Stride => _model.Data[Offset + 0x1C];
    public int Count => _model.U16(Offset + 0x1E);
    public int DataOffset => Offset + _model.I32(Offset + 0x08);

    public (byte R, byte G, byte B, byte A) Read(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        byte[] d = _model.Data;
        int at = DataOffset + index * Stride;
        switch (Format)
        {
            case Mdl0ColorFormat.Rgb565:
            {
                int v = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
                return (Expand(v >> 11, 5), Expand((v >> 5) & 0x3F, 6), Expand(v & 0x1F, 5), 0xFF);
            }
            case Mdl0ColorFormat.Rgb8 or Mdl0ColorFormat.Rgbx8:
                return (d[at], d[at + 1], d[at + 2], 0xFF);
            case Mdl0ColorFormat.Rgba4:
            {
                int v = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
                return (Expand(v >> 12, 4), Expand((v >> 8) & 0xF, 4), Expand((v >> 4) & 0xF, 4), Expand(v & 0xF, 4));
            }
            case Mdl0ColorFormat.Rgba6:
            {
                int v = d[at] << 16 | d[at + 1] << 8 | d[at + 2];
                return (Expand(v >> 18, 6), Expand((v >> 12) & 0x3F, 6), Expand((v >> 6) & 0x3F, 6), Expand(v & 0x3F, 6));
            }
            default:
                return (d[at], d[at + 1], d[at + 2], d[at + 3]);
        }
    }

    private static byte Expand(int value, int bits) => (byte)(value * 255 / ((1 << bits) - 1));

    public override string ToString() => $"{Name} ({Count} x {Format})";
}
