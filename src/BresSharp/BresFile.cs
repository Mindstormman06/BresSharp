using System.Buffers.Binary;
using System.Text;

namespace BresSharp;

public readonly record struct BresStringRef(int Position, int Base, string Value);

public sealed class BresSubfile(string name, byte[] data)
{
    public string Name { get; set; } = name;

    public byte[] Data { get; set; } = data;

    public byte[] Trailing { get; set; } = [];

    public List<BresStringRef> StringRefs { get; } = [];

    public string Magic => Data.Length >= 4 ? Encoding.Latin1.GetString(Data, 0, 4) : "";

    public bool HasHeader => Magic is "MDL0" or "TEX0" or "PLT0" or "CHR0" or "CLR0" or "SRT0" or "PAT0" or "VIS0" or "SHP0" or "SCN0";

    public bool UsesXtx => Magic == "TEX0" && Data.Length >= 0x38 && BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(0x34)) != 0;

    public int Alignment => Magic is "CHR0" or "CLR0" or "SRT0" or "PAT0" or "VIS0" or "SHP0" or "SCN0" ? 4 : 0x20;

    public override string ToString() => $"{Name} ({Magic}, 0x{Data.Length:X} bytes)";
}

public sealed class BresFolder(string name)
{
    public string Name { get; set; } = name;

    public List<BresSubfile> Files { get; } = [];

    public BresSubfile? this[string name] => Files.Find(f => f.Name == name);
}

public sealed class BresFile
{
    private const int HeaderSize = 0x10;
    private const int FileAlignment = 0x80;

    public List<BresFolder> Folders { get; } = [];

    public SortedSet<string> ExtraStrings { get; } = new(StringComparer.Ordinal);

    public BresFolder? this[string name] => Folders.Find(f => f.Name == name);

    public IEnumerable<BresSubfile> AllFiles => Folders.SelectMany(f => f.Files);

    public IReadOnlyList<BresSubfile> XtxTextureSlots => [.. this["Textures(NW4R)"]?.Files.Where(f => f.UsesXtx) ?? []];

