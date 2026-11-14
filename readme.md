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

## Build

```bash
dotnet build BresSharp.slnx -c Release
```

## Licence

AGPL-3.0-or-later. See [license.md](license.md).
