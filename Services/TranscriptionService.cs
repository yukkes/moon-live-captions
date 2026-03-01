using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using MoonLiveCaptions.Helpers;
using MoonLiveCaptions.Native;

namespace MoonLiveCaptions.Services
{
    /// <summary>
    /// Moonshine streaming transcription service with robust error handling.
    /// Native calls are isolated on a dedicated thread pool thread to prevent
    /// AccessViolationException from crashing the application.
    /// </summary>
    public class TranscriptionService : IDisposable
    {
        private int _transcriberHandle = -1;
        private int _speakerStreamHandle = -1;
        private int _micStreamHandle = -1;
        private bool _isInitialized;
        private readonly object _lock = new object();
        private string _currentLanguage;
        private uint _currentModelArch;
        private int _speakerLastLineCount;
        private int _micLastLineCount;

        public bool IsInitialized => _isInitialized;
        public string CurrentLanguage => _currentLanguage;


        public event EventHandler<TranscriptUpdate> TranscriptUpdated;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<Exception> ErrorOccurred;
        public event EventHandler<int> ModelDownloadProgress;

        /// <summary>
        /// Initialize the Moonshine transcriber. Downloads model automatically if needed.
        /// Runs native initialization on a thread-pool thread.
        /// </summary>
        public async Task InitializeAsync(string language, string modelSize = "base")
        {
            try
            {
                StatusChanged?.Invoke(this, "モデルを準備中...");

                string modelDir = Path.Combine(
                    AppSettings.ModelsDir,
                    string.Format("moonshine-{0}", language));

                _currentModelArch = MoonshineNative.MODEL_ARCH_BASE;

                string encoderPath   = Path.Combine(modelDir, "encoder_model.ort");
                string decoderPath   = Path.Combine(modelDir, "decoder_model_merged.ort");
                string tokenizerPath = Path.Combine(modelDir, "tokenizer.bin");

                if (!ValidateModelFiles(encoderPath, decoderPath, tokenizerPath))
                {
                    // Remove partial/corrupt files and re-download
                    CleanDir(modelDir);
                    Directory.CreateDirectory(modelDir);
                    StatusChanged?.Invoke(this, "モデルをダウンロード中...");
                    await DownloadModelAsync(language, modelSize, modelDir);

                    if (!ValidateModelFiles(encoderPath, decoderPath, tokenizerPath))
                        throw new Exception("モデルファイルの検証に失敗しました。ネットワーク接続を確認してください。");
                }

                DisposeTranscriber();
                StatusChanged?.Invoke(this, "モデルを読み込み中...");

                // Run native P/Invoke on thread-pool to isolate native exceptions.
                // LoadNativeSafe has [HandleProcessCorruptedStateExceptions] on the method
                // that contains the try/catch, which is the required placement for .NET 4.x.
                var lr = new LoadResult();
                await Task.Run(() => LoadNativeSafe(modelDir, language, lr));

                if (lr.Error != null)
                    throw new Exception("Moonshine ネイティブ初期化失敗: " + lr.Error.Message, lr.Error);

                if (lr.Handle < 0)
                    throw new Exception("Moonshine 初期化エラー: " + MoonshineNative.GetErrorString(lr.Handle));

                int handle = lr.Handle;

                _transcriberHandle = handle;
                _currentLanguage   = language;
                _isInitialized     = true;

                Debug.WriteLine("Moonshine loaded. Handle=" + handle +
                    " Version=" + MoonshineNative.moonshine_get_version());

                StatusChanged?.Invoke(this, "準備完了");
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                StatusChanged?.Invoke(this, "初期化エラー: " + ex.Message);
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        // ──────────────────────────────────────────────────────────
        // Native initialization (isolated to catch AVE from bad models)
        // ──────────────────────────────────────────────────────────

        private sealed class LoadResult
        {
            public int Handle = -1;
            public Exception Error;
        }

        /// <summary>
        /// [HPSE] MUST be on the method that contains the try/catch to catch
        /// AccessViolationException / CSEs in .NET 4.x.  This is the correct placement.
        /// </summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void LoadNativeSafe(string modelDir, string language, LoadResult result)
        {
            try
            {
                result.Handle = LoadTranscriberNative(modelDir, language);
            }
            catch (Exception ex)
            {
                result.Error = ex;
            }
        }

        private int LoadTranscriberNative(string modelDir, string language)
        {
            if (language == "en")
            {
                return MoonshineNative.moonshine_load_transcriber_from_files(
                    modelDir, _currentModelArch, IntPtr.Zero, 0, MoonshineNative.MOONSHINE_HEADER_VERSION);
            }

            // Non-English: pass max_tokens_per_second option.
            // We manually marshal the struct to avoid the AccessViolationException caused
            // by [MarshalAs(UnmanagedType.LPArray)] on non-blittable (LPStr) structs.
            IntPtr namePtr  = IntPtr.Zero;
            IntPtr valuePtr = IntPtr.Zero;
            IntPtr optsPtr  = IntPtr.Zero;
            try
            {
                namePtr  = Marshal.StringToHGlobalAnsi("max_tokens_per_second");
                valuePtr = Marshal.StringToHGlobalAnsi("13.0");

                // Native struct layout: { char* name; char* value; }
                optsPtr = Marshal.AllocHGlobal(IntPtr.Size * 2);
                Marshal.WriteIntPtr(optsPtr, 0,           namePtr);
                Marshal.WriteIntPtr(optsPtr, IntPtr.Size, valuePtr);

                return MoonshineNative.moonshine_load_transcriber_from_files(
                    modelDir, _currentModelArch, optsPtr, 1, MoonshineNative.MOONSHINE_HEADER_VERSION);
            }
            finally
            {
                if (optsPtr  != IntPtr.Zero) Marshal.FreeHGlobal(optsPtr);
                if (namePtr  != IntPtr.Zero) Marshal.FreeHGlobal(namePtr);
                if (valuePtr != IntPtr.Zero) Marshal.FreeHGlobal(valuePtr);
            }
        }

        // ──────────────────────────────────────────────────────────
        // Model file helpers
        // ──────────────────────────────────────────────────────────

        private static bool ValidateModelFiles(string encoder, string decoder, string tokenizer)
        {
            foreach (string path in new[] { encoder, decoder, tokenizer })
            {
                if (!File.Exists(path)) return false;
                if (new FileInfo(path).Length < 1024) return false; // empty/partial
            }
            return true;
        }

        private static void CleanDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { Debug.WriteLine("CleanDir: " + ex.Message); }
        }

        private async Task DownloadModelAsync(string language, string modelSize, string modelDir)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string baseUrl = string.Format(
                "https://download.moonshine.ai/model/{0}-{1}/quantized/{0}-{1}",
                modelSize, language);

            string[] files = { "encoder_model.ort", "decoder_model_merged.ort", "tokenizer.bin" };

            for (int i = 0; i < files.Length; i++)
            {
                string file      = files[i];
                string localPath = Path.Combine(modelDir, file);
                string url       = string.Format("{0}/{1}", baseUrl, file);

                StatusChanged?.Invoke(this, string.Format(
                    "ダウンロード: {0}  ({1}/{2})", file, i + 1, files.Length));

                int idx = i;
                try
                {
                    using (var client = new WebClient())
                    {
                        client.DownloadProgressChanged += (s, e) =>
                        {
                            int overall = (idx * 100 + e.ProgressPercentage) / files.Length;
                            ModelDownloadProgress?.Invoke(this, overall);
                        };
                        await client.DownloadFileTaskAsync(new Uri(url), localPath);
                    }
                    Debug.WriteLine(string.Format("Downloaded {0}: {1} bytes",
                        file, new FileInfo(localPath).Length));
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
                    throw new Exception(string.Format("{0} のダウンロードに失敗: {1}", file, ex.Message), ex);
                }
            }

            StatusChanged?.Invoke(this, "ダウンロード完了");
        }

