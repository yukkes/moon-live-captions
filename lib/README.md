# Native DLLs for MoonLiveCaptions

This directory is intended to hold the two native libraries required by the
application:

- `moonshine.dll`
- `onnxruntime.dll`

These files are **not** checked into source control for licensing and size
reasons. Developers should obtain them manually (e.g. by downloading the
`moonshine-voice` wheel from PyPI and extracting, or by other means) and place
both DLLs in this folder.

The MSBuild project (`MoonLiveCaptions.csproj`) copies whatever is in this
`lib` folder into the output directory during the build. The GitHub Actions
workflow also downloads the wheel and extracts the DLLs here automatically on
CI.

If you prefer to build without CI assistance, just drop the DLLs here and the
project will pick them up.