# Fire Will background assets

`build-backgrounds.ps1` is the reproducible development-time conversion command.

It performs three isolated operations:

1. remuxes `羁绊/background.mp4` to a silent `susanoo-madara.mp4`;
2. renders the supplied `sasuke_web_wallpaper` Canvas scene at fixed 1920x1080/30fps and encodes `flowing-sasuke.mp4`;
3. validates that both files contain exactly one H.264 `yuv420p` video stream and no audio, then writes `assets/backgrounds/manifest.json`.

The renderer uses headless Edge/Chrome only during development. It is not a runtime dependency and `edge-test-profile` is never read or copied.

```powershell
pwsh -File .\tools\wallpaper\build-backgrounds.ps1
```

The WPF project should embed the two MP4 files with these logical resource names:

- `FireWill.Assets.Backgrounds.susanoo-madara.mp4`
- `FireWill.Assets.Backgrounds.flowing-sasuke.mp4`

From `src/FireWill.App/FireWill.App.csproj`, the exact resource entries are:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\..\assets\backgrounds\susanoo-madara.mp4"
                    LogicalName="FireWill.Assets.Backgrounds.susanoo-madara.mp4" />
  <EmbeddedResource Include="..\..\assets\backgrounds\flowing-sasuke.mp4"
                    LogicalName="FireWill.Assets.Backgrounds.flowing-sasuke.mp4" />
</ItemGroup>
```

Run the no-package smoke test with the repository's pinned .NET SDK:

```powershell
& '..\..\.tools\dotnet\dotnet.exe' run `
  --project '.\tools\wallpaper\smoke-tests\BackgroundSmokeTests.csproj' `
  --configuration Release
```

The smoke test embeds both MP4 files, extracts them by logical resource name, verifies their SHA-256 values, repairs a deliberately corrupted cache entry, round-trips preferences, and exercises selection plus rotation.
