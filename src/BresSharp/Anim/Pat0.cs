using static BresSharp.Anim.AnimHeader;

namespace BresSharp.Anim;

public readonly record struct Pat0Key(float Frame, int Texture, int Palette);

public sealed class Pat0Animation
{
    public Pat0Animation(BresSubfile subfile)
    {
        byte[] d = Open(subfile, "PAT0", 4, out var names);
        Subfile = subfile;
        FrameCount = U16(d, 0x30);
        Loop = U32(d, 0x38) != 0;
        TextureNames = Table(d, I32(d, 0x14), U16(d, 0x34), names);
        PaletteNames = Table(d, I32(d, 0x18), U16(d, 0x36), names);
        Materials = [.. AnimGroup.Entries(d, I32(d, 0x10)).Select(at => new Pat0Material(d, at, names.GetValueOrDefault(at) ?? ""))];
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int FrameCount { get; }
    public bool Loop { get; }
    public IReadOnlyList<string> TextureNames { get; }
    public IReadOnlyList<string> PaletteNames { get; }
    public IReadOnlyList<Pat0Material> Materials { get; }

    private static string[] Table(byte[] d, int at, int count, Dictionary<int, string> names)
        => at <= 0 ? [] : [.. Enumerable.Range(0, count).Select(i => names.GetValueOrDefault(at + i * 4) ?? "")];

    public override string ToString() => $"{Name}: {FrameCount} frames, {Materials.Count} materials, {TextureNames.Count} textures";
}

public sealed class Pat0Material
{
    internal Pat0Material(byte[] d, int at, string name)
    {
        Offset = at;
        Name = name;
        Flags = U32(d, at + 4);
        List<Pat0Slot> slots = [];
        int word = at + 8;
        for (int slot = 0; slot < 8; slot++)
        {
            uint f = Flags >> (slot * 4) & 0xF;
            if ((f & 1) == 0) continue;
            slots.Add(new Pat0Slot(d, at, word, slot, f));
            word += 4;
        }
        Slots = slots;
        End = word;
    }

    public int Offset { get; }
    public int End { get; }
    public string Name { get; }
    public uint Flags { get; }
    public IReadOnlyList<Pat0Slot> Slots { get; }

    public override string ToString() => $"{Name} ({Slots.Count} slots)";
}

public sealed class Pat0Slot
{
    internal Pat0Slot(byte[] d, int entry, int word, int index, uint flags)
    {
        Index = index;
        IsFixed = (flags & 2) != 0;
        HasTexture = (flags & 4) != 0;
        HasPalette = (flags & 8) != 0;

        if (IsFixed)
        {
            Keys = [new Pat0Key(0, U16(d, word), U16(d, word + 2))];
            return;
        }

        int at = entry + I32(d, word);
        int count = U16(d, at);
        if (count == 0 || count > 0x4000 || at + 8 + count * 8 > d.Length)
            throw new InvalidDataException($"PAT0 key list at 0x{at:X} is invalid.");
        DataOffset = at;
        DataSize = 8 + count * 8;
        Keys = [.. Enumerable.Range(0, count).Select(i => at + 8 + i * 8).Select(k => new Pat0Key(F32(d, k), U16(d, k + 4), U16(d, k + 6)))];
    }

    public int Index { get; }
    public bool IsFixed { get; }
    public bool HasTexture { get; }
    public bool HasPalette { get; }
    public IReadOnlyList<Pat0Key> Keys { get; }
    public int DataOffset { get; } = -1;
    public int DataSize { get; }

    public Pat0Key At(float frame)
    {
        Pat0Key current = Keys[0];
        foreach (Pat0Key key in Keys)
        {
            if (key.Frame > frame) break;
            current = key;
        }
        return current;
    }

    public override string ToString() => IsFixed ? $"slot{Index} fixed {Keys[0].Texture}" : $"slot{Index} ({Keys.Count} keys)";
}
