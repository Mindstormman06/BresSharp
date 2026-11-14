using static BresSharp.Anim.AnimHeader;

namespace BresSharp.Anim;

public enum Clr0Target
{
    MaterialColor0,
    MaterialColor1,
    AmbientColor0,
    AmbientColor1,
    TevColor1,
    TevColor2,
    TevColor3,
    TevKColor0,
    TevKColor1,
    TevKColor2,
    TevKColor3,
}

public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    internal static Rgba From(uint v) => new((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

public sealed class Clr0Animation
{
    public Clr0Animation(BresSubfile subfile)
    {
        byte[] d = Open(subfile, "CLR0", 4, out var names);
        Subfile = subfile;
        FrameCount = U16(d, 0x20);
        Loop = U32(d, 0x24) != 0;
        Materials = [.. AnimGroup.Entries(d, I32(d, 0x10)).Select(at => new Clr0Material(d, at, names.GetValueOrDefault(at) ?? "", FrameCount + 1))];
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int FrameCount { get; }
    public bool Loop { get; }
    public IReadOnlyList<Clr0Material> Materials { get; }

    public override string ToString() => $"{Name}: {FrameCount} frames, {Materials.Count} materials";
}

public sealed class Clr0Material
{
    internal Clr0Material(byte[] d, int at, string name, int frames)
    {
        Offset = at;
        Name = name;
        Flags = U32(d, at + 4);
        List<Clr0Track> tracks = [];
        int word = at + 8;
        for (int t = 0; t <= (int)Clr0Target.TevKColor3; t++)
        {
            uint f = Flags >> (t * 2) & 3;
            if ((f & 1) == 0) continue;
            tracks.Add(new Clr0Track(d, word, (Clr0Target)t, (f & 2) != 0, frames));
            word += 8;
        }
        Tracks = tracks;
        End = word;
    }

    public int Offset { get; }
    public int End { get; }
    public string Name { get; }
    public uint Flags { get; }
    public IReadOnlyList<Clr0Track> Tracks { get; }

    public override string ToString() => $"{Name} ({Tracks.Count} colours)";
}

public sealed class Clr0Track
{
    internal Clr0Track(byte[] d, int at, Clr0Target target, bool isFixed, int frames)
    {
        Target = target;
        Mask = U32(d, at);
        IsFixed = isFixed;
        if (isFixed)
        {
            Colors = [Rgba.From(U32(d, at + 4))];
            return;
        }

        int data = at + 4 + I32(d, at + 4);
        if (data < 0 || data + frames * 4 > d.Length) throw new InvalidDataException($"CLR0 colour data at 0x{data:X} runs past the sub-file.");
        DataOffset = data;
        DataSize = frames * 4;
        Colors = [.. Enumerable.Range(0, frames).Select(i => Rgba.From(U32(d, data + i * 4)))];
    }

    public Clr0Target Target { get; }
    public uint Mask { get; }
    public bool IsFixed { get; }
    public IReadOnlyList<Rgba> Colors { get; }
    public int DataOffset { get; } = -1;
    public int DataSize { get; }

    public Rgba At(int frame) => Colors[Math.Clamp(frame, 0, Colors.Count - 1)];

    public override string ToString() => IsFixed ? $"{Target} {Colors[0]}" : $"{Target} ({Colors.Count} frames)";
}
