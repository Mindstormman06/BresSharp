namespace BresSharp.Mdl0;

public enum Mdl0Cull : uint { None, Front, Back, All }

public enum Mdl0Wrap : uint { Clamp, Repeat, Mirror }

public enum Mdl0MinFilter : uint { Nearest, Linear, NearestMipNearest, LinearMipNearest, NearestMipLinear, LinearMipLinear }

public enum Mdl0MagFilter : uint { Nearest, Linear }

public sealed class Mdl0Material
{
    private const int TextureRefSize = 0x34;
    private readonly Mdl0Model _model;

    internal Mdl0Material(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
        int count = (int)_model.U32(at + 0x2C);
        int refs = at + _model.I32(at + 0x30);
        Textures = count == 0 ? [] : [.. Enumerable.Range(0, count).Select(i => new Mdl0TextureRef(model, refs + i * TextureRefSize))];
    }

    public int Offset { get; }

    public string Name => _model.NameAt(Offset + 0x08) ?? $"material{Index}";
    public int Index => _model.I32(Offset + 0x0C);
    public bool IsTranslucent => (_model.U32(Offset + 0x10) & 0x80000000) != 0;
    public int TexGenCount => _model.Data[Offset + 0x14];
    public int LightChannelCount => _model.Data[Offset + 0x15];
    public int TevStageCount => _model.Data[Offset + 0x16];
    public int IndirectStageCount => _model.Data[Offset + 0x17];
    public Mdl0Cull Cull { get => (Mdl0Cull)_model.U32(Offset + 0x18); set => _model.SetU32(Offset + 0x18, (uint)value); }
    public bool DepthTestBeforeTexture => _model.Data[Offset + 0x1C] != 0;
    public sbyte LightSet => (sbyte)_model.Data[Offset + 0x1D];
    public sbyte Fog => (sbyte)_model.Data[Offset + 0x1E];
    public int ShaderOffset => Offset + _model.I32(Offset + 0x28);

    public IReadOnlyList<Mdl0TextureRef> Textures { get; }

    public Mdl0Shader? Shader => _model.Shaders.FirstOrDefault(s => s.Offset == ShaderOffset);

    // The material's GX commands: pixel state, TEV colours and constants, and the texgen and dual-texgen matrix loads.
    public ReadOnlySpan<byte> DisplayList => _model.Data.AsSpan(Offset + _model.I32(Offset + 0x3C), DisplayListSize);

    public Mdl0TextureSrt TextureSrt(int index) => new(_model, Offset + SrtBlock + Check(index) * 0x14);

    public Mdl0TextureEffect TextureEffect(int index) => new(_model, Offset + EffectBlock + Check(index) * 0x34);

    public uint LightChannelFlags(int channel) => _model.U32(Channel(channel));

    public (byte R, byte G, byte B, byte A) MaterialColor(int channel) => Rgba(Channel(channel) + 0x04);

    public (byte R, byte G, byte B, byte A) AmbientColor(int channel) => Rgba(Channel(channel) + 0x08);

    // GX channel control words (XF registers 0x100E-0x1011) for the colour and the alpha of a channel.
    public uint ColorControl(int channel) => _model.U32(Channel(channel) + 0x0C);

    public uint AlphaControl(int channel) => _model.U32(Channel(channel) + 0x10);

    private const int DisplayListSize = 0x180, SrtBlock = 0x1B0, EffectBlock = 0x250, ChannelBlock = 0x3F0;

    private int Channel(int channel) => Offset + ChannelBlock + (channel is 0 or 1 ? channel : throw new ArgumentOutOfRangeException(nameof(channel))) * 0x14;

    private static int Check(int index) => (uint)index < 8 ? index : throw new ArgumentOutOfRangeException(nameof(index));

    private (byte, byte, byte, byte) Rgba(int at) => (_model.Data[at], _model.Data[at + 1], _model.Data[at + 2], _model.Data[at + 3]);

    public override string ToString() => $"{Name} ({Textures.Count} textures, {TevStageCount} stages)";
}

public sealed class Mdl0TextureRef
{
    private readonly Mdl0Model _model;

    internal Mdl0TextureRef(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }

    public string? Texture => _model.NameAt(Offset);
    public string? Palette => _model.NameAt(Offset + 0x04);
    public int TextureId => _model.I32(Offset + 0x10);
    public int PaletteId => _model.I32(Offset + 0x14);
    public Mdl0Wrap WrapU { get => (Mdl0Wrap)_model.U32(Offset + 0x18); set => _model.SetU32(Offset + 0x18, (uint)value); }
    public Mdl0Wrap WrapV { get => (Mdl0Wrap)_model.U32(Offset + 0x1C); set => _model.SetU32(Offset + 0x1C, (uint)value); }
    public Mdl0MinFilter MinFilter { get => (Mdl0MinFilter)_model.U32(Offset + 0x20); set => _model.SetU32(Offset + 0x20, (uint)value); }
    public Mdl0MagFilter MagFilter { get => (Mdl0MagFilter)_model.U32(Offset + 0x24); set => _model.SetU32(Offset + 0x24, (uint)value); }
    public float LodBias { get => _model.F32(Offset + 0x28); set => _model.SetF32(Offset + 0x28, value); }
    public uint MaxAnisotropy => _model.U32(Offset + 0x2C);

    public override string ToString() => $"{Texture} ({WrapU}/{WrapV}, {MinFilter}/{MagFilter})";
}

public sealed class Mdl0TextureSrt
{
    private readonly Mdl0Model _model;

    internal Mdl0TextureSrt(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }

    public System.Numerics.Vector2 Scale => new(_model.F32(Offset), _model.F32(Offset + 4));
    // Degrees.
    public float Rotation => _model.F32(Offset + 8);
    public System.Numerics.Vector2 Translation => new(_model.F32(Offset + 12), _model.F32(Offset + 16));

    public override string ToString() => $"S{Scale} R{Rotation} T{Translation}";
}

public sealed class Mdl0TextureEffect
{
    private readonly Mdl0Model _model;

    internal Mdl0TextureEffect(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }

    // The camera and light a generated coordinate is projected from, -1 for none.
    public sbyte Camera => (sbyte)_model.Data[Offset];
    public sbyte Light => (sbyte)_model.Data[Offset + 1];
    // 0 standard UV, 1 environment (camera), 2 projection, 3 environment (light), 4 environment (specular).
    public byte MapMode => _model.Data[Offset + 2];
    public byte Flags => _model.Data[Offset + 3];
    public System.Numerics.Matrix4x4 Matrix => _model.Matrix3x4(Offset + 4);

    public override string ToString() => $"map {MapMode} camera {Camera} light {Light}";
}

public sealed class Mdl0Shader
{
    private readonly Mdl0Model _model;

    internal Mdl0Shader(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }
    public int Size => (int)_model.U32(Offset);
    public int Index => _model.I32(Offset + 0x08);
    public int StageCount => _model.Data[Offset + 0x0C];
    public ReadOnlySpan<byte> TexScaleMap => _model.Data.AsSpan(Offset + 0x10, 8);
    public ReadOnlySpan<byte> Code => _model.Data.AsSpan(Offset + 0x20, Math.Max(0, Size - 0x20));

    public override string ToString() => $"shader{Index} ({StageCount} stages)";
}
