# Moon Live Captions

Moon Live Captions は、WPF（.NET Framework 4.8）で構築された
Windows デスクトップアプリケーションで、システムからの音声を
キャプチャし、リアルタイムで字幕として表示します。会議や動画、
その他のライブプレゼンテーション中の音声コンテンツを
より追いやすくすることを目的としています。

## 文字起こしエンジン

プロジェクトでは音声からテキストへの変換に
[moonshine](https://github.com/moonshine-ai/moonshine) ライブラリを
利用しています。Moonshine はオープンソースのニューラルネット
ワークフレームワークで、学習済みモデルとシンプルな API を提供し、
音声入力をテキストに変換します。アプリケーションでは
`moonshine.dll` のネイティブコンポーネントを使用しており、
`lib` フォルダーを通じて含まれています（詳細は `lib/README.md` を
参照してください）。

詳細については GitHub リポジトリをご覧ください。

https://github.com/moonshine-ai/moonshine

## ビルドと実行

プロジェクトには GitHub Actions ワークフロー（`.github/workflows/build.yml`）
が含まれており、完全なビルド手順を示しています。ワークフローは
`windows-latest` 上で実行され、以下の処理を行います：

1. リポジトリのチェックアウト。
2. Python を使って PyPI から `moonshine-voice` ホイール
   (現時点では `0.0.49`) をダウンロードし、ネイティブの
   `moonshine.dll` と `onnxruntime.dll` を `lib` フォルダへ展開。
3. .NET SDK (9.x) をセットアップし、`MoonLiveCaptions.csproj` の
   NuGet パッケージをリストア。
4. Release 構成で x64 をターゲットにプロジェクトをビルド。
5. (CI のみ) 生成されたバイナリをアーティファクトとしてアップロードし、
   タグによるビルドでは GitHub Release を作成。

ローカルで同様の手順を実行するには：

```powershell
# リポジトリをクローンして移動
git clone https://github.com/yukkes/moon-live-captions.git
cd moon-live-captions

# Python 3.13 以上が必要。DLLをダウンロード/展開
pip download "moonshine-voice==0.0.49" --no-deps --platform win_amd64 --only-binary :all: -d wheels/

$whl = Get-ChildItem wheels/*.whl | Select-Object -First 1
Copy-Item $whl.FullName ($whl.FullName -replace '\.whl$','.zip')
# 注意: 古い PowerShell では -DestinationPath を使う必要がある
Expand-Archive -Path ($whl.FullName -replace '\.whl$',','.zip') -DestinationPath wheel_contents -Force
Copy-Item "wheel_contents/moonshine_voice/moonshine.dll" lib -Force
Copy-Item "wheel_contents/moonshine_voice/onnxruntime.dll" lib -Force

# dotnet でビルド (.NET SDK が Windows にインストールされている必要あり)
dotnet restore MoonLiveCaptions.csproj
dotnet build MoonLiveCaptions.csproj -c Release --no-restore -p:Platform=x64
```

または、2つの DLL を手動で `lib` に置いた後に
`MoonLiveCaptions.sln` を Visual Studio で開き、`MoonLiveCaptions`
プロジェクトをビルドして実行しても構いません。


## ドキュメント

[English README](README.md) も参照してください。
