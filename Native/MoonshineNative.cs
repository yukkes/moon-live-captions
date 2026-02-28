using System;
using System.Runtime.InteropServices;

namespace MoonLiveCaptions.Native
{
    /// <summary>
    /// P/Invoke wrapper for the Moonshine Voice C API (moonshine.dll).
    /// </summary>
    public static class MoonshineNative
    {
        private const string DllName = "moonshine.dll";

        // ── Constants ─────────────────────────────────────────────
        public const int MOONSHINE_HEADER_VERSION = 20000;

        // Model architectures
        public const uint MODEL_ARCH_TINY = 0;
        public const uint MODEL_ARCH_BASE = 1;
        public const uint MODEL_ARCH_TINY_STREAMING = 2;
        public const uint MODEL_ARCH_BASE_STREAMING = 3;
        public const uint MODEL_ARCH_SMALL_STREAMING = 4;
        public const uint MODEL_ARCH_MEDIUM_STREAMING = 5;

        // Error codes
        public const int ERROR_NONE = 0;
        public const int ERROR_UNKNOWN = -1;
        public const int ERROR_INVALID_HANDLE = -2;
        public const int ERROR_INVALID_ARGUMENT = -3;

        // Flags
        public const uint FLAG_FORCE_UPDATE = (1 << 0);

        // ── Functions ─────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_get_version();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr moonshine_error_to_string(int error);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr moonshine_transcript_to_string(IntPtr transcript);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int moonshine_load_transcriber_from_files(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            uint model_arch,
            IntPtr options,
            ulong options_count,
            int moonshine_version);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int moonshine_load_transcriber_from_files(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            uint model_arch,
            [MarshalAs(UnmanagedType.LPArray)] TranscriberOption[] options,
            ulong options_count,
            int moonshine_version);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void moonshine_free_transcriber(int transcriber_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_transcribe_without_streaming(
            int transcriber_handle,
            float[] audio_data,
            ulong audio_length,
            int sample_rate,
            uint flags,
            out IntPtr out_transcript);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_create_stream(
            int transcriber_handle,
            uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_free_stream(
            int transcriber_handle,
            int stream_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_start_stream(
            int transcriber_handle,
            int stream_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_stop_stream(
            int transcriber_handle,
            int stream_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_transcribe_add_audio_to_stream(
            int transcriber_handle,
            int stream_handle,
            float[] new_audio_data,
            ulong audio_length,
            int sample_rate,
            uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int moonshine_transcribe_stream(
            int transcriber_handle,
            int stream_handle,
            uint flags,
            out IntPtr out_transcript);

        // ── Helper Methods ────────────────────────────────────────

        public static string GetErrorString(int errorCode)
        {
            IntPtr ptr = moonshine_error_to_string(errorCode);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : "Unknown error";
        }

        public static TranscriptLineManaged[] ReadTranscript(IntPtr transcriptPtr)
        {
            if (transcriptPtr == IntPtr.Zero)
                return new TranscriptLineManaged[0];

            IntPtr linesPtr = Marshal.ReadIntPtr(transcriptPtr, 0);
            long lineCount = Marshal.ReadInt64(transcriptPtr, IntPtr.Size);

            if (lineCount <= 0 || linesPtr == IntPtr.Zero)
                return new TranscriptLineManaged[0];

            var results = new TranscriptLineManaged[lineCount];
            int lineSize = Marshal.SizeOf(typeof(TranscriptLineNative));

            for (int i = 0; i < lineCount; i++)
            {
                IntPtr linePtr = new IntPtr(linesPtr.ToInt64() + i * lineSize);
                var native = (TranscriptLineNative)Marshal.PtrToStructure(linePtr, typeof(TranscriptLineNative));

                results[i] = new TranscriptLineManaged
                {
                    Text = native.text != IntPtr.Zero ? MarshalUTF8.PtrToStringUTF8(native.text) : "",
                    StartTime = native.start_time,
                    Duration = native.duration,
                    LineId = native.id,
                    IsComplete = native.is_complete != 0,
                    IsUpdated = native.is_updated != 0,
                    IsNew = native.is_new != 0,
                    HasTextChanged = native.has_text_changed != 0,
                    HasSpeakerId = native.has_speaker_id != 0,
                    SpeakerId = native.speaker_id,
                    SpeakerIndex = native.speaker_index,
                    LastTranscriptionLatencyMs = native.last_transcription_latency_ms
                };
            }

            return results;
        }
    }

    // ── Native Structs ────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct TranscriberOption
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string name;
        [MarshalAs(UnmanagedType.LPStr)]
        public string value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TranscriptLineNative
    {
        public IntPtr text;
        public IntPtr audio_data;
        public ulong audio_data_count;
        public float start_time;
        public float duration;
        public ulong id;
        public sbyte is_complete;
        public sbyte is_updated;
        public sbyte is_new;
        public sbyte has_text_changed;
        public sbyte has_speaker_id;
        private sbyte _pad1;
        private sbyte _pad2;
        private sbyte _pad3;
        public ulong speaker_id;
        public uint speaker_index;
        public uint last_transcription_latency_ms;
    }

    public class TranscriptLineManaged
    {
        public string Text { get; set; }
        public float StartTime { get; set; }
        public float Duration { get; set; }
        public ulong LineId { get; set; }
        public bool IsComplete { get; set; }
        public bool IsUpdated { get; set; }
        public bool IsNew { get; set; }
        public bool HasTextChanged { get; set; }
        public bool HasSpeakerId { get; set; }
        public ulong SpeakerId { get; set; }
        public uint SpeakerIndex { get; set; }
        public uint LastTranscriptionLatencyMs { get; set; }
    }

    public static class MarshalExtensions
    {
        public static string PtrToStringUTF8(this IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;

            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0)
                len++;

            if (len == 0) return string.Empty;

            byte[] bytes = new byte[len];
            Marshal.Copy(ptr, bytes, 0, len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    public static class MarshalUTF8
    {
        public static string PtrToStringUTF8(IntPtr ptr)
        {
            return ptr.PtrToStringUTF8();
        }
    }
}
