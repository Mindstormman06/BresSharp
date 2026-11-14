using System.Buffers.Binary;
using System.Text;

namespace BresSharp;

internal sealed class StringRefs
{
    private readonly byte[] _sub;
    private readonly Func<int, bool> _isString;
    private readonly List<(int Position, int Base)> _refs = [];
    private readonly HashSet<int> _taken = [];

    private StringRefs(byte[] sub, Func<int, bool> isString)
    {
        _sub = sub;
        _isString = isString;
    }

    public static List<(int Position, int Base)> Find(byte[] sub, Func<int, bool> isString)
    {
        StringRefs r = new(sub, isString);
        if (sub.Length < 0x20) return r._refs;
        r.Walk();
        if (r._refs.Count > 0) r.UserData();
        return r._refs;
    }

    private void UserData()
    {
        for (int at = 0x10; at + 0x28 <= _sub.Length; at += 4)
        {
            uint count = U32(at + 4);
            if (count == 0 || count > 0x100 || U32(at) != 8 + (count + 1) * 0x10 || U32(at + 8) != 0xFFFF0000) continue;
            foreach (int entry in Group(at))
            {
                if (U32(entry) != 0x18 || U32(entry + 0x0C) > 2) continue;
                Ref(entry + 0x10, entry);
                if (U32(entry + 0x0C) == 2) Ref(entry + 0x04, entry);
            }
        }
    }

    private void Walk()
    {
        switch (Encoding.Latin1.GetString(_sub, 0, 4))
        {
            case "TEX0" or "PLT0":
                Header(1);
                break;
            case "CHR0" or "CLR0" or "SRT0" or "VIS0":
                Header(2);
                foreach (int node in Group(Section(0))) Ref(node, node);
                break;
            case "PAT0":
                Header(6);
                foreach (int node in Group(Section(0))) Ref(node, node);
                Table(Section(1), U16(0x34));
                Table(Section(2), U16(0x36));
                break;
            case "SHP0":
                Header(3);
                foreach (int node in Group(Section(0))) Ref(node + 4, node);
                Table(Section(1), int.MaxValue);
                break;
            case "SCN0":
                Header(7);
                foreach (int folder in Group(Section(0)))
                    foreach (int item in Group(folder)) Scn0Item(item, U32(item) == 0x4C);
                for (int kind = 0; kind < 5; kind++)
                {
                    int item = Section(1 + kind);
                    for (int i = 0; i < U16(0x3C + kind * 2) && item > 0; i++)
                    {
                        uint size = U32(item);
                        if (size < 0x14 || size > 0x200 || item + size > _sub.Length) break;
                        Scn0Item(item, kind == 0);
                        item += (int)size;
                    }
                }
                break;
            case "MDL0":
                Header(14);
                Model();
                break;
        }
    }

    private void Model()
    {
        foreach (int i in (int[])[0, 11, 12]) Group(Section(i)).Count();
        foreach (int bone in Group(Section(1))) Ref(bone + 8, bone);
        for (int s = 2; s <= 7; s++)
            foreach (int array in Group(Section(s)))
                if (!Ref(array + 12, array)) Ref(array + 8, array);

        foreach (int material in Group(Section(8)))
        {
            Ref(material + 8, material);
            int count = (int)U32(material + 0x2C);
            int refs = material + (int)U32(material + 0x30);
            if (count > 8 || refs <= material || refs + count * 0x34 > _sub.Length) continue;
            for (int t = 0; t < count; t++)
            {
                int texRef = refs + t * 0x34;
                if (!Ref(texRef, texRef)) Ref(texRef, material);
                if (!Ref(texRef + 4, texRef)) Ref(texRef + 4, material);
            }
        }

        Group(Section(9)).Count();
        foreach (int shape in Group(Section(10))) Ref(shape + 0x38, shape);
        Group(Section(13)).Count();
    }

    private void Scn0Item(int item, bool lightSet)
    {
        Ref(item + 8, item);
        if (!lightSet) return;
        Ref(item + 0x14, item);
        for (int i = 0; i < 8; i++) Ref(item + 0x1C + i * 4, item + 0x1C);
    }

    private void Header(int sections)
    {
        int name = 0x10 + sections * 4;
        Ref(name, 0);
        Ref(name + 4, 0);
    }

    private int Section(int index)
    {
        int at = 0x10 + index * 4;
        if (at + 4 > _sub.Length) return 0;
        int offset = BinaryPrimitives.ReadInt32BigEndian(_sub.AsSpan(at));
        return offset > 0 && offset < _sub.Length ? offset : 0;
    }

    private IEnumerable<int> Group(int at)
    {
        if (at <= 0 || at + 8 > _sub.Length) yield break;
        uint size = U32(at), count = U32(at + 4);
        if (count > 0x4000 || size != 8 + (count + 1) * 0x10 || at + size > _sub.Length) yield break;
        if (U16(at + 8) != 0xFFFF) yield break;

        for (int i = 1; i <= count; i++)
        {
            int e = at + 8 + i * 0x10;
            Ref(e + 8, at);
            int data = BinaryPrimitives.ReadInt32BigEndian(_sub.AsSpan(e + 12));
            if (data > 0 && at + data < _sub.Length) yield return at + data;
        }
    }

    private void Table(int at, int count)
    {
        if (at <= 0) return;
        for (int i = 0; i < count && at + i * 4 + 4 <= _sub.Length; i++)
            if (!Ref(at + i * 4, at)) return;
    }

    private bool Ref(int position, int @base)
    {
        if (position < 0 || position + 4 > _sub.Length || _taken.Contains(position)) return false;
        int value = BinaryPrimitives.ReadInt32BigEndian(_sub.AsSpan(position));
        if (value == 0 || !_isString(@base + value)) return false;
        _taken.Add(position);
        _refs.Add((position, @base));
        return true;
    }

    private uint U32(int at) => at >= 0 && at + 4 <= _sub.Length ? BinaryPrimitives.ReadUInt32BigEndian(_sub.AsSpan(at)) : 0;

    private ushort U16(int at) => at >= 0 && at + 2 <= _sub.Length ? BinaryPrimitives.ReadUInt16BigEndian(_sub.AsSpan(at)) : (ushort)0;
}
