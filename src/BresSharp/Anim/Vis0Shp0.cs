using static BresSharp.Anim.AnimHeader;

namespace BresSharp.Anim;

public sealed class Vis0Animation
{
    public Vis0Animation(BresSubfile subfile)
    {
        byte[] d = Open(subfile, "VIS0", 4, out var names);
        Subfile = subfile;
        FrameCount = U16(d, 0x20);
        Loop = U32(d, 0x24) != 0;
        Bones = [.. AnimGroup.Entries(d, I32(d, 0x10)).Select(at => new Vis0Bone(d, at, names.GetValueOrDefault(at) ?? "", FrameCount + 1))];
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int FrameCount { get; }
    public bool Loop { get; }
    public IReadOnlyList<Vis0Bone> Bones { get; }

    public override string ToString() => $"{Name}: {FrameCount} frames, {Bones.Count} bones";
}

public sealed class Vis0Bone
{
    internal Vis0Bone(byte[] d, int at, string name, int frames)
    {
        Offset = at;
        Name = name;
        Flags = U32(d, at + 4);
        if (IsConstant)
        {
            Visible = [(Flags & 1) != 0];
            End = at + 8;
            return;
        }

        int words = (frames + 31) / 32;
        if (at + 8 + words * 4 > d.Length) throw new InvalidDataException($"VIS0 bone '{name}' runs past the sub-file.");
        Visible = [.. Enumerable.Range(0, frames).Select(f => (U32(d, at + 8 + f / 32 * 4) >> (31 - f % 32) & 1) != 0)];
        End = at + 8 + words * 4;
    }

    public int Offset { get; }
    public int End { get; }
    public string Name { get; }
    public uint Flags { get; }
    public bool IsConstant => (Flags & 2) != 0;
    public IReadOnlyList<bool> Visible { get; }

    public bool At(int frame) => Visible[Math.Clamp(frame, 0, Visible.Count - 1)];

    public override string ToString() => IsConstant ? $"{Name} {(Visible[0] ? "shown" : "hidden")}" : $"{Name} ({Visible.Count} frames)";
}

public sealed class Shp0Animation
{
    public Shp0Animation(BresSubfile subfile)
    {
        byte[] d = Open(subfile, "SHP0", 4, out var names);
        Subfile = subfile;
        FrameCount = U16(d, 0x24);
        Loop = U32(d, 0x28) != 0;
        int table = I32(d, 0x14);
        ShapeNames = table <= 0 ? [] : [.. Enumerable.Range(0, U16(d, 0x26)).Select(i => names.GetValueOrDefault(table + i * 4) ?? "")];
        Entries = [.. AnimGroup.Entries(d, I32(d, 0x10)).Select(at => new Shp0Entry(d, at, names.GetValueOrDefault(at + 4) ?? "", FrameCount))];
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int FrameCount { get; }
    public bool Loop { get; }
    public IReadOnlyList<string> ShapeNames { get; }
    public IReadOnlyList<Shp0Entry> Entries { get; }

    public override string ToString() => $"{Name}: {FrameCount} frames, {Entries.Count} entries";
}

public sealed class Shp0Entry
{
    internal Shp0Entry(byte[] d, int at, string name, int frameCount)
    {
        Offset = at;
        Name = name;
        Flags = U32(d, at);
        BaseShape = U16(d, at + 8);
        int count = U16(d, at + 10);
        FixedMask = U32(d, at + 12);
        int indices = at + I32(d, at + 16);
        if (count > 32 || indices + count * 2 > d.Length) throw new InvalidDataException($"SHP0 entry '{name}' is invalid.");

        List<(int, AnimTrack)> targets = [];
        for (int k = 0; k < count; k++)
        {
            int word = at + 20 + k * 4;
            AnimTrack weight = (FixedMask >> k & 1) != 0
                ? AnimTrack.Fixed(F32(d, word))
                : AnimTrack.Read(d, word + I32(d, word), AnimFormat.Interpolated12, frameCount + 1);
            targets.Add((U16(d, indices + k * 2), weight));
        }
        Targets = targets;
        End = Math.Max(at + 20 + count * 4, indices + (count * 2 + 3) / 4 * 4);
    }

    public int Offset { get; }
    public int End { get; }
    public string Name { get; }
    public uint Flags { get; }
    public int BaseShape { get; }
    public uint FixedMask { get; }
    public IReadOnlyList<(int Shape, AnimTrack Weight)> Targets { get; }

    public override string ToString() => $"{Name} ({Targets.Count} targets)";
}
