using System.Buffers.Binary;

namespace BresSharp.Mdl0;

public enum GxPrimitive : byte
{
    Quads = 0x80,
    Triangles = 0x90,
    TriangleStrip = 0x98,
    TriangleFan = 0xA0,
    Lines = 0xA8,
    LineStrip = 0xB0,
    Points = 0xB8,
}

public sealed class Mdl0Vertex
{
    public int MatrixIndex { get; init; } = -1;
    public int[] TexMatrixIndex { get; } = [-1, -1, -1, -1, -1, -1, -1, -1];
    public int Position { get; init; } = -1;
    public int Normal { get; init; } = -1;
    public int Color0 { get; init; } = -1;
    public int Color1 { get; init; } = -1;
    public int[] TexCoord { get; } = [-1, -1, -1, -1, -1, -1, -1, -1];
}

public sealed record Mdl0Primitive(GxPrimitive Type, IReadOnlyList<Mdl0Vertex> Vertices, IReadOnlyList<(byte Op, ushort Index, ushort Address)> MatrixLoads);

public sealed class Mdl0Shape
{
    private readonly Mdl0Model _model;

    internal Mdl0Shape(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }

    public string Name => _model.NameAt(Offset + 0x38) ?? $"shape{Index}";
    public int BoneIndex => _model.I32(Offset + 0x08);
    public uint VertexDescriptorLow => _model.U32(Offset + 0x0C);
    public uint VertexDescriptorHigh => _model.U32(Offset + 0x10);
    public int Index => _model.I32(Offset + 0x3C);
    public int VertexCount => _model.I32(Offset + 0x40);
    public int FaceCount => _model.I32(Offset + 0x44);
    public int PositionArray => (short)_model.U16(Offset + 0x48);
    public int NormalArray => (short)_model.U16(Offset + 0x4A);
    public int ColorArray(int channel) => (short)_model.U16(Offset + 0x4C + channel * 2);
    public int TexCoordArray(int set) => (short)_model.U16(Offset + 0x50 + set * 2);

    public IReadOnlyList<int> BoneTable
    {
        get
        {
            int offset = _model.I32(Offset + 0x64);
            if (offset == 0) return [];
            int at = Offset + offset;
            int count = (int)_model.U32(at);
            if (count > 0x400) throw new InvalidDataException($"Shape '{Name}' has an invalid bone table count {count}.");
            return [.. Enumerable.Range(0, count).Select(i => (int)_model.U16(at + 4 + i * 2))];
        }
    }

    public int PrimitiveListOffset => Offset + 0x24 + _model.I32(Offset + 0x2C);
    public int PrimitiveListSize => _model.I32(Offset + 0x28);

    public IEnumerable<Mdl0Primitive> Primitives
    {
        get
        {
            byte[] d = _model.Data;
            uint lo = VertexDescriptorLow, hi = VertexDescriptorHigh;
            int p = PrimitiveListOffset, end = Math.Min(d.Length, p + PrimitiveListSize);
            List<(byte, ushort, ushort)> loads = [];

            while (p < end)
            {
                byte op = d[p];
                if (op == 0x00) { p++; continue; }
                if (op == 0x08) { p += 6; continue; }
                if (op == 0x10) { p += 5 + 4 * (BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 1)) + 1); continue; }
                if (op is 0x20 or 0x28 or 0x30 or 0x38)
                {
                    loads.Add((op, BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 1)), BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 3))));
                    p += 5;
                    continue;
                }
                if ((op & 0x80) == 0 || (op & 0xF8) == 0x88)
                    throw new InvalidDataException($"Shape '{Name}' has an unknown display list command 0x{op:X2} at 0x{p:X}.");

                GxPrimitive type = (GxPrimitive)(op & 0xF8);
                int count = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 1));
                p += 3;
                List<Mdl0Vertex> vertices = new(count);
                for (int v = 0; v < count; v++)
                {
                    int matrix = -1;
                    if ((lo & 1) != 0) matrix = d[p++];
                    int[] texMatrix = new int[8];
                    for (int t = 0; t < 8; t++) texMatrix[t] = (lo >> (1 + t) & 1) != 0 ? d[p++] : -1;

                    int position = ReadIndex(d, ref p, (int)(lo >> 9) & 3);
                    int normal = ReadIndex(d, ref p, (int)(lo >> 11) & 3);
                    int color0 = ReadIndex(d, ref p, (int)(lo >> 13) & 3);
                    int color1 = ReadIndex(d, ref p, (int)(lo >> 15) & 3);
                    Mdl0Vertex vertex = new() { MatrixIndex = matrix, Position = position, Normal = normal, Color0 = color0, Color1 = color1 };
                    texMatrix.CopyTo(vertex.TexMatrixIndex, 0);
                    for (int t = 0; t < 8; t++) vertex.TexCoord[t] = ReadIndex(d, ref p, (int)(hi >> (t * 2)) & 3);
                    vertices.Add(vertex);
                }
                yield return new Mdl0Primitive(type, vertices, [.. loads]);
                loads.Clear();
            }
        }
    }

    public IEnumerable<(Mdl0Vertex A, Mdl0Vertex B, Mdl0Vertex C)> Triangles()
    {
        foreach (Mdl0Primitive prim in Primitives)
        {
            IReadOnlyList<Mdl0Vertex> v = prim.Vertices;
            switch (prim.Type)
            {
                case GxPrimitive.Triangles:
                    for (int i = 0; i + 2 < v.Count; i += 3) yield return (v[i], v[i + 1], v[i + 2]);
                    break;
                case GxPrimitive.TriangleStrip:
                    for (int i = 0; i + 2 < v.Count; i++)
                        yield return i % 2 == 0 ? (v[i], v[i + 1], v[i + 2]) : (v[i + 1], v[i], v[i + 2]);
                    break;
                case GxPrimitive.TriangleFan:
                    for (int i = 1; i + 1 < v.Count; i++) yield return (v[0], v[i], v[i + 1]);
                    break;
                case GxPrimitive.Quads:
                    for (int i = 0; i + 3 < v.Count; i += 4)
                    {
                        yield return (v[i], v[i + 1], v[i + 2]);
                        yield return (v[i], v[i + 2], v[i + 3]);
                    }
                    break;
            }
        }
    }

    private int ReadIndex(byte[] d, ref int p, int mode)
    {
        switch (mode)
        {
            case 0: return -1;
            case 2: return d[p++];
            case 3:
                int value = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p));
                p += 2;
                return value;
            default: throw new NotSupportedException($"Shape '{Name}' uses direct vertex attributes.");
        }
    }

    public override string ToString() => $"{Name} ({VertexCount} vertices, {FaceCount} faces)";
}
