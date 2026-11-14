using System.Buffers.Binary;

namespace BresSharp.Anim;

public enum AnimFormat
{
    Fixed,
    Interpolated4,
    Interpolated6,
    Interpolated12,
    Linear1,
    Linear2,
    Linear4,
}

public readonly record struct AnimKey(float Frame, float Value, float Tangent);

public sealed class AnimTrack
{
    private AnimTrack(AnimFormat format, float fixedValue, IReadOnlyList<AnimKey> keys, IReadOnlyList<float> baked, int dataOffset)
    {
        Format = format;
        FixedValue = fixedValue;
        Keys = keys;
        Baked = baked;
        DataOffset = dataOffset;
    }

    public AnimFormat Format { get; }
    public float FixedValue { get; }
    public IReadOnlyList<AnimKey> Keys { get; }
    public IReadOnlyList<float> Baked { get; }
    public int DataOffset { get; }
    public int DataSize { get; private init; }

    public bool IsFixed => Format == AnimFormat.Fixed;

    public static AnimTrack Fixed(float value) => new(AnimFormat.Fixed, value, [], [], -1);

    internal static AnimTrack Read(byte[] d, int at, AnimFormat format, int frameCount)
    {
        if (at < 0 || at + 8 > d.Length) throw new InvalidDataException($"Animation data at 0x{at:X} is outside the sub-file.");

        switch (format)
        {
            case AnimFormat.Interpolated4 or AnimFormat.Interpolated6 or AnimFormat.Interpolated12:
            {
                int count = U16(d, at);
                if (count == 0 || count > 0x4000) throw new InvalidDataException($"Animation data at 0x{at:X} has {count} keys.");
                int header = format == AnimFormat.Interpolated12 ? 8 : 16;
                int size = format switch { AnimFormat.Interpolated4 => 4, AnimFormat.Interpolated6 => 6, _ => 12 };
                if (at + header + count * size > d.Length) throw new InvalidDataException($"Animation keys at 0x{at:X} run past the sub-file.");

                float step = header == 16 ? F32(d, at + 8) : 0, @base = header == 16 ? F32(d, at + 12) : 0;
                AnimKey[] keys = new AnimKey[count];
                for (int i = 0; i < count; i++)
                {
                    int k = at + header + i * size;
                    keys[i] = format switch
                    {
                        AnimFormat.Interpolated4 => I4(d, k, step, @base),
                        AnimFormat.Interpolated6 => new AnimKey((short)U16(d, k) / 32f, @base + step * U16(d, k + 2), (short)U16(d, k + 4) / 256f),
                        _ => new AnimKey(F32(d, k), F32(d, k + 4), F32(d, k + 8)),
                    };
                }
                return new AnimTrack(format, 0, keys, [], at) { DataSize = header + count * size };
            }
            default:
            {
                int header = format == AnimFormat.Linear4 ? 0 : 8;
                int size = format switch { AnimFormat.Linear1 => 1, AnimFormat.Linear2 => 2, _ => 4 };
                int count = frameCount;
                if (at + header + count * size > d.Length) throw new InvalidDataException($"Baked animation at 0x{at:X} runs past the sub-file.");
                float step = header == 8 ? F32(d, at) : 0, @base = header == 8 ? F32(d, at + 4) : 0;
                float[] values = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int v = at + header + i * size;
                    values[i] = format switch
                    {
                        AnimFormat.Linear1 => @base + step * d[v],
                        AnimFormat.Linear2 => @base + step * U16(d, v),
                        _ => F32(d, v),
                    };
                }
                return new AnimTrack(format, 0, [], values, at) { DataSize = header + count * size };
            }
        }
    }

    public float Evaluate(float frame)
    {
        if (IsFixed) return FixedValue;
        if (Baked.Count > 0)
        {
            if (frame <= 0) return Baked[0];
            if (frame >= Baked.Count - 1) return Baked[^1];
            int i = (int)frame;
            return Baked[i] + (Baked[i + 1] - Baked[i]) * (frame - i);
        }

        if (frame <= Keys[0].Frame) return Keys[0].Value;
        if (frame >= Keys[^1].Frame) return Keys[^1].Value;
        int k = 0;
        while (k + 1 < Keys.Count && Keys[k + 1].Frame <= frame) k++;
        AnimKey a = Keys[k], b = Keys[k + 1];
        float span = b.Frame - a.Frame;
        if (span <= 0) return b.Value;
        float t = (frame - a.Frame) / span, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * a.Value + (t3 - 2 * t2 + t) * span * a.Tangent
             + (-2 * t3 + 3 * t2) * b.Value + (t3 - t2) * span * b.Tangent;
    }

    private static AnimKey I4(byte[] d, int at, float step, float @base)
    {
        uint packed = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        int frame = (int)(packed >> 24);
        int value = (int)(packed >> 12) & 0xFFF;
        int tangent = (int)(packed << 20) >> 20;
        return new AnimKey(frame, @base + step * value, tangent / 32f);
    }

    private static ushort U16(byte[] d, int at) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));

    private static float F32(byte[] d, int at) => BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at));

    public override string ToString() => IsFixed ? $"{FixedValue}" : $"{Format} ({(Keys.Count > 0 ? Keys.Count : Baked.Count)})";
}
