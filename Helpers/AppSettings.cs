using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MoonLiveCaptions.Helpers
{
    /// <summary>
    /// Simple key=value settings file stored in %LOCALAPPDATA%\MoonLiveCaptions\settings.ini.
    /// No external libraries required.
    /// </summary>
    public static class AppSettings
    {
        // ── Directory paths ───────────────────────────────────────

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MoonLiveCaptions");

        /// <summary>Full path to settings.ini</summary>
        public static string SettingsFilePath => Path.Combine(AppDataDir, "settings.ini");

        /// <summary>Directory where model files are cached.</summary>
        public static string ModelsDir => Path.Combine(AppDataDir, "Models");

        /// <summary>Directory where recordings are saved (same folder as the executable).</summary>
        public static string RecordingsDir => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Recordings");

        // ── Settings properties ───────────────────────────────────

        /// <summary>"ja" or "en". Null means not yet persisted (auto-detect on first run).</summary>
        public static string UILanguage         { get; set; } = null;

        public static string DisplayMode        { get; set; } = "Floating";
        public static string FontSize           { get; set; } = "Normal";
        public static string Theme              { get; set; } = "Light";
        public static double BackgroundOpacity  { get; set; } = 1.0;
        public static bool   CaptureSystemAudio { get; set; } = true;
        public static bool   CaptureMicrophone  { get; set; } = false;
        public static string TranscriptionLang  { get; set; } = "ja";
        public static double WindowLeft         { get; set; } = double.NaN;
        public static double WindowTop          { get; set; } = double.NaN;
        public static double WindowWidth        { get; set; } = 720;
        public static double WindowHeight       { get; set; } = 190;

        /// <summary>True when a settings file already exists on disk.</summary>
        public static bool Exists => File.Exists(SettingsFilePath);

        // ── Directory initialisation ──────────────────────────────

        public static void EnsureDirectories()
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                Directory.CreateDirectory(ModelsDir);
                // RecordingsDir is next to the exe; created on demand when a session starts
            }
            catch { /* best-effort */ }
        }

        // ── Load ──────────────────────────────────────────────────

        /// <summary>
        /// Load settings from disk. If the file does not exist the method returns
        /// without changing any property (defaults remain in place).
        /// </summary>
        public static void Load()
        {
            if (!File.Exists(SettingsFilePath)) return;

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string line in File.ReadAllLines(SettingsFilePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    dict[key] = val;
                }
            }
            catch { return; }

            UILanguage         = Get(dict,    "UILanguage",         null);
            DisplayMode        = Get(dict,    "DisplayMode",        "Floating");
            FontSize           = Get(dict,    "FontSize",           "Normal");
            Theme              = Get(dict,    "Theme",              "Light");
            BackgroundOpacity  = GetDouble(dict, "BackgroundOpacity", 1.0);
            CaptureSystemAudio = GetBool(dict,   "CaptureSystemAudio", true);
            CaptureMicrophone  = GetBool(dict,   "CaptureMicrophone",  false);
            TranscriptionLang  = Get(dict,    "TranscriptionLang",  "ja");
            WindowLeft         = GetDouble(dict, "WindowLeft",        double.NaN);
            WindowTop          = GetDouble(dict, "WindowTop",         double.NaN);
            WindowWidth        = GetDouble(dict, "WindowWidth",       720);
            WindowHeight       = GetDouble(dict, "WindowHeight",      190);
        }

        // ── Save ──────────────────────────────────────────────────

        public static void Save()
        {
            try
            {
                EnsureDirectories();
                using (var sw = new StreamWriter(SettingsFilePath, append: false, encoding: Encoding.UTF8))
                {
                    sw.WriteLine("# MoonLiveCaptions settings");
                    sw.WriteLine("UILanguage="         + (UILanguage ?? ""));
                    sw.WriteLine("DisplayMode="        + DisplayMode);
                    sw.WriteLine("FontSize="           + FontSize);
                    sw.WriteLine("Theme="              + Theme);
                    sw.WriteLine("BackgroundOpacity="  + BackgroundOpacity.ToString("R", CultureInfo.InvariantCulture));
                    sw.WriteLine("CaptureSystemAudio=" + (CaptureSystemAudio ? "true" : "false"));
                    sw.WriteLine("CaptureMicrophone="  + (CaptureMicrophone  ? "true" : "false"));
                    sw.WriteLine("TranscriptionLang="  + TranscriptionLang);
                    sw.WriteLine("WindowLeft="         + WindowLeft.ToString("R",  CultureInfo.InvariantCulture));
                    sw.WriteLine("WindowTop="          + WindowTop.ToString("R",   CultureInfo.InvariantCulture));
                    sw.WriteLine("WindowWidth="        + WindowWidth.ToString("R", CultureInfo.InvariantCulture));
                    sw.WriteLine("WindowHeight="       + WindowHeight.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            catch { /* best-effort */ }
        }

        // ── Language detection ────────────────────────────────────

        /// <summary>
        /// Returns "ja" if the Windows UI language is Japanese, "en" otherwise.
        /// </summary>
        public static string DetectWindowsUILanguage()
        {
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return lang == "ja" ? "ja" : "en";
        }

        // ── Private helpers ───────────────────────────────────────

        private static string Get(Dictionary<string, string> d, string key, string def)
        {
            string v;
            return d.TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : def;
        }

        private static double GetDouble(Dictionary<string, string> d, string key, double def)
        {
            string v;
            double r;
            return d.TryGetValue(key, out v) && double.TryParse(v,
                NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        private static bool GetBool(Dictionary<string, string> d, string key, bool def)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return def;
            return v == "true" || v == "1" || v == "yes";
        }
    }
}
