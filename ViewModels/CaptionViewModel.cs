using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MoonLiveCaptions.Native;
using MoonLiveCaptions.Services;
using NAudio.Wave;

namespace MoonLiveCaptions.ViewModels
{
    // ── Enums ─────────────────────────────────────────────────────

    public enum DisplayMode    { Top, Bottom, Floating }
    public enum FontSizeOption { Small, Normal, Large, ExtraLarge }
    public enum CaptionTheme   { Light, Dark }
    public enum UILang         { ja, en }

    public class LanguageChoice
    {
        public string DisplayName { get; }
        public string Code        { get; }
        public LanguageChoice(string displayName, string code) { DisplayName = displayName; Code = code; }
        public override string ToString() => DisplayName;
    }

    // ── ViewModel ─────────────────────────────────────────────────

    public class CaptionViewModel : ViewModelBase, IDisposable
    {
        // ── Services ──────────────────────────────────────────────
        private readonly AudioCaptureService   _audioService;
        private readonly TranscriptionService  _transcriptionService;
        private readonly DispatcherTimer       _transcriptionTimer;
        private readonly DispatcherTimer       _idleTimer;
        private readonly Dispatcher            _dispatcher;

        // ── WAV saving ────────────────────────────────────────────
        private WaveFileWriter _speakerWavWriter;
        private WaveFileWriter _micWavWriter;
        private readonly object _speakerWavLock = new object();
        private readonly object _micWavLock     = new object();
        private StreamWriter   _transcriptWriter;
        private readonly object _transcriptLock = new object();
        private string _sessionDirectory;
        private readonly HashSet<ulong> _writtenLineIds = new HashSet<ulong>();

        // ── Transcript model ──────────────────────────────────────
        private readonly List<CaptionLine> _speakerLines = new List<CaptionLine>();
        private readonly List<CaptionLine> _micLines     = new List<CaptionLine>();
        private DateTime _lastSpeechTime = DateTime.Now;
        private const int MaxDisplayLines = 150;

        // ── Events ────────────────────────────────────────────────
        public event EventHandler<DisplayMode> DisplayModeChanged;
        public event EventHandler              CaptionTextUpdated;
        public event EventHandler              CloseRequested;

        // ══════════════════════════════════════════════════════════
        // Constructor
        // ══════════════════════════════════════════════════════════
        public CaptionViewModel()
        {
            _dispatcher           = Application.Current.Dispatcher;
            _audioService         = new AudioCaptureService();
            _transcriptionService = new TranscriptionService();

            _transcriptionService.TranscriptUpdated       += OnTranscriptUpdated;
            _transcriptionService.StatusChanged           += OnStatusChanged;
            _transcriptionService.ErrorOccurred           += OnErrorOccurred;
            _transcriptionService.ModelDownloadProgress   += OnModelDownloadProgress;

            _transcriptionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _transcriptionTimer.Tick += OnTranscriptionTimerTick;

            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _idleTimer.Tick += OnIdleTimerTick;

            Languages = new List<LanguageChoice>
            {
                new LanguageChoice("日本語", "ja"),
                new LanguageChoice("English", "en"),
                new LanguageChoice("Español", "es"),
                new LanguageChoice("한국어", "ko"),
                new LanguageChoice("中文", "zh"),
                new LanguageChoice("العربية", "ar"),
                new LanguageChoice("Українська", "uk"),
                new LanguageChoice("Tiếng Việt", "vi"),
            };
            _selectedLanguage = Languages[0];

            ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
            CloseCommand          = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
            SetDisplayModeCommand = new RelayCommand<string>(m =>
            {
                if (Enum.TryParse(m, out DisplayMode dm)) CurrentDisplayMode = dm;
            });
            SetFontSizeCommand = new RelayCommand<string>(s =>
            {
                if (Enum.TryParse(s, out FontSizeOption fs)) CurrentFontSize = fs;
            });
            ChangeLanguageCommand = new RelayCommand(async () => await ChangeLanguageAsync());
            SetThemeCommand      = new RelayCommand<string>(t =>
            {
                if (Enum.TryParse(t, out CaptionTheme ct)) CurrentTheme = ct;
            });
            SetUILanguageCommand = new RelayCommand<string>(l =>
            {
                if (Enum.TryParse(l, out UILang ul)) UILanguage = ul;
            });

            _ = InitializeAndStartAsync();
        }

