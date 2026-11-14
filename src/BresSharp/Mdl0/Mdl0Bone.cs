using System.Numerics;

namespace BresSharp.Mdl0;

[Flags]
public enum Mdl0BoneFlags : uint
{
    Identity = 1 << 0,
    TranslationIsotropic = 1 << 1,
    RotationIsotropic = 1 << 2,
    ScaleIsotropic = 1 << 3,
    ScaleUniform = 1 << 4,
    ScaleCompensate = 1 << 5,
    ChildScaleCompensate = 1 << 6,
    DisableClassicScale = 1 << 7,
    Visible = 1 << 8,
    DisplayMatrix = 1 << 9,
    BillboardChild = 1 << 10,
}

public enum Mdl0Billboard : uint
{
    None,
    Standard,
    StandardPerspective,
    Rotation,
    RotationPerspective,
    Y,
    YPerspective,
}

public sealed class Mdl0Bone
{
    private readonly Mdl0Model _model;

    internal Mdl0Bone(Mdl0Model model, int at)
    {
        _model = model;
        Offset = at;
    }

    public int Offset { get; }

    public string Name => _model.NameAt(Offset + 0x08) ?? $"bone{Index}";
    public int Index => _model.I32(Offset + 0x0C);
    public int NodeId => _model.I32(Offset + 0x10);
    public Mdl0BoneFlags Flags { get => (Mdl0BoneFlags)_model.U32(Offset + 0x14); set => _model.SetU32(Offset + 0x14, (uint)value); }
    public Mdl0Billboard Billboard { get => (Mdl0Billboard)_model.U32(Offset + 0x18); set => _model.SetU32(Offset + 0x18, (uint)value); }
    public Vector3 Scale { get => _model.Vec3(Offset + 0x20); set => _model.SetVec3(Offset + 0x20, value); }
    public Vector3 Rotation { get => _model.Vec3(Offset + 0x2C); set => _model.SetVec3(Offset + 0x2C, value); }
    public Vector3 Translation { get => _model.Vec3(Offset + 0x38); set => _model.SetVec3(Offset + 0x38, value); }
    public Vector3 BoundsMin { get => _model.Vec3(Offset + 0x44); set => _model.SetVec3(Offset + 0x44, value); }
    public Vector3 BoundsMax { get => _model.Vec3(Offset + 0x50); set => _model.SetVec3(Offset + 0x50, value); }
    public Matrix4x4 Transform => ReadMatrix(Offset + 0x70);
    public Matrix4x4 InverseTransform => ReadMatrix(Offset + 0xA0);

    public Mdl0Bone? Parent { get; private set; }
    public IReadOnlyList<Mdl0Bone> Children { get; private set; } = [];

    internal void Link(Dictionary<int, Mdl0Bone> bonesAt)
    {
        Parent = Resolve(bonesAt, 0x5C);
        List<Mdl0Bone> children = [];
        for (Mdl0Bone? child = Resolve(bonesAt, 0x60); child is not null && children.Count < bonesAt.Count; child = child.Resolve(bonesAt, 0x64))
            children.Add(child);
        Children = children;
    }

    private Mdl0Bone? Resolve(Dictionary<int, Mdl0Bone> bonesAt, int field)
    {
        int offset = _model.I32(Offset + field);
        return offset != 0 && bonesAt.TryGetValue(Offset + offset, out Mdl0Bone? bone) ? bone : null;
    }

    private Matrix4x4 ReadMatrix(int at) => new(
        _model.F32(at), _model.F32(at + 16), _model.F32(at + 32), 0,
        _model.F32(at + 4), _model.F32(at + 20), _model.F32(at + 36), 0,
        _model.F32(at + 8), _model.F32(at + 24), _model.F32(at + 40), 0,
        _model.F32(at + 12), _model.F32(at + 28), _model.F32(at + 44), 1);

    public override string ToString() => $"{Name} T{Translation} R{Rotation} S{Scale}";
}
