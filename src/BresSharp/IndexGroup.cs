using System.Buffers.Binary;
using System.Text;

namespace BresSharp;

internal static class IndexGroup
{
    public const int EntrySize = 0x10;

    public static int Size(int count) => 8 + (count + 1) * EntrySize;

    public static List<(string Name, int Data)> Read(ReadOnlySpan<byte> d, int at)
    {
        int count = (int)BinaryPrimitives.ReadUInt32BigEndian(d[(at + 4)..]);
        if (count > 0xFFFF || at + Size(count) > d.Length)
            throw new InvalidDataException($"Index group at 0x{at:X} has an invalid entry count {count}.");

        List<(string, int)> entries = new(count);
        for (int i = 1; i <= count; i++)
        {
            int e = at + 8 + i * EntrySize;
            int name = at + BinaryPrimitives.ReadInt32BigEndian(d[(e + 0x08)..]);
            int data = at + BinaryPrimitives.ReadInt32BigEndian(d[(e + 0x0C)..]);
            entries.Add((StringAt(d, name), data));
        }
        return entries;
    }

    public static string StringAt(ReadOnlySpan<byte> d, int at)
    {
        ReadOnlySpan<byte> rest = d[at..];
        int end = rest.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? rest : rest[..end]);
    }

    public static (ushort Id, ushort Left, ushort Right)[] BuildTree(IReadOnlyList<string> names)
    {
        int n = names.Count;
        byte[][] keys = new byte[n + 1][];
        keys[0] = [];
        for (int i = 0; i < n; i++) keys[i + 1] = Encoding.Latin1.GetBytes(names[i]);

        ushort[] id = new ushort[n + 1], left = new ushort[n + 1], right = new ushort[n + 1];
        id[0] = 0xFFFF;

        for (int i = 1; i <= n; i++)
        {
            byte[] key = keys[i];
            int prev = 0, cur = left[0];
            while (id[cur] < id[prev])
            {
                prev = cur;
                cur = Bit(key, id[cur]) ? right[cur] : left[cur];
            }

            int bit = FirstDifference(key, keys[cur]);
            prev = 0;
            cur = left[0];
            bool goRight = false;
            while (id[cur] < id[prev] && id[cur] > bit)
            {
                prev = cur;
                goRight = Bit(key, id[cur]);
                cur = goRight ? right[cur] : left[cur];
            }

            id[i] = (ushort)bit;
            if (Bit(key, bit)) { left[i] = (ushort)cur; right[i] = (ushort)i; }
            else { left[i] = (ushort)i; right[i] = (ushort)cur; }

            if (goRight) right[prev] = (ushort)i;
            else left[prev] = (ushort)i;
        }

        var tree = new (ushort, ushort, ushort)[n + 1];
        for (int i = 0; i <= n; i++) tree[i] = (id[i], left[i], right[i]);
        return tree;
    }

    private static bool Bit(byte[] key, int bit)
    {
        int index = bit >> 3;
        return index < key.Length && ((key[index] >> (bit & 7)) & 1) != 0;
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        for (int i = Math.Max(a.Length, b.Length) * 8 - 1; i >= 0; i--)
            if (Bit(a, i) != Bit(b, i)) return i;
        throw new InvalidDataException($"Index group has the name '{Encoding.Latin1.GetString(a)}' twice.");
    }
}
