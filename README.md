# Moon Live Captions

[日本語版はこちら](README.ja.md)

Moon Live Captions is a Windows desktop application built with WPF
(.NET Framework 4.8) that captures audio from the system and displays
real-time transcriptions as on-screen captions. It is designed to make
spoken content easier to follow during meetings, videos, or other live
presentations.

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
