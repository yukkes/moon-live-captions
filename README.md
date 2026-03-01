# Moon Live Captions

[日本語版はこちら](README.ja.md)

Moon Live Captions is a Windows desktop application that shows real-time speech-to-text captions on screen. Audio is captured from the system speaker output and/or microphone and transcribed locally using the on-device [Moonshine](https://github.com/moonshine-ai/moonshine) neural network — no internet connection or cloud service is required during operation.

## Features

- **Real-time transcription** of system audio (speaker) and/or microphone
- **Multiple languages** — Japanese, English, Spanish, Korean, Chinese, Arabic, Ukrainian, Vietnamese
- **Display modes** — docked to top, docked to bottom, or free-floating window
- **Appearance** — Light / Dark theme, four font sizes, adjustable background opacity
- **UI language** — Japanese or English; auto-detected from Windows system language on first launch
- **Automatic model download** — model files are fetched on first use and cached in `%LOCALAPPDATA%\MoonLiveCaptions\Models\`
- **Session recording** — audio and transcript are saved to a `Recordings\` folder next to the executable
- **Settings persistence** — all preferences saved to `%LOCALAPPDATA%\MoonLiveCaptions\settings.ini`
- **Single-file executable** — NuGet dependencies are embedded via Costura.Fody; only `moonshine.dll` and `onnxruntime.dll` need to ship alongside the exe

## Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 / 11 (x64) |
| Runtime | .NET Framework 4.8.1 (pre-installed on Windows 11) |
| Native DLLs | `moonshine.dll` + `onnxruntime.dll` (see below) |

## Getting Started

### Download a Release

Download the latest `MoonLiveCaptions.zip` from the [Releases](../../releases) page and extract all files to a single folder. Run `MoonLiveCaptions.exe`.

On first launch the application detects the Windows system language and starts in Japanese or English accordingly. It will automatically download the model for the selected transcription language (~50–150 MB depending on language).

### Build from Source

#### 1. Obtain native DLLs

The native libraries are not checked into this repository. Extract them from the `moonshine-voice` PyPI wheel:

```powershell
pip download "moonshine-voice==0.0.49" --no-deps --platform win_amd64 --only-binary :all: -d wheels/

$whl = Get-ChildItem wheels/*.whl | Select-Object -First 1
$zip = $whl.FullName -replace '\.whl$', '.zip'
Copy-Item $whl.FullName $zip
Expand-Archive -Path $zip -DestinationPath wheel_contents -Force
Copy-Item "wheel_contents/moonshine_voice/moonshine.dll" lib -Force
Copy-Item "wheel_contents/moonshine_voice/onnxruntime.dll" lib -Force
```

Alternatively, place the two DLLs in the `lib\` folder by any other means.

#### 2. Build

```powershell
dotnet restore MoonLiveCaptions.csproj
dotnet build MoonLiveCaptions.csproj -c Release --no-restore -p:Platform=x64
```

The output is written to `bin\Release\net481\`. Or open `MoonLiveCaptions.sln` in Visual Studio 2022 and build from there.

## Project Structure

```
MoonLiveCaptions.csproj      WPF project targeting net481 / x64
App.xaml(.cs)                Application entry point; loads AppSettings on startup
CaptionWindow.xaml(.cs)      Main window (docking + floating logic)
Converters/                  WPF value converters
Helpers/
  AppBarManager.cs           Win32 AppBar API wrapper for edge-docking
  AppSettings.cs             Settings load/save (key=value .ini, no extra deps)
Native/
  MoonshineNative.cs         P/Invoke declarations for moonshine.dll
Services/
  AudioCaptureService.cs     NAudio-based speaker loopback + microphone capture
  TranscriptionService.cs    Moonshine streaming transcription + model download
ViewModels/
  CaptionViewModel.cs        Main view model (MVVM); manages all app state
  RelayCommand.cs            ICommand helper
  ViewModelBase.cs           INotifyPropertyChanged base
lib/                         Place moonshine.dll and onnxruntime.dll here
```

## Data & Settings Locations

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\MoonLiveCaptions\settings.ini` | User preferences |
| `%LOCALAPPDATA%\MoonLiveCaptions\Models\` | Downloaded model files |
| `<exe folder>\Recordings\` | Session WAV audio and transcript text files |

## Technology Stack

- **WPF / .NET Framework 4.8.1** — UI framework
- **NAudio 2.2.1** — audio capture (WASAPI loopback + microphone)
- **Moonshine** — on-device speech-to-text via P/Invoke (`moonshine.dll`)
- **ONNX Runtime** — neural network inference backend (`onnxruntime.dll`)
- **Costura.Fody** — embeds managed assemblies into the single exe at build time

## CI / Releases

GitHub Actions (`.github/workflows/build.yml`) builds on `windows-latest` for every push to `main` or `master`. Pushing a tag creates a GitHub Release with a `MoonLiveCaptions.zip` containing the exe and the two native DLLs.

## License

See [LICENSE](LICENSE).


## Transcription Engine

The project relies on the [moonshine](https://github.com/moonshine-ai/moonshine)
library for speech-to-text transcription. Moonshine is an open-source
neural network framework that provides pre-trained models and a simple
API for converting audio input into text. The application uses the
`moonshine.dll` native component, which is included via the `lib` folder
as described in `lib/README.md`.

For more information about moonshine, visit the GitHub repository:

https://github.com/moonshine-ai/moonshine

## Building and Running

The project includes a GitHub Actions workflow (`.github/workflows/build.yml`)
that demonstrates the full build process. It runs on `windows-latest` and
performs the following steps:

1. Checkout the repository.
2. Use Python to download the `moonshine-voice` wheel from PyPI (currently
   pinned to `0.0.49`) and extract the native `moonshine.dll` and
   `onnxruntime.dll` into the `lib` folder.
3. Setup the .NET SDK (9.x) and restore NuGet packages for
   `MoonLiveCaptions.csproj`.
4. Build the project in Release configuration targeting x64.
5. (CI-only) upload the resulting binaries as artifacts and create a
   GitHub release when the build is triggered by a tag.

You can follow the same procedure locally:

```powershell
# clone and navigate to repo
git clone https://github.com/yukkes/moon-live-captions.git
cd moon-live-captions

# ensure Python 3.13+ is available, then download/extract DLLs
pip download "moonshine-voice==0.0.49" --no-deps --platform win_amd64 --only-binary :all: -d wheels/

$whl = Get-ChildItem wheels/*.whl | Select-Object -First 1
Copy-Item $whl.FullName ($whl.FullName -replace '\.whl$','.zip')
# older PowerShell on windows-latest uses -DestinationPath
Expand-Archive -Path ($whl.FullName -replace '\.whl$','.zip') -DestinationPath wheel_contents -Force
Copy-Item "wheel_contents/moonshine_voice/moonshine.dll" lib -Force
Copy-Item "wheel_contents/moonshine_voice/onnxruntime.dll" lib -Force

# build with dotnet (requires .NET SDK on Windows)
dotnet restore MoonLiveCaptions.csproj
dotnet build MoonLiveCaptions.csproj -c Release --no-restore -p:Platform=x64
```

Alternatively, open `MoonLiveCaptions.sln` in Visual Studio after placing
the two DLLs manually in `lib`, then build and run the `MoonLiveCaptions`
project.
