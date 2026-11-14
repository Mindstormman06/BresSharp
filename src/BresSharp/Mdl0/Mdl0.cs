using System.Buffers.Binary;

namespace BresSharp.Mdl0;

public sealed class Mdl0Model
{
    public const int SupportedVersion = 11;
    private const int InfoOffset = 0x4C;

    private readonly Dictionary<int, string> _names;

    public Mdl0Model(BresSubfile subfile)
    {
        if (subfile.Magic != "MDL0") throw new InvalidDataException($"'{subfile.Name}' is not an MDL0.");
        Subfile = subfile;
        Version = (int)U32(0x08);
        if (Version != SupportedVersion) throw new NotSupportedException($"MDL0 version {Version} is not supported (only {SupportedVersion}).");

        _names = subfile.StringRefs.ToDictionary(r => r.Position, r => r.Value);

        Definitions = [.. Group(0).Select(e => new Mdl0Definition(e.Name, this, e.At))];
        Bones = [.. Group(1).Select((e, i) => new Mdl0Bone(this, e.At))];
        Positions = [.. Group(2).Select(e => new Mdl0VertexArray(this, e.At, Mdl0ArrayKind.Position))];
        Normals = [.. Group(3).Select(e => new Mdl0VertexArray(this, e.At, Mdl0ArrayKind.Normal))];
        Colors = [.. Group(4).Select(e => new Mdl0ColorArray(this, e.At))];
        TexCoords = [.. Group(5).Select(e => new Mdl0VertexArray(this, e.At, Mdl0ArrayKind.TexCoord))];
        Materials = [.. Group(8).Select(e => new Mdl0Material(this, e.At))];
        Shaders = [.. Group(9).Select(e => new Mdl0Shader(this, e.At))];
        Shapes = [.. Group(10).Select(e => new Mdl0Shape(this, e.At))];

        Dictionary<int, Mdl0Bone> bonesAt = Bones.ToDictionary(b => b.Offset);
        foreach (Mdl0Bone bone in Bones) bone.Link(bonesAt);
    }

    public BresSubfile Subfile { get; }

    public byte[] Data => Subfile.Data;

    public string Name => Subfile.Name;

    public int Version { get; }

    public IReadOnlyList<Mdl0Definition> Definitions { get; }
    public IReadOnlyList<Mdl0Bone> Bones { get; }
    public IReadOnlyList<Mdl0VertexArray> Positions { get; }
    public IReadOnlyList<Mdl0VertexArray> Normals { get; }
    public IReadOnlyList<Mdl0ColorArray> Colors { get; }
    public IReadOnlyList<Mdl0VertexArray> TexCoords { get; }
    public IReadOnlyList<Mdl0Material> Materials { get; }
    public IReadOnlyList<Mdl0Shader> Shaders { get; }
    public IReadOnlyList<Mdl0Shape> Shapes { get; }

    public uint ScalingMode => U32(InfoOffset + 0x08);
    public uint TextureMatrixMode => U32(InfoOffset + 0x0C);
    public int VertexCount => I32(InfoOffset + 0x10);
    public int FaceCount => I32(InfoOffset + 0x14);
    public uint MatrixCount => U32(InfoOffset + 0x1C);
    public bool NeedsNormalMatrices => Data[InfoOffset + 0x20] != 0;
    public bool NeedsTextureMatrices => Data[InfoOffset + 0x21] != 0;
    public bool BoundsEnabled => Data[InfoOffset + 0x22] != 0;
    public System.Numerics.Vector3 BoundsMin => Vec3(InfoOffset + 0x28);
    public System.Numerics.Vector3 BoundsMax => Vec3(InfoOffset + 0x34);

    public IReadOnlyList<int> MatrixToBone
    {
        get
        {
            int table = InfoOffset + I32(InfoOffset + 0x24);
            int count = (int)U32(table);
            int[] map = new int[count];
            for (int i = 0; i < count; i++) map[i] = I32(table + 4 + i * 4);
            return map;
        }
    }

    public override string ToString() => $"{Name}: {Bones.Count} bones, {Materials.Count} materials, {Shapes.Count} shapes";

    internal string? NameAt(int position) => _names.GetValueOrDefault(position);

    internal IEnumerable<(string Name, int At)> Group(int section)
    {
        int at = I32(0x10 + section * 4);
        if (at <= 0) yield break;
        int count = (int)U32(at + 4);
        if (count > 0x4000 || U32(at) != 8 + (count + 1) * 0x10) throw new InvalidDataException($"MDL0 section {section} is not an index group.");
        for (int i = 1; i <= count; i++)
        {
            int e = at + 8 + i * 0x10;
            yield return (NameAt(e + 8) ?? $"#{i - 1}", at + I32(e + 12));
        }
    }

    internal uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(at));
    internal int I32(int at) => BinaryPrimitives.ReadInt32BigEndian(Data.AsSpan(at));
    internal ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(at));
    internal float F32(int at) => BinaryPrimitives.ReadSingleBigEndian(Data.AsSpan(at));
    internal void SetF32(int at, float value) => BinaryPrimitives.WriteSingleBigEndian(Data.AsSpan(at), value);
    internal void SetU32(int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(Data.AsSpan(at), value);
    internal System.Numerics.Vector3 Vec3(int at) => new(F32(at), F32(at + 4), F32(at + 8));

    // A stored 3x4 matrix (three rows of four floats) as a row-vector Matrix4x4.
    internal System.Numerics.Matrix4x4 Matrix3x4(int at) => new(
        F32(at), F32(at + 16), F32(at + 32), 0,
        F32(at + 4), F32(at + 20), F32(at + 36), 0,
        F32(at + 8), F32(at + 24), F32(at + 40), 0,
        F32(at + 12), F32(at + 28), F32(at + 44), 1);

    internal void SetVec3(int at, System.Numerics.Vector3 v)
    {
        SetF32(at, v.X);
        SetF32(at + 4, v.Y);
        SetF32(at + 8, v.Z);
    }
}

public sealed class Mdl0Definition(string name, Mdl0Model model, int at)
{
    public string Name { get; } = name;

    public int Offset => at;

    public IEnumerable<Mdl0DefinitionCommand> Commands
    {
        get
        {
            byte[] d = model.Data;
            int p = at;
            while (p < d.Length)
            {
                byte op = d[p];
                int length = op switch
                {
                    0x00 or 0x01 => 1,
                    0x02 or 0x05 or 0x06 => 5,
                    0x03 => 4 + 6 * d[p + 3],
                    0x04 => 8,
                    _ => throw new InvalidDataException($"Unknown MDL0 definition command 0x{op:X2} in '{Name}' at 0x{p:X}."),
                };
                yield return new Mdl0DefinitionCommand(op, d.AsSpan(p + 1, length - 1).ToArray());
                if (op == 0x01) yield break;
                p += length;
            }
        }
    }
}

public readonly record struct Mdl0DefinitionCommand(byte Op, byte[] Args)
{
    public override string ToString() => $"{Op:X2} {Convert.ToHexString(Args)}";
}
