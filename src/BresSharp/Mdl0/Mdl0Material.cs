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

    public (byte R, byte G, byte B, byte A) MaterialColor(int channel) => Rgba(Offset + 0x3F0 + channel * 0x14 + 0x04);

    public (byte R, byte G, byte B, byte A) AmbientColor(int channel) => Rgba(Offset + 0x3F0 + channel * 0x14 + 0x08);

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
