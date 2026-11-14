using System.Buffers.Binary;
using System.Numerics;

namespace BresSharp.Anim;

public sealed class Chr0Animation
{
    public const int SupportedVersion = 5;

    public Chr0Animation(BresSubfile subfile)
    {
        if (subfile.Magic != "CHR0") throw new InvalidDataException($"'{subfile.Name}' is not a CHR0.");
        Subfile = subfile;
        byte[] d = subfile.Data;
        Version = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x08));
        if (Version != SupportedVersion) throw new NotSupportedException($"CHR0 version {Version} is not supported (only {SupportedVersion}).");

        FrameCount = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x20));
        Loop = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x24)) != 0;
        ScalingRule = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(0x28));

        Dictionary<int, string> names = subfile.StringRefs.ToDictionary(r => r.Position, r => r.Value);
        int group = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(0x10));
        Bones = [.. AnimGroup.Entries(d, group).Select(at => new Chr0Bone(d, at, names.GetValueOrDefault(at) ?? "", FrameCount))];
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int Version { get; }
    public int FrameCount { get; }
    public bool Loop { get; }
    public uint ScalingRule { get; }
    public IReadOnlyList<Chr0Bone> Bones { get; }

    public override string ToString() => $"{Name}: {FrameCount} frames, {Bones.Count} bones";
}

public sealed class Chr0Bone
{
    internal Chr0Bone(byte[] d, int at, string name, int frameCount)
    {
        Offset = at;
        Name = name;
        Code = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 4));
        int word = at + 8;

        Scale = HasScale ? Component(d, at, ref word, (Code >> 4 & 1) != 0, 13, (int)(Code >> 25) & 3, false, frameCount) : null;
        Rotation = HasRotation ? Component(d, at, ref word, (Code >> 5 & 1) != 0, 16, (int)(Code >> 27) & 7, true, frameCount) : null;
        Translation = HasTranslation ? Component(d, at, ref word, (Code >> 6 & 1) != 0, 19, (int)(Code >> 30) & 3, false, frameCount) : null;
        End = word;
    }

    public int Offset { get; }
    public int End { get; }
    public string Name { get; }
    public uint Code { get; }

    public bool Identity => (Code & 1 << 1) != 0;
    public bool HasScale => (Code & 1 << 22) != 0;
    public bool HasRotation => (Code & 1 << 23) != 0;
    public bool HasTranslation => (Code & 1 << 24) != 0;

    public AnimTrack[]? Scale { get; }
    public AnimTrack[]? Rotation { get; }
    public AnimTrack[]? Translation { get; }

    public Vector3 ScaleAt(float frame) => Scale is null ? Vector3.One : At(Scale, frame);
    public Vector3 RotationAt(float frame) => Rotation is null ? Vector3.Zero : At(Rotation, frame);
    public Vector3 TranslationAt(float frame) => Translation is null ? Vector3.Zero : At(Translation, frame);

    private static Vector3 At(AnimTrack[] t, float f) => new(t[0].Evaluate(f), t[1].Evaluate(f), t[2].Evaluate(f));

    private AnimTrack[] Component(byte[] d, int entry, ref int word, bool isotropic, int fixedBit, int format, bool rotation, int frameCount)
    {
        AnimFormat animFormat = format switch
        {
            1 => AnimFormat.Interpolated4,
            2 => AnimFormat.Interpolated6,
            3 => AnimFormat.Interpolated12,
            4 when rotation => AnimFormat.Linear1,
            5 when rotation => AnimFormat.Linear2,
            6 when rotation => AnimFormat.Linear4,
            _ => AnimFormat.Fixed,
        };

        int axes = isotropic ? 1 : 3;
        AnimTrack[] tracks = new AnimTrack[axes];
        for (int i = 0; i < axes; i++)
        {
            bool isFixed = (Code >> (fixedBit + i) & 1) != 0;
            if (isFixed)
                tracks[i] = AnimTrack.Fixed(BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(word)));
            else if (animFormat == AnimFormat.Fixed)
                throw new InvalidDataException($"CHR0 bone '{Name}' has animated data in unknown format {format}.");
            else
                tracks[i] = AnimTrack.Read(d, entry + BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(word)), animFormat, frameCount + 1);
            word += 4;
        }
        return isotropic ? [tracks[0], tracks[0], tracks[0]] : tracks;
    }

    public override string ToString() => $"{Name} ({Code:X8})";
}

internal static class AnimGroup
{
    public static IEnumerable<int> Entries(byte[] d, int at)
    {
        if (at <= 0) yield break;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at + 4));
        if (count > 0x4000 || BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at)) != 8 + (count + 1) * 0x10)
            throw new InvalidDataException($"Animation index group at 0x{at:X} is invalid.");
        for (int i = 1; i <= count; i++)
            yield return at + BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(at + 8 + i * 0x10 + 12));
    }
}
