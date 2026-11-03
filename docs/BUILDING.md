# Building Captly

## You need

- Windows 10 or 11, x64 or ARM64
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows PowerShell, for the scripts

## Build and test

```powershell
# 1. The speech library. For x64 it is copied from Moonshine's own release. For ARM64, which
#    Moonshine doesn't publish, it is built from source with a portable toolchain, on an ARM64 PC.
.\scripts\get-native.ps1

# 2. Build and test.
dotnet build Captly.slnx -c Release
dotnet test --solution Captly.slnx -c Release
```

Without step 1 everything still builds, but Captly can't load a model, and the speech engine tests are skipped. The engine tests also need the Tiny model, which `tools\Bench` downloads: `Bench download tiny`.

Run the app from `src\Captly.App\bin\Release\net10.0-windows10.0.22621.0\Captly.exe`.

## Make the download

```powershell
.\scripts\publish.ps1                 # both runtimes
.\scripts\publish.ps1 -Rid win-arm64  # or one
```

This writes `dist\Captly-<version>-<runtime>.zip` and a `.sha256` beside it. Each zip holds one self-contained `Captly.exe`, a read-me and the licences. The script refuses a runtime whose speech library is missing or built for the wrong processor.

## Releases

Pushing a tag such as `v0.1.0` runs `.github/workflows/release.yml`, which builds, tests and packages both runtimes on GitHub's x64 and ARM64 Windows runners and publishes them as a release. The tag must match `<Version>` in `Directory.Build.props`.

## How the code is laid out

| Project | What it does |
|---|---|
| `Captly.Core` | Settings, caption timing, source choice, media following, transcripts, window placement maths and the view models. No Windows dependencies, so it is all unit-tested. |
| `Captly.Audio` | System and per-app audio capture (NAudio), downmixing and resampling to 16 kHz. |
| `Captly.Speech` | The Moonshine engine through its C API, model downloads and the speed test. |
| `Captly.Media` | Windows' media controls: what's playing, paused or seeking. |
| `Captly.Windowing` | Finding and following the video's window, monitors and DPI, start with Windows. |
| `Captly.UI` | The WPF design system, caption overlay, Settings, popover, setup and tray. |
| `Captly.Updates` | The GitHub update check. |
| `Captly.App` | Puts it all together. |
| `tools/Bench` | Measurements and checks: speed tests, live latency, per-app isolation, window following and more. The commands are listed at the top of `tools/Bench/Program.cs`. |

Code comments sometimes cite a decision number, such as D22, or a section of the build plan. Those come from the project's internal design notes, which aren't published; the comment itself says what was decided.

## Useful flags

`Captly.exe` takes a few flags for development:

| Flag | What it does |
|---|---|
| `--settings [page]` | Opens Settings at start |
| `--setup` | Shows first-run setup again |
| `--overlay [seconds] [file.wav \| system \| app]` | Captions for a while without the tray. A WAV plays silently through the real pipeline |
| `--overlay-demo [app]` | Scripted captions with no audio or model, on an app's window if named |
| `--gallery` | Every control, in light and dark |
| `--ui-render <folder> [scale]` | Draws every screen to PNG |

Set `CAPTLY_DATA` to a folder to run with a fresh, separate profile.
