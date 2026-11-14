using static BresSharp.Anim.AnimHeader;

namespace BresSharp.Anim;

public enum Scn0ItemKind { LightSet, AmbientLight, Light, Fog, Camera }

public sealed class Scn0Animation
{
    public Scn0Animation(BresSubfile subfile)
    {
        byte[] d = Open(subfile, "SCN0", 5, out var names);
        Subfile = subfile;
        FrameCount = U16(d, 0x34);
        SpecularLightCount = U16(d, 0x36);
        Loop = U32(d, 0x38) != 0;

        List<Scn0Item> items = [];
        for (int kind = 0; kind < 5; kind++)
        {
            int at = I32(d, 0x14 + kind * 4);
            int count = U16(d, 0x3C + kind * 2);
            if (count == 0) continue;
            int item = at;
            for (int i = 0; i < count; i++)
            {
                uint size = item > 0 && item + 0x14 <= d.Length ? U32(d, item) : 0;
                if (size < 0x14 || size > 0x200 || item + size > d.Length)
                    throw new InvalidDataException($"SCN0 {(Scn0ItemKind)kind} {i} at 0x{item:X} has an invalid size 0x{size:X}.");
                items.Add(new Scn0Item(d, item, (Scn0ItemKind)kind, names));
                item += (int)size;
            }
        }
        Items = items;
    }

    public BresSubfile Subfile { get; }
    public string Name => Subfile.Name;
    public int FrameCount { get; }
    public int SpecularLightCount { get; }
    public bool Loop { get; }
    public IReadOnlyList<Scn0Item> Items { get; }

    public IEnumerable<Scn0Item> OfKind(Scn0ItemKind kind) => Items.Where(i => i.Kind == kind);

    public override string ToString() => $"{Name}: {FrameCount} frames, {Items.Count} items";
}

public sealed class Scn0Item
{
    internal Scn0Item(byte[] d, int at, Scn0ItemKind kind, Dictionary<int, string> names)
    {
        Offset = at;
        Kind = kind;
        Name = names.GetValueOrDefault(at + 8) ?? "";
        NodeIndex = (int)U32(d, at + 0x0C);
        RealIndex = (int)U32(d, at + 0x10);
        Body = d.AsSpan(at + 0x14, (int)U32(d, at) - 0x14).ToArray();

        if (kind == Scn0ItemKind.LightSet)
        {
            AmbientLight = names.GetValueOrDefault(at + 0x14);
            int lights = d[at + 0x1A];
            Lights = [.. Enumerable.Range(0, Math.Min(lights, 8)).Select(i => names.GetValueOrDefault(at + 0x1C + i * 4) ?? "")];
        }
    }

    public int Offset { get; }
    public Scn0ItemKind Kind { get; }
    public string Name { get; }
    public int NodeIndex { get; }
    public int RealIndex { get; }
    public bool IsUnused => NodeIndex == -1 && Name.Length == 0;
    public byte[] Body { get; }

    public string? AmbientLight { get; }
    public IReadOnlyList<string> Lights { get; } = [];

    public override string ToString() => $"{Kind} {Name}";
}
