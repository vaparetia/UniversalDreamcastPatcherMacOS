# Universal Dreamcast Patcher — macOS (arm64)

Native arm64 macOS port using .NET 10 and Avalonia 12.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- xdelta3 via Homebrew: `brew install xdelta`

## Run without building

```bash
dotnet run --project source_mac
```

## Build a .app bundle

From the repo root:

```bash
./build-app.sh
```

This produces `Universal Dreamcast Patcher.app` in the repo root, ready to drag to `/Applications`.

## Build manually

```bash
dotnet publish source_mac/UniversalDreamcastPatcher.csproj \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -c Release \
  -o out
```

Output lands in `out/`. The `tools/` folder (containing `buildgdi` and `convertredumptogdi`) is copied there automatically.

## Notes

- `buildgdi` (in `source_mac/tools/`) is a native arm64 binary compiled from [Sappharad/GDIbuilder](https://github.com/Sappharad/GDIbuilder). It handles GDI extraction and rebuild natively.
- `convertredumptogdi` (in `source_mac/tools/`) is built from `tools_source/convertredumptogdi/` — a .NET 10 CLI port of [RedumpCUE2GDI](https://github.com/AwfulBear/RedumpCUE2GDI). It converts CUE/BIN disc images to GDI format.
- `xdelta3` is looked up in `tools/` first, then `PATH` — the Homebrew install satisfies this automatically.
- Patched gdi will be saved to '~/Documents/exported_gdi'