        // ──────────────────────────────────────────────────────────
        // Stream management
        // ──────────────────────────────────────────────────────────

        public int CreateAndStartStream()
        {
            if (!_isInitialized || _transcriberHandle < 0)
                throw new InvalidOperationException("Transcriber not initialized");

            int h = MoonshineNative.moonshine_create_stream(_transcriberHandle, 0);
            if (h < 0) throw new Exception("ストリーム作成エラー: " + MoonshineNative.GetErrorString(h));

            int r = MoonshineNative.moonshine_start_stream(_transcriberHandle, h);
            if (r != 0)
            {
                MoonshineNative.moonshine_free_stream(_transcriberHandle, h);
                throw new Exception("ストリーム開始エラー: " + MoonshineNative.GetErrorString(r));
            }
            return h;
        }

        public void StartSpeakerStream()
        {
            lock (_lock) { if (_speakerStreamHandle >= 0) StopSpeakerStream(); _speakerStreamHandle = CreateAndStartStream(); _speakerLastLineCount = 0; }
        }

        public void StartMicStream()
        {
            lock (_lock) { if (_micStreamHandle >= 0) StopMicStream(); _micStreamHandle = CreateAndStartStream(); _micLastLineCount = 0; }
        }

        public void AddSpeakerAudio(float[] samples, int sampleRate = 16000)
        {
            lock (_lock)
            {
                if (_transcriberHandle < 0 || _speakerStreamHandle < 0) return;
                int r = MoonshineNative.moonshine_transcribe_add_audio_to_stream(
                    _transcriberHandle, _speakerStreamHandle, samples, (ulong)samples.Length, sampleRate, 0);
                if (r != 0)
                    Debug.WriteLine("AddSpeakerAudio error: " + MoonshineNative.GetErrorString(r));
            }
        }