    public static bool IsBres(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && data[..4].SequenceEqual("bres"u8);

    // ---- reading ----------------------------------------------------------

    public static BresFile FromFile(string path) => FromBinary(File.ReadAllBytes(path));

    public static BresFile FromBinary(ReadOnlySpan<byte> data)
    {
        if (!IsBres(data) || BinaryPrimitives.ReadUInt16BigEndian(data[4..]) != 0xFEFF)
            throw new InvalidDataException("Not a big-endian BRRES file.");

        int root = BinaryPrimitives.ReadUInt16BigEndian(data[0x0C..]);
        if (!data.Slice(root, 4).SequenceEqual("root"u8))
            throw new InvalidDataException("BRRES has no root section.");

        BresFile file = new();
        List<(BresSubfile File, int Start)> placed = [];
        HashSet<string> named = new(StringComparer.Ordinal);

        foreach ((string folderName, int group) in IndexGroup.Read(data, root + 8))
        {
            BresFolder folder = new(folderName);
            named.Add(folderName);
            foreach ((string name, int start) in IndexGroup.Read(data, group))
            {
                BresSubfile sub = new(name, []);
                folder.Files.Add(sub);
                placed.Add((sub, start));
                named.Add(name);
            }
            file.Folders.Add(folder);
        }

        placed.Sort((a, b) => a.Start.CompareTo(b.Start));
        int scanFrom = root + (int)BinaryPrimitives.ReadUInt32BigEndian(data[(root + 4)..]);
        if (placed.Count > 0)
        {
            int last = placed[^1].Start;
            scanFrom = last + (IsKnownMagic(data.Slice(last, 4)) ? (int)BinaryPrimitives.ReadUInt32BigEndian(data[(last + 4)..]) : 4);
        }
        int pool = FindPool(data, LowestGroupName(data, root), scanFrom);
        List<(int At, string Value)> poolStrings = ReadPool(data, pool);
        HashSet<int> stringStarts = [.. poolStrings.Select(s => s.At)];

        for (int i = 0; i < placed.Count; i++)
        {
            (BresSubfile sub, int start) = placed[i];
            int next = i + 1 < placed.Count ? placed[i + 1].Start : pool;
            int size = next - start;
            if (data.Length - start >= 8 && IsKnownMagic(data.Slice(start, 4)))
                size = Math.Min(size, (int)BinaryPrimitives.ReadUInt32BigEndian(data[(start + 4)..]));

            sub.Data = data.Slice(start, size).ToArray();
            sub.Trailing = data[(start + size)..next].ToArray();
            foreach ((int position, int @base) in StringRefs.Find(sub.Data, relative => stringStarts.Contains(start + relative)))
            {
                int target = @base + BinaryPrimitives.ReadInt32BigEndian(sub.Data.AsSpan(position));
                string value = IndexGroup.StringAt(data, start + target);
                sub.StringRefs.Add(new BresStringRef(position, @base, value));
                named.Add(value);
            }
        }

        foreach ((_, string s) in poolStrings)
            if (!named.Contains(s)) file.ExtraStrings.Add(s);

        return file;
    }

    private static bool IsKnownMagic(ReadOnlySpan<byte> magic)
        => magic.SequenceEqual("MDL0"u8) || magic.SequenceEqual("TEX0"u8) || magic.SequenceEqual("PLT0"u8)
        || magic.SequenceEqual("CHR0"u8) || magic.SequenceEqual("CLR0"u8) || magic.SequenceEqual("SRT0"u8)
        || magic.SequenceEqual("PAT0"u8) || magic.SequenceEqual("VIS0"u8) || magic.SequenceEqual("SHP0"u8)
        || magic.SequenceEqual("SCN0"u8);

    private static int FindPool(ReadOnlySpan<byte> data, int lowestName, int scanFrom)
    {
        if (lowestName >= data.Length) return Math.Min(Align(scanFrom, 4), data.Length);

        for (int p = Align(scanFrom, 4); p <= lowestName - 4; p += 4)
        {
            int q = p;
            while (q < lowestName - 4)
            {
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[q..]);
                if (length <= 0 || length > 0x400 || q + 4 + length >= data.Length || data[q + 4 + length] != 0
                    || data.Slice(q + 4, length).IndexOf((byte)0) >= 0) break;
                q = Align(q + 4 + length + 1, 4);
            }
            if (q == lowestName - 4) return p;
        }
        throw new InvalidDataException("Could not find the BRRES string pool.");
    }

