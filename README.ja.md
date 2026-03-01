# Moon Live Captions

[English README](README.md)

Moon Live Captions は、画面にリアルタイムの音声認識字幕を表示する Windows デスクトップアプリケーションです。スピーカー出力やマイクからの音声をキャプチャし、オンデバイスの [Moonshine](https://github.com/moonshine-ai/moonshine) ニューラルネットワークでローカル処理します。クラウドサービスや常時インターネット接続は不要です。

## 機能

- **リアルタイム文字起こし** — システムオーディオ（スピーカー）またはマイク、あるいは両方に対応
- **多言語対応** — 日本語・英語・スペイン語・韓国語・中国語・アラビア語・ウクライナ語・ベトナム語
- **表示モード** — 上部固定 / 下部固定 / フローティングウィンドウ
- **外観** — ライト / ダークテーマ、4 段階のフォントサイズ、背景の不透明度を調整可能
- **UI 言語** — 日本語または英語。初回起動時に Windows のシステム言語から自動検出
- **モデルの自動ダウンロード** — 初回使用時にモデルを自動取得し `%LOCALAPPDATA%\MoonLiveCaptions\Models\` にキャッシュ
- **セッション録音** — 音声と字幕テキストを実行ファイルと同じ場所の `Recordings\` フォルダに保存
- **設定の永続化** — すべての設定を `%LOCALAPPDATA%\MoonLiveCaptions\settings.ini` に保存
- **単一 exe** — NuGet の依存関係は Costura.Fody で exe に統合済み。配布に必要なファイルは exe + `moonshine.dll` + `onnxruntime.dll` のみ

## 動作要件

| 項目 | 内容 |
|---|---|
| OS | Windows 10 / 11（x64） |
| ランタイム | .NET Framework 4.8.1（Windows 11 には標準搭載） |
| ネイティブ DLL | `moonshine.dll` + `onnxruntime.dll`（後述） |

## はじめに

### リリース版をダウンロードする

[Releases](../../releases) ページから最新の `MoonLiveCaptions.zip` をダウンロードし、同一フォルダに展開してください。`MoonLiveCaptions.exe` を実行するだけで起動します。

初回起動時に Windows のシステム言語が検出され、日本語または英語の UI で起動します。選択した文字起こし言語のモデルが自動でダウンロードされます（言語によって 50〜150 MB 程度）。

### ソースからビルドする

#### 1. ネイティブ DLL を用意する

ネイティブライブラリはリポジトリに含まれていません。PyPI の `moonshine-voice` ホイールから取得できます。

```powershell
pip download "moonshine-voice==0.0.49" --no-deps --platform win_amd64 --only-binary :all: -d wheels/

$whl = Get-ChildItem wheels/*.whl | Select-Object -First 1
$zip = $whl.FullName -replace '\.whl$', '.zip'
Copy-Item $whl.FullName $zip
Expand-Archive -Path $zip -DestinationPath wheel_contents -Force
Copy-Item "wheel_contents/moonshine_voice/moonshine.dll" lib -Force
Copy-Item "wheel_contents/moonshine_voice/onnxruntime.dll" lib -Force
```

または、2 つの DLL を手動で `lib\` フォルダに配置しても構いません。

#### 2. ビルド

```powershell
dotnet restore MoonLiveCaptions.csproj
dotnet build MoonLiveCaptions.csproj -c Release --no-restore -p:Platform=x64
```

出力は `bin\Release\net481\` に生成されます。Visual Studio 2022 で `MoonLiveCaptions.sln` を開いてビルドすることも可能です。

## プロジェクト構成

```
MoonLiveCaptions.csproj      WPF プロジェクト（net481 / x64）
App.xaml(.cs)                アプリケーションエントリポイント。起動時に AppSettings を読み込む
CaptionWindow.xaml(.cs)      メインウィンドウ（ドッキング / フローティング制御）
Converters/                  WPF 値コンバーター
Helpers/
  AppBarManager.cs           エッジドッキング用の Win32 AppBar API ラッパー
  AppSettings.cs             設定の読み書き（key=valueの .ini 形式、外部ライブラリ不使用）
Native/
  MoonshineNative.cs         moonshine.dll の P/Invoke 宣言
Services/
  AudioCaptureService.cs     NAudio を使ったスピーカーループバック / マイクキャプチャ
  TranscriptionService.cs    Moonshine ストリーミング文字起こし + モデルダウンロード
ViewModels/
  CaptionViewModel.cs        メイン ViewModel（MVVM）。アプリ全体の状態を管理
  RelayCommand.cs            ICommand ヘルパー
  ViewModelBase.cs           INotifyPropertyChanged 基底クラス
lib/                         moonshine.dll と onnxruntime.dll をここに配置
```

## データ・設定の保存場所

| パス | 内容 |
|---|---|
| `%LOCALAPPDATA%\MoonLiveCaptions\settings.ini` | ユーザー設定 |
| `%LOCALAPPDATA%\MoonLiveCaptions\Models\` | ダウンロード済みモデルファイル |
| `<exe フォルダ>\Recordings\` | セッションの WAV 音声とテキスト字幕 |

## 技術スタック

- **WPF / .NET Framework 4.8.1** — UI フレームワーク
- **NAudio 2.2.1** — 音声キャプチャ（WASAPI ループバック + マイク）
- **Moonshine** — P/Invoke 経由のオンデバイス音声認識（`moonshine.dll`）
- **ONNX Runtime** — ニューラルネット推論バックエンド（`onnxruntime.dll`）
- **Costura.Fody** — ビルド時にマネージドアセンブリを exe に統合

## CI / リリース

GitHub Actions（`.github/workflows/build.yml`）が `main` / `master` へのプッシュごとに `windows-latest` 上でビルドします。タグをプッシュすると、exe と 2 つのネイティブ DLL をまとめた `MoonLiveCaptions.zip` を添付した GitHub Release が自動作成されます。

## ライセンス

[LICENSE](LICENSE) を参照してください。


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