        public void AddMicAudio(float[] samples, int sampleRate = 16000)
        {
            lock (_lock)
            {
                if (_transcriberHandle < 0 || _micStreamHandle < 0) return;
                MoonshineNative.moonshine_transcribe_add_audio_to_stream(
                    _transcriberHandle, _micStreamHandle, samples, (ulong)samples.Length, sampleRate, 0);
            }
        }

        public TranscriptLineManaged[] UpdateSpeakerTranscription(bool forceUpdate = false)
        {
            lock (_lock)
            {
                if (_transcriberHandle < 0 || _speakerStreamHandle < 0) return new TranscriptLineManaged[0];
                return RunStream(_speakerStreamHandle, "Speaker", ref _speakerLastLineCount, forceUpdate);
            }
        }

        public TranscriptLineManaged[] UpdateMicTranscription(bool forceUpdate = false)
        {
            lock (_lock)
            {
                if (_transcriberHandle < 0 || _micStreamHandle < 0) return new TranscriptLineManaged[0];
                return RunStream(_micStreamHandle, "Mic", ref _micLastLineCount, forceUpdate);
            }
        }

        private TranscriptLineManaged[] RunStream(int h, string source, ref int lastCount, bool force)
        {
            try
            {
                uint flags = force ? MoonshineNative.FLAG_FORCE_UPDATE : 0;
                int r = MoonshineNative.moonshine_transcribe_stream(_transcriberHandle, h, flags, out IntPtr ptr);

                if (r != 0) { Debug.WriteLine($"Stream error ({source}): {MoonshineNative.GetErrorString(r)}"); return new TranscriptLineManaged[0]; }

                var lines = MoonshineNative.ReadTranscript(ptr);
                if (lines.Length > 0)
                    TranscriptUpdated?.Invoke(this, new TranscriptUpdate { Source = source, Lines = lines, PreviousLineCount = lastCount });

                lastCount = lines.Length;
                return lines;
            }
            catch (Exception ex) { Debug.WriteLine($"Transcription error ({source}): {ex.Message}"); return new TranscriptLineManaged[0]; }
        }

        public void StopSpeakerStream()
        {
            lock (_lock)
            {
                if (_transcriberHandle >= 0 && _speakerStreamHandle >= 0)
                {
                    try { MoonshineNative.moonshine_stop_stream(_transcriberHandle, _speakerStreamHandle); } catch { }
                    try { MoonshineNative.moonshine_free_stream(_transcriberHandle, _speakerStreamHandle); } catch { }
                    _speakerStreamHandle = -1;
                }
            }
        }

        public void StopMicStream()
        {
            lock (_lock)
            {
                if (_transcriberHandle >= 0 && _micStreamHandle >= 0)
                {
                    try { MoonshineNative.moonshine_stop_stream(_transcriberHandle, _micStreamHandle); } catch { }
                    try { MoonshineNative.moonshine_free_stream(_transcriberHandle, _micStreamHandle); } catch { }
                    _micStreamHandle = -1;
                }
            }
        }

        private void DisposeTranscriber()
        {
            lock (_lock)
            {
                StopSpeakerStream();
                StopMicStream();
                if (_transcriberHandle >= 0)
                {
                    try { MoonshineNative.moonshine_free_transcriber(_transcriberHandle); } catch { }
                    _transcriberHandle = -1;
                }
                _isInitialized = false;
            }
        }

        public void Dispose() => DisposeTranscriber();
    }

    public class TranscriptUpdate
    {
        public string Source { get; set; }
        public TranscriptLineManaged[] Lines { get; set; }
        public int PreviousLineCount { get; set; }
    }
}
