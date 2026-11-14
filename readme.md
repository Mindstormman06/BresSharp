# BresSharp

NW4R BRRES archive reading and writing (*Skyward Sword HD* `.brres`, and most Wii titles).

```csharp
BresFile bres = BresFile.FromBinary(U8File.FromBinary(arc)["g3d/model.brres"]!.Data);

BresSubfile link = bres["3DModels(NW4R)"]!["Alink"]!;
bres["3DModels(NW4R)"]!.Files.Remove(link);
byte[] packed = bres.Write();
```

Sub-files (MDL0, TEX0, PLT0, CHR0, CLR0, SRT0, PAT0, VIS0, SHP0, SCN0, and External data) are kept as bytes. Their
references into the shared string pool are found on read and rewritten on write, so sub-files can be added, removed,
renamed or moved between archives.

## Models

```csharp
Mdl0Model model = new(bres["3DModels(NW4R)"]!["Alink"]!);

foreach (Mdl0Bone bone in model.Bones) Console.WriteLine($"{bone.Name} -> {bone.Parent?.Name}");
Mdl0Shape body = model.Shapes[0];
Vector3[] positions = [.. model.Positions[body.PositionArray].AllVector3()];
foreach (var (a, b, c) in body.Triangles()) { /* a.Position, a.Normal, a.TexCoord[0] ... */ }

model.Bones[0].Translation += new Vector3(0, 10, 0);   // edits write into the sub-file bytes
```

MDL0 version 11 only. The views read the sub-file's bytes in place: bones, vertex/normal/colour/UV arrays, materials
and their texture references, shaders (raw GX code), shapes (display lists decoded to primitives and triangles) and
the definition bytecode. Fixed-size fields can be edited; adding or removing bones, materials or vertices is not
supported yet.

## Build

```bash
dotnet build BresSharp.slnx -c Release
```

## Licence

AGPL-3.0-or-later. See [license.md](license.md).