    private static int LowestGroupName(ReadOnlySpan<byte> data, int root)
    {
        int lowest = int.MaxValue;
        int group = root + 8;
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(group + 4)..]);
        for (int i = 1; i <= count; i++)
        {
            int e = group + 8 + i * IndexGroup.EntrySize;
            lowest = Math.Min(lowest, group + BinaryPrimitives.ReadInt32BigEndian(data[(e + 8)..]));
            int sub = group + BinaryPrimitives.ReadInt32BigEndian(data[(e + 12)..]);
            int subCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data[(sub + 4)..]);
            for (int j = 1; j <= subCount; j++)
                lowest = Math.Min(lowest, sub + BinaryPrimitives.ReadInt32BigEndian(data[(sub + 8 + j * IndexGroup.EntrySize + 8)..]));
        }
        return lowest == int.MaxValue ? data.Length : lowest;
    }

    private static List<(int At, string Value)> ReadPool(ReadOnlySpan<byte> data, int pool)
    {
        List<(int, string)> strings = [];
        int p = pool;
        while (p + 4 <= data.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[p..]);
            if (length <= 0 || p + 4 + length >= data.Length) break;
            strings.Add((p + 4, Encoding.Latin1.GetString(data.Slice(p + 4, length))));
            p = (p + 4 + length + 1 + 3) & ~3;
        }
        return strings;
    }

    // ---- writing ----------------------------------------------------------

    public void WriteTo(string path) => File.WriteAllBytes(path, Write());

    public byte[] Write()
    {
        SortedSet<string> strings = new(ExtraStrings, StringComparer.Ordinal);
        foreach (BresFolder folder in Folders)
        {
            strings.Add(folder.Name);
            foreach (BresSubfile sub in folder.Files)
            {
                strings.Add(sub.Name);
                foreach (BresStringRef r in sub.StringRefs) strings.Add(r.Value);
            }
        }

        int root = HeaderSize;
        int rootGroup = root + 8;
        int[] groups = new int[Folders.Count];
        int at = rootGroup + IndexGroup.Size(Folders.Count);
        for (int i = 0; i < Folders.Count; i++)
        {
            groups[i] = at;
            at += IndexGroup.Size(Folders[i].Files.Count);
        }
        int rootEnd = at;

        Dictionary<BresSubfile, int> starts = new(ReferenceEqualityComparer.Instance);
        foreach (BresSubfile sub in AllFiles)
        {
            at = Align(at, sub.Alignment);
            starts[sub] = at;
            at += sub.Data.Length + sub.Trailing.Length;
        }

        int pool = Align(at, 4);
        Dictionary<string, int> stringAt = new(StringComparer.Ordinal);
        at = pool;
        foreach (string s in strings)
        {
            stringAt[s] = at + 4;
            at = Align(at + 4 + Encoding.Latin1.GetByteCount(s) + 1, 4);
        }

        byte[] output = new byte[Align(at, FileAlignment)];
        Span<byte> o = output;

        "bres"u8.CopyTo(o);
        BinaryPrimitives.WriteUInt16BigEndian(o[4..], 0xFEFF);
        BinaryPrimitives.WriteUInt32BigEndian(o[8..], (uint)output.Length);
        BinaryPrimitives.WriteUInt16BigEndian(o[0x0C..], (ushort)root);
        BinaryPrimitives.WriteUInt16BigEndian(o[0x0E..], (ushort)(1 + AllFiles.Count()));

        "root"u8.CopyTo(o[root..]);
        BinaryPrimitives.WriteUInt32BigEndian(o[(root + 4)..], (uint)(rootEnd - root));

        WriteGroup(o, rootGroup, Folders.Select((f, i) => (f.Name, groups[i])).ToList(), stringAt);
        for (int i = 0; i < Folders.Count; i++)
            WriteGroup(o, groups[i], Folders[i].Files.Select(f => (f.Name, starts[f])).ToList(), stringAt);

        foreach (BresSubfile sub in AllFiles)
        {
            int start = starts[sub];
            sub.Data.CopyTo(o[start..]);
            sub.Trailing.CopyTo(o[(start + sub.Data.Length)..]);
            if (sub.HasHeader && sub.Data.Length >= 0x10)
                BinaryPrimitives.WriteInt32BigEndian(o[(start + 0x0C)..], -start);
            foreach (BresStringRef r in sub.StringRefs)
                BinaryPrimitives.WriteInt32BigEndian(o[(start + r.Position)..], stringAt[r.Value] - (start + r.Base));
        }

        foreach ((string s, int position) in stringAt)
        {
            int length = Encoding.Latin1.GetByteCount(s);
            BinaryPrimitives.WriteUInt32BigEndian(o[(position - 4)..], (uint)length);
            Encoding.Latin1.GetBytes(s, o[position..]);
        }

        return output;
    }

    private static void WriteGroup(Span<byte> o, int at, List<(string Name, int Data)> entries, Dictionary<string, int> stringAt)
    {
        var tree = IndexGroup.BuildTree(entries.Select(e => e.Name).ToList());
        BinaryPrimitives.WriteUInt32BigEndian(o[at..], (uint)IndexGroup.Size(entries.Count));
        BinaryPrimitives.WriteUInt32BigEndian(o[(at + 4)..], (uint)entries.Count);

        for (int i = 0; i <= entries.Count; i++)
        {
            Span<byte> e = o[(at + 8 + i * IndexGroup.EntrySize)..];
            BinaryPrimitives.WriteUInt16BigEndian(e, tree[i].Id);
            BinaryPrimitives.WriteUInt16BigEndian(e[2..], 0);
            BinaryPrimitives.WriteUInt16BigEndian(e[4..], tree[i].Left);
            BinaryPrimitives.WriteUInt16BigEndian(e[6..], tree[i].Right);
            if (i == 0) continue;
            BinaryPrimitives.WriteInt32BigEndian(e[8..], stringAt[entries[i - 1].Name] - at);
            BinaryPrimitives.WriteInt32BigEndian(e[12..], entries[i - 1].Data - at);
        }
    }

    private static int Align(int value, int to) => (value + to - 1) / to * to;
}