        // ══════════════════════════════════════════════════════════
        // Properties – State
        // ══════════════════════════════════════════════════════════

        private DisplayMode _displayMode = DisplayMode.Floating;
        public DisplayMode CurrentDisplayMode
        {
            get => _displayMode;
            set
            {
                if (SetProperty(ref _displayMode, value))
                {
                    DisplayModeChanged?.Invoke(this, value);
                    OnPropertyChanged(nameof(IsFloating));
                }
            }
        }

        public bool IsFloating => _displayMode == DisplayMode.Floating;

        private bool _captureSystemAudio = true;
        public bool CaptureSystemAudio
        {
            get => _captureSystemAudio;
            set { if (SetProperty(ref _captureSystemAudio, value)) RestartCapture(); }
        }

        private bool _captureMicrophone;
        public bool CaptureMicrophone
        {
            get => _captureMicrophone;
            set { if (SetProperty(ref _captureMicrophone, value)) RestartCapture(); }
        }

        // ══════════════════════════════════════════════════════════
        // Properties – Visual
        // ══════════════════════════════════════════════════════════

        private FontSizeOption _fontSize = FontSizeOption.Normal;
        public FontSizeOption CurrentFontSize
        {
            get => _fontSize;
            set { if (SetProperty(ref _fontSize, value)) OnPropertyChanged(nameof(CaptionFontSize)); }
        }

        public double CaptionFontSize
        {
            get
            {
                switch (_fontSize)
                {
                    case FontSizeOption.Small:      return 12;
                    case FontSizeOption.Normal:     return 14;
                    case FontSizeOption.Large:      return 18;
                    case FontSizeOption.ExtraLarge: return 24;
                    default:                        return 14;
                }
            }
        }

        // ══════════════════════════════════════════════════════════
        // Properties – Theme / Appearance
        // ══════════════════════════════════════════════════════════

        private CaptionTheme _theme = CaptionTheme.Light;
        public CaptionTheme CurrentTheme
        {
            get => _theme;
            set
            {
                if (SetProperty(ref _theme, value))
                {
                    OnPropertyChanged(nameof(BackgroundBrush));
                    OnPropertyChanged(nameof(ForegroundBrush));
                    OnPropertyChanged(nameof(SeparatorBrush));
                    OnPropertyChanged(nameof(DragPillBrush));
                }
            }
        }

        private double _backgroundOpacity = 1.0;
        public double BackgroundOpacity
        {
            get => _backgroundOpacity;
            set { if (SetProperty(ref _backgroundOpacity, value)) OnPropertyChanged(nameof(BackgroundBrush)); }
        }

        public SolidColorBrush BackgroundBrush
        {
            get
            {
                Color c = _theme == CaptionTheme.Dark
                    ? Color.FromRgb(0x1E, 0x1E, 0x1E)
                    : Color.FromRgb(0xF9, 0xF9, 0xF9);
                return new SolidColorBrush(Color.FromArgb((byte)(_backgroundOpacity * 255), c.R, c.G, c.B));
            }
        }

