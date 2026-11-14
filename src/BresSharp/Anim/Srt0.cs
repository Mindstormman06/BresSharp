using System.Buffers.Binary;
using System.Numerics;

namespace BresSharp.Anim;

public sealed class Srt0Animation
{
    public const int SupportedVersion = 5;

    public Srt0Animation(BresSubfile subfile)
    {
        if (subfile.Magic != "SRT0") throw new InvalidDataException($"'{subfile.Name}' is not an SRT0.");
        Subfile = subfile;
        byte[] d = subfile.Data;
        Version = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x08));
        if (Version != SupportedVersion) throw new NotSupportedException($"SRT0 version {Version} is not supported (only {SupportedVersion}).");

        FrameCount = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x20));
        MatrixMode = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x24));
        Loop = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x28)) != 0;

        Dictionary<int, string> names = subfile.StringRefs.ToDictionary(r => r.Position, r => r.Value);
        int group = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(0x10));
        Materials = [.. AnimGroup.Entries(d, group).Select(at => new Srt0Material(d, at, names.GetValueOrDefault(at) ?? "", FrameCount))];
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int Version { get; }
    public int FrameCount { get; }
    public uint MatrixMode { get; }
    public bool Loop { get; }
    public IReadOnlyList<Srt0Material> Materials { get; }

    public override string ToString() => $"{Name}: {FrameCount} frames, {Materials.Count} materials";
}

public sealed class Srt0Material
{
    internal Srt0Material(byte[] d, int at, string name, int frameCount)
    {
        Offset = at;
        Name = name;
        TextureMask = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 4));
        IndirectMask = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 8));

        List<Srt0Matrix> matrices = [];
        int word = at + 12;
        for (int i = 0; i < 8; i++)
            if ((TextureMask >> i & 1) != 0) { matrices.Add(new Srt0Matrix(d, at + BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(word)), i, false, frameCount)); word += 4; }
        for (int i = 0; i < 3; i++)
            if ((IndirectMask >> i & 1) != 0) { matrices.Add(new Srt0Matrix(d, at + BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(word)), i, true, frameCount)); word += 4; }
        Matrices = matrices;
        End = word;
    }

    public int Offset { get; }
    public int End { get; }
    public string Name { get; }
    public uint TextureMask { get; }
    public uint IndirectMask { get; }
    public IReadOnlyList<Srt0Matrix> Matrices { get; }

    public override string ToString() => $"{Name} ({Matrices.Count} matrices)";
}

public sealed class Srt0Matrix
{
    internal Srt0Matrix(byte[] d, int at, int index, bool indirect, int frameCount)
    {
        Offset = at;
        Index = index;
        IsIndirect = indirect;
        Code = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        int word = at + 4;

        AnimTrack Next(int fixedBit)
        {
            AnimTrack t = (Code >> fixedBit & 1) != 0
                ? AnimTrack.Fixed(BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(word)))
                : AnimTrack.Read(d, word + BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(word)), AnimFormat.Interpolated12, frameCount + 1);
            word += 4;
            return t;
        }

        if (!ScaleOne)
        {
            AnimTrack u = Next(5);
            ScaleTracks = ScaleIsotropic ? [u, u] : [u, Next(6)];
        }
        if (!RotationZero) RotationTrack = Next(7);
        if (!TranslationZero) TranslationTracks = [Next(8), Next(9)];
        End = word;
    }

    public int Offset { get; }
    public int End { get; }
    public int Index { get; }
    public bool IsIndirect { get; }
    public uint Code { get; }

    public bool ScaleOne => (Code & 1 << 1) != 0;
    public bool RotationZero => (Code & 1 << 2) != 0;
    public bool TranslationZero => (Code & 1 << 3) != 0;
    public bool ScaleIsotropic => (Code & 1 << 4) != 0;

    public AnimTrack[]? ScaleTracks { get; }
    public AnimTrack? RotationTrack { get; }
    public AnimTrack[]? TranslationTracks { get; }

    public Vector2 ScaleAt(float frame) => ScaleTracks is null ? Vector2.One : new(ScaleTracks[0].Evaluate(frame), ScaleTracks[1].Evaluate(frame));
    public float RotationAt(float frame) => RotationTrack?.Evaluate(frame) ?? 0;
    public Vector2 TranslationAt(float frame) => TranslationTracks is null ? Vector2.Zero : new(TranslationTracks[0].Evaluate(frame), TranslationTracks[1].Evaluate(frame));

    public override string ToString() => $"{(IsIndirect ? "ind" : "tex")}{Index} ({Code:X8})";
}