        public SolidColorBrush ForegroundBrush => _theme == CaptionTheme.Dark
            ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
            : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));

        public SolidColorBrush SeparatorBrush => _theme == CaptionTheme.Dark
            ? new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38))
            : new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));

        public SolidColorBrush DragPillBrush => _theme == CaptionTheme.Dark
            ? new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))
            : new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));

        // ══════════════════════════════════════════════════════════
        // Properties – UI Language
        // ══════════════════════════════════════════════════════════

        private UILang _uiLang = UILang.ja;
        private UIStringsProvider _ui = new UIStringsProvider(UILang.ja);

        public UILang UILanguage
        {
            get => _uiLang;
            set
            {
                if (_uiLang != value)
                {
                    _uiLang = value;
                    _ui = new UIStringsProvider(value);
                    OnPropertyChanged(nameof(UILanguage));
                    OnPropertyChanged(nameof(UI));
                    OnPropertyChanged(nameof(PlaceholderText));
                }
            }
        }

        public UIStringsProvider UI => _ui;

        public string PlaceholderText
        {
            get
            {
                string name = _selectedLanguage?.DisplayName ?? "日本語";
                return _uiLang == UILang.en
                    ? "Ready to show " + name + " captions"
                    : name + " のライブ キャプションを表示する準備ができました";
            }
        }

        // ══════════════════════════════════════════════════════════
        // Properties – Caption / Status
        // ══════════════════════════════════════════════════════════

        private string _captionText = "";
        public string CaptionText
        {
            get => _captionText;
            private set => SetProperty(ref _captionText, value);
        }

        private bool _isListening = true;
        public bool IsListening
        {
            get => _isListening;
            set => SetProperty(ref _isListening, value);
        }

        private bool _isCapturing;
        public bool IsCapturing
        {
            get => _isCapturing;
            private set => SetProperty(ref _isCapturing, value);
        }

        private string _headerStatus = "";
        public string HeaderStatus
        {
            get => _headerStatus;
            private set => SetProperty(ref _headerStatus, value);
        }

        private string _statusText = "初期化中...";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isInitializing = true;
        public bool IsInitializing
        {
            get => _isInitializing;
            set => SetProperty(ref _isInitializing, value);
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        private int _downloadProgress;
        public int DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        private bool _isSettingsOpen;
        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => SetProperty(ref _isSettingsOpen, value);
        }

        private string _sessionInfo = "";
        public string SessionInfo
        {
            get => _sessionInfo;
            private set => SetProperty(ref _sessionInfo, value);
        }

        // ══════════════════════════════════════════════════════════
        // Properties – Language
        // ══════════════════════════════════════════════════════════

        public List<LanguageChoice> Languages { get; }

        private LanguageChoice _selectedLanguage;
        public LanguageChoice SelectedLanguage
        {
            get => _selectedLanguage;
            set { if (SetProperty(ref _selectedLanguage, value)) OnPropertyChanged(nameof(PlaceholderText)); }
        }

        // ══════════════════════════════════════════════════════════
        // Commands
        // ══════════════════════════════════════════════════════════

        public RelayCommand           ToggleSettingsCommand  { get; }
        public RelayCommand           CloseCommand           { get; }
        public RelayCommand<string>   SetDisplayModeCommand  { get; }
        public RelayCommand<string>   SetFontSizeCommand     { get; }
        public RelayCommand           ChangeLanguageCommand   { get; }
        public RelayCommand<string>   SetThemeCommand        { get; }
        public RelayCommand<string>   SetUILanguageCommand   { get; }

        // ══════════════════════════════════════════════════════════
        // Initialization
        // ══════════════════════════════════════════════════════════

        private async Task InitializeAndStartAsync()
        {
            try
            {
                IsInitializing = true;
                await _transcriptionService.InitializeAsync(_selectedLanguage.Code);
                StartCapture();
                IsInitializing = false;
                IsCapturing    = true;
                IsListening    = true;
                _lastSpeechTime = DateTime.Now;
                StatusText     = "音声を待機中...";
                HeaderStatus   = "● 録音中";
                _transcriptionTimer.Start();
                _idleTimer.Start();
            }
            catch (Exception ex)
            {
                IsInitializing  = false;
                IsDownloading   = false;
                StatusText      = "エラー: " + ex.Message;
                Debug.WriteLine("InitializeAndStartAsync error: " + ex);
            }
        }

        // ══════════════════════════════════════════════════════════
        // Audio capture + WAV saving
        // ══════════════════════════════════════════════════════════

        private void StartCapture()
        {
            // Unhook any previous handlers
            _audioService.MicSamplesAvailable     -= OnMicSamples;
            _audioService.SpeakerSamplesAvailable -= OnSpeakerSamples;
            _audioService.StopCapture();
            _transcriptionService.StopSpeakerStream();
            _transcriptionService.StopMicStream();

            // Create new session directory for saving
            OpenSessionFiles();

            if (_captureSystemAudio) _transcriptionService.StartSpeakerStream();
            if (_captureMicrophone)  _transcriptionService.StartMicStream();

            _audioService.MicSamplesAvailable     += OnMicSamples;
            _audioService.SpeakerSamplesAvailable += OnSpeakerSamples;

            string speakerId = _captureSystemAudio ? _audioService.GetDefaultSpeakerDeviceId() : null;
            string micId     = _captureMicrophone  ? _audioService.GetDefaultMicDeviceId()     : null;

            _audioService.StartCapture(micId, speakerId, _captureMicrophone, _captureSystemAudio);
        }

        private void OpenSessionFiles()
        {
            CloseSessionFiles();

            try
            {
                _sessionDirectory = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Recordings",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_sessionDirectory);

                var waveFormat = new WaveFormat(16000, 16, 1);

                if (_captureSystemAudio)
                {
                    string path = Path.Combine(_sessionDirectory, "speaker.wav");
                    _speakerWavWriter = new WaveFileWriter(path, waveFormat);
                }
                if (_captureMicrophone)
                {
                    string path = Path.Combine(_sessionDirectory, "mic.wav");
                    _micWavWriter = new WaveFileWriter(path, waveFormat);
                }

                string txtPath = Path.Combine(_sessionDirectory, "transcript.txt");
                _transcriptWriter = new StreamWriter(txtPath, append: false, encoding: Encoding.UTF8);
                _transcriptWriter.AutoFlush = true;
                _transcriptWriter.WriteLine("# MoonLiveCaptions Transcript");
                _transcriptWriter.WriteLine("# Session: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _transcriptWriter.WriteLine("# Language: " + _selectedLanguage?.DisplayName);
                _transcriptWriter.WriteLine();

                SessionInfo = "録音中: " + _sessionDirectory;
                Debug.WriteLine("Session dir: " + _sessionDirectory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OpenSessionFiles error: " + ex.Message);
                SessionInfo = "";
            }
        }

        private void CloseSessionFiles()
        {
            lock (_speakerWavLock)
            {
                try { _speakerWavWriter?.Flush(); _speakerWavWriter?.Dispose(); } catch { }
                _speakerWavWriter = null;
            }
            lock (_micWavLock)
            {
                try { _micWavWriter?.Flush(); _micWavWriter?.Dispose(); } catch { }
                _micWavWriter = null;
            }
            lock (_transcriptLock)
            {
                try { _transcriptWriter?.Flush(); _transcriptWriter?.Dispose(); } catch { }
                _transcriptWriter = null;
                _writtenLineIds.Clear();
            }
        }

        private void RestartCapture()
        {
            if (!_transcriptionService.IsInitialized) return;
            _speakerLines.Clear();
            _micLines.Clear();
            CaptionText = "";
            StartCapture();
        }

        // ── Audio sample handlers ─────────────────────────────────

        private void OnSpeakerSamples(object sender, float[] samples)
        {
            _transcriptionService.AddSpeakerAudio(samples);

            lock (_speakerWavLock)
            {
                if (_speakerWavWriter != null)
                {
                    foreach (float s in samples)
                        _speakerWavWriter.WriteSample(Clamp(s));
                }
            }
        }

        private void OnMicSamples(object sender, float[] samples)
        {
            _transcriptionService.AddMicAudio(samples);

            lock (_micWavLock)
            {
                if (_micWavWriter != null)
                {
                    foreach (float s in samples)
                        _micWavWriter.WriteSample(Clamp(s));
                }
            }
        }

        private static float Clamp(float v) => v < -1f ? -1f : (v > 1f ? 1f : v);

        // ══════════════════════════════════════════════════════════
        // Transcription
        // ══════════════════════════════════════════════════════════

        private void OnTranscriptionTimerTick(object sender, EventArgs e)
        {
            if (IsInitializing) return;
            Task.Run(() =>
            {
                if (_captureSystemAudio) _transcriptionService.UpdateSpeakerTranscription();
                if (_captureMicrophone)  _transcriptionService.UpdateMicTranscription();
            });
        }

        private void OnIdleTimerTick(object sender, EventArgs e)
        {
            if (IsInitializing) return;
            bool idle = (DateTime.Now - _lastSpeechTime).TotalSeconds > 3.5;
            IsListening = idle;
            if (IsCapturing)
                HeaderStatus = idle ? "● 待機中" : "● 録音中";
        }

        private void OnTranscriptUpdated(object sender, TranscriptUpdate update)
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                var list = update.Source == "Speaker" ? _speakerLines : _micLines;

                foreach (var line in update.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.Text)) continue;

                    var existing = list.FirstOrDefault(l => l.LineId == line.LineId);
                    if (existing != null)
                    {
                        existing.Text       = CleanCjkSpaces(line.Text);
                        existing.IsComplete = line.IsComplete;
                    }
                    else
                    {
                        list.Add(new CaptionLine
                        {
                            LineId     = line.LineId,
                            Source     = update.Source,
                            Text       = CleanCjkSpaces(line.Text),
                            IsComplete = line.IsComplete,
                            StartTime  = line.StartTime,
                            Timestamp  = DateTime.Now
                        });
                    }
                }

                TrimLines(list);
                _lastSpeechTime = DateTime.Now;
                IsListening     = false;

                // Write completed new lines to transcript file
                AppendNewLinesToFile(update);

                RebuildCaptionText();
            }));
        }

        private void AppendNewLinesToFile(TranscriptUpdate update)
        {
            lock (_transcriptLock)
            {
                if (_transcriptWriter == null) return;
                try
                {
                    foreach (var line in update.Lines)
                    {
                        if (line.IsComplete
                            && !string.IsNullOrWhiteSpace(line.Text)
                            && !_writtenLineIds.Contains(line.LineId))
                        {
                            _writtenLineIds.Add(line.LineId);
                            string ts     = DateTime.Now.ToString("HH:mm:ss");
                            string source = update.Source == "Speaker" ? "[スピーカー]" : "[マイク]";
                            _transcriptWriter.WriteLine(string.Format("{0} {1} {2}", ts, source, CleanCjkSpaces(line.Text)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("AppendNewLinesToFile: " + ex.Message);
                }
            }
        }

        private void TrimLines(List<CaptionLine> lines)
        {
            while (lines.Count > MaxDisplayLines)
            {
                int idx = lines.FindIndex(l => l.IsComplete);
                lines.RemoveAt(idx >= 0 ? idx : 0);
            }
        }

        private void RebuildCaptionText()
        {
            var all = new List<CaptionLine>(_speakerLines.Count + _micLines.Count);
            all.AddRange(_speakerLines);
            all.AddRange(_micLines);
            all.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            var sb = new StringBuilder();
            foreach (var line in all)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line.Text);
            }

            CaptionText = sb.ToString();
            CaptionTextUpdated?.Invoke(this, EventArgs.Empty);
        }

        // ══════════════════════════════════════════════════════════
        // Language change
        // ══════════════════════════════════════════════════════════

        private async Task ChangeLanguageAsync()
        {
            if (_selectedLanguage == null) return;
            try
            {
                IsSettingsOpen = false;
                _transcriptionTimer.Stop();
                _audioService.StopCapture();
                _transcriptionService.StopSpeakerStream();
                _transcriptionService.StopMicStream();
                CloseSessionFiles();

                _speakerLines.Clear();
                _micLines.Clear();
                CaptionText = "";

                IsInitializing = true;
                await _transcriptionService.InitializeAsync(_selectedLanguage.Code);
                IsInitializing = false;

                StartCapture();
                _transcriptionTimer.Start();
            }
            catch (Exception ex)
            {
                IsInitializing = false;
                StatusText     = "言語切替エラー: " + ex.Message;
            }
        }

        // ══════════════════════════════════════════════════════════
        // Service event dispatchers
        // ══════════════════════════════════════════════════════════

        private void OnStatusChanged(object sender, string status)
            => _dispatcher.BeginInvoke(new Action(() => StatusText = status));

        private void OnErrorOccurred(object sender, Exception ex)
            => _dispatcher.BeginInvoke(new Action(() =>
            {
                StatusText    = "エラー: " + ex.Message;
                IsDownloading = false;
                Debug.WriteLine("Service error: " + ex);
            }));

        private void OnModelDownloadProgress(object sender, int progress)
            => _dispatcher.BeginInvoke(new Action(() =>
            {
                IsDownloading    = true;
                DownloadProgress = progress;
            }));

        // ══════════════════════════════════════════════════════════
        // CJK space cleanup
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Remove single spaces between CJK characters.
        /// Moonshine streaming tokenizer inserts spaces between individual Japanese/CJK tokens.
        /// </summary>
        private static string CleanCjkSpaces(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ' && i > 0 && i < text.Length - 1
                    && IsCjk(text[i - 1]) && IsCjk(text[i + 1]))
                    continue;
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        private static bool IsCjk(char c)
        {
            // Hiragana, Katakana, CJK Unified, CJK Punctuation, Fullwidth forms, ASCII digits
            return (c >= '\u3000' && c <= '\u9FFF') || (c >= '\uFF00' && c <= '\uFFEF')
                || (c >= '0' && c <= '9');
        }

        // ══════════════════════════════════════════════════════════
        // Dispose
        // ══════════════════════════════════════════════════════════

        public void Dispose()
        {
            _transcriptionTimer.Stop();
            _idleTimer.Stop();

            _audioService.MicSamplesAvailable     -= OnMicSamples;
            _audioService.SpeakerSamplesAvailable -= OnSpeakerSamples;
            _audioService.StopCapture();
            _audioService.Dispose();

            _transcriptionService.StopSpeakerStream();
            _transcriptionService.StopMicStream();
            _transcriptionService.Dispose();

            CloseSessionFiles();
        }

        // ══════════════════════════════════════════════════════════
        // Inner types
        // ══════════════════════════════════════════════════════════

        private class CaptionLine
        {
            public ulong    LineId     { get; set; }
            public string   Source     { get; set; }
            public string   Text       { get; set; }
            public bool     IsComplete { get; set; }
            public float    StartTime  { get; set; }
            public DateTime Timestamp  { get; set; }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // UI string provider — two-language support (ja / en)
    // ══════════════════════════════════════════════════════════════

    public sealed class UIStringsProvider
    {
        private readonly UILang _lang;
        public UIStringsProvider(UILang lang) { _lang = lang; }

        private string J(string ja, string en) => _lang == UILang.en ? en : ja;

        public string LabelPosition    => J("表示位置",                          "Position");
        public string LabelAppearance  => J("外観",                              "Appearance");
        public string LabelAudio       => J("オーディオソース",                  "Audio source");
        public string LabelFontSize    => J("文字サイズ",                        "Text size");
        public string LabelTranscription => J("文字起こし言語",                 "Transcription language");
        public string LabelUILanguage  => J("UI言語",                           "UI language");
        public string LabelOpacity     => J("背景の不透明度",                   "Background opacity");
        public string LabelApply       => J("適用",                             "Apply");
        public string ThemeLight       => J("ライト",                           "Light");
        public string ThemeDark        => J("ダーク",                           "Dark");
        public string AudioSystem      => J("システムオーディオ（スピーカー）", "System audio (speaker)");
        public string AudioMic         => J("マイク",                           "Microphone");
        public string SizeSmall        => J("小",                               "Small");
        public string SizeNormal       => J("標準",                             "Normal");
        public string SizeLarge        => J("大",                               "Large");
        public string SizeXL           => J("特大",                             "Extra large");
        public string PosTop           => J("上固定",                           "Top");
        public string PosBottom        => J("下固定",                           "Bottom");
        public string PosFloat         => J("フロート",                         "Floating");
    }
}
