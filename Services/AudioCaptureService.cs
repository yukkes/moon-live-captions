using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MoonLiveCaptions.Services
{
    /// <summary>
    /// Mixes N-channel audio down to mono.
    /// </summary>
    public class MonoMixdownSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _sourceBuffer;

        public WaveFormat WaveFormat { get; }

        public MonoMixdownSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int sourceCount = count * _channels;
            if (_sourceBuffer == null || _sourceBuffer.Length < sourceCount)
                _sourceBuffer = new float[sourceCount];

            int sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceCount);
            int monoSamples = sourceSamplesRead / _channels;

            for (int i = 0; i < monoSamples; i++)
            {
                float sum = 0f;
                for (int ch = 0; ch < _channels; ch++)
                    sum += _sourceBuffer[i * _channels + ch];
                buffer[offset + i] = sum / _channels;
            }

            return monoSamples;
        }
    }

    /// <summary>
    /// Linearly resamples an ISampleProvider from one sample rate to another.
    /// </summary>
    public class LinearResampleSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceSampleRate;
        private readonly int _targetSampleRate;
        private readonly double _ratio;
        private float _lastSample;
        private double _position;
        private float[] _sourceBuffer;

        public WaveFormat WaveFormat { get; }

        public LinearResampleSampleProvider(ISampleProvider source, int targetSampleRate)
        {
            _source = source;
            _sourceSampleRate = source.WaveFormat.SampleRate;
            _targetSampleRate = targetSampleRate;
            _ratio = (double)_sourceSampleRate / _targetSampleRate;
            _position = 0;
            _lastSample = 0;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetSampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int sourceNeeded = (int)(count * _ratio) + 4;
            if (_sourceBuffer == null || _sourceBuffer.Length < sourceNeeded)
                _sourceBuffer = new float[sourceNeeded];

            int sourceRead = _source.Read(_sourceBuffer, 0, sourceNeeded);
            if (sourceRead == 0) return 0;

            int written = 0;
            for (int i = 0; i < count && written < count; i++)
            {
                int srcIndex = (int)_position;
                double frac = _position - srcIndex;

                if (srcIndex + 1 < sourceRead)
                {
                    buffer[offset + written] = (float)(_sourceBuffer[srcIndex] * (1.0 - frac) +
                                                        _sourceBuffer[srcIndex + 1] * frac);
                }
                else if (srcIndex < sourceRead)
                {
                    buffer[offset + written] = _sourceBuffer[srcIndex];
                }
                else
                {
                    break;
                }

                written++;
                _position += _ratio;
            }

            _position -= (int)_position;
            if (sourceRead > 0)
                _lastSample = _sourceBuffer[sourceRead - 1];

            return written;
        }
    }

    /// <summary>
    /// Audio capture service using WASAPI for mic input and WASAPI Loopback for system audio.
    /// Outputs 16kHz mono float samples. System audio capture works even when speakers are muted.
    /// </summary>
    public class AudioCaptureService : IDisposable
    {
        private const int TargetSampleRate = 16000;

        private WasapiCapture _micCapture;
        private WasapiLoopbackCapture _speakerCapture;

        private BufferedWaveProvider _micBuffer;
        private BufferedWaveProvider _speakerBuffer;

        private ISampleProvider _micResampled;
        private ISampleProvider _speakerResampled;

        private bool _capturingMic;
        private bool _capturingSpeaker;
        private bool _isCapturing;

        public event EventHandler<float[]> MicSamplesAvailable;
        public event EventHandler<float[]> SpeakerSamplesAvailable;
        public event EventHandler<float> LevelChanged;

        public bool IsCapturing => _isCapturing;

        /// <summary>
        /// Returns available microphone devices.
        /// </summary>
        public List<AudioDeviceInfo> GetMicDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            var enumerator = new MMDeviceEnumerator();

            try
            {
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                string defaultId = defaultDevice?.ID;
                defaultDevice?.Dispose();

                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    devices.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = device.ID == defaultId
                    });
                    device.Dispose();
                }
            }
            catch { }

            return devices;
        }

        /// <summary>
        /// Returns available speaker/output devices (for loopback capture).
        /// </summary>
        public List<AudioDeviceInfo> GetSpeakerDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            var enumerator = new MMDeviceEnumerator();

            try
            {
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                string defaultId = defaultDevice?.ID;
                defaultDevice?.Dispose();

                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    devices.Add(new AudioDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = device.ID == defaultId
                    });
                    device.Dispose();
                }
            }
            catch { }

            return devices;
        }

        /// <summary>
        /// Gets the default speaker device ID.
        /// </summary>
        public string GetDefaultSpeakerDeviceId()
        {
            var enumerator = new MMDeviceEnumerator();
            try
            {
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                string id = device?.ID;
                device?.Dispose();
                return id;
            }
            catch { return null; }
        }

        /// <summary>
        /// Gets the default microphone device ID.
        /// </summary>
        public string GetDefaultMicDeviceId()
        {
            var enumerator = new MMDeviceEnumerator();
            try
            {
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                string id = device?.ID;
                device?.Dispose();
                return id;
            }
            catch { return null; }
        }

        /// <summary>
        /// Starts capturing audio from the specified devices.
        /// </summary>
        public void StartCapture(string micDeviceId, string speakerDeviceId, bool captureMic, bool captureSpeaker)
        {
            if (_isCapturing) return;

            _capturingMic = captureMic;
            _capturingSpeaker = captureSpeaker;

            var enumerator = new MMDeviceEnumerator();

            if (captureSpeaker && !string.IsNullOrEmpty(speakerDeviceId))
            {
                try
                {
                    var speakerDevice = enumerator.GetDevice(speakerDeviceId);
                    _speakerCapture = new WasapiLoopbackCapture(speakerDevice);

                    SetupSpeakerPipeline(_speakerCapture.WaveFormat);

                    _speakerCapture.DataAvailable += OnSpeakerDataAvailable;
                    _speakerCapture.RecordingStopped += OnRecordingStopped;
                    _speakerCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Speaker capture error: " + ex.Message);
                    _capturingSpeaker = false;
                }
            }

            if (captureMic && !string.IsNullOrEmpty(micDeviceId))
            {
                try
                {
                    var micDevice = enumerator.GetDevice(micDeviceId);
                    _micCapture = new WasapiCapture(micDevice, true, 30);
                    _micCapture.WaveFormat = new WaveFormat(
                        _micCapture.WaveFormat.SampleRate,
                        16,
                        _micCapture.WaveFormat.Channels);

                    SetupMicPipeline(_micCapture.WaveFormat);

                    _micCapture.DataAvailable += OnMicDataAvailable;
                    _micCapture.RecordingStopped += OnRecordingStopped;
                    _micCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Mic capture error: " + ex.Message);
                    _capturingMic = false;
                }
            }

            _isCapturing = _capturingMic || _capturingSpeaker;
        }

        public void StopCapture()
        {
            if (!_isCapturing) return;

            try { _micCapture?.StopRecording(); } catch { }
            try { _speakerCapture?.StopRecording(); } catch { }

            _isCapturing = false;
        }

        private void SetupMicPipeline(WaveFormat captureFormat)
        {
            _micBuffer = new BufferedWaveProvider(captureFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = captureFormat.AverageBytesPerSecond * 5
            };

            ISampleProvider provider = _micBuffer.ToSampleProvider();

            if (captureFormat.Channels > 1)
                provider = new MonoMixdownSampleProvider(provider);

            if (captureFormat.SampleRate != TargetSampleRate)
                provider = new LinearResampleSampleProvider(provider, TargetSampleRate);

            _micResampled = provider;
        }

        private void SetupSpeakerPipeline(WaveFormat captureFormat)
        {
            _speakerBuffer = new BufferedWaveProvider(captureFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = captureFormat.AverageBytesPerSecond * 5
            };

            ISampleProvider provider = _speakerBuffer.ToSampleProvider();

            if (captureFormat.Channels > 1)
                provider = new MonoMixdownSampleProvider(provider);

            if (captureFormat.SampleRate != TargetSampleRate)
                provider = new LinearResampleSampleProvider(provider, TargetSampleRate);

            _speakerResampled = provider;
        }

        private void OnMicDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            _micBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            int inputSamples = e.BytesRecorded / (_micCapture.WaveFormat.BitsPerSample / 8);
            int inputFrames = inputSamples / _micCapture.WaveFormat.Channels;
            int outputSamples = (int)((long)inputFrames * TargetSampleRate / _micCapture.WaveFormat.SampleRate) + 64;

            var buffer = new float[outputSamples];
            int read = _micResampled.Read(buffer, 0, buffer.Length);

            if (read > 0)
            {
                var samples = new float[read];
                Array.Copy(buffer, samples, read);

                float level = CalculateRMS(samples);
                LevelChanged?.Invoke(this, level);

                MicSamplesAvailable?.Invoke(this, samples);
            }
        }

        private void OnSpeakerDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            _speakerBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            int bytesPerSample = _speakerCapture.WaveFormat.BitsPerSample / 8;
            int inputSamples = e.BytesRecorded / bytesPerSample;
            int inputFrames = inputSamples / _speakerCapture.WaveFormat.Channels;
            int outputSamples = (int)((long)inputFrames * TargetSampleRate / _speakerCapture.WaveFormat.SampleRate) + 64;

            var buffer = new float[outputSamples];
            int read = _speakerResampled.Read(buffer, 0, buffer.Length);

            if (read > 0)
            {
                var samples = new float[read];
                Array.Copy(buffer, samples, read);

                float level = CalculateRMS(samples);
                LevelChanged?.Invoke(this, level);

                SpeakerSamplesAvailable?.Invoke(this, samples);
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                System.Diagnostics.Debug.WriteLine("Recording stopped with error: " + e.Exception.Message);
        }

        private static float CalculateRMS(float[] samples)
        {
            if (samples.Length == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];
            return (float)Math.Sqrt(sum / samples.Length);
        }

        public void Dispose()
        {
            StopCapture();

            if (_micCapture != null)
            {
                _micCapture.DataAvailable -= OnMicDataAvailable;
                _micCapture.RecordingStopped -= OnRecordingStopped;
                _micCapture.Dispose();
                _micCapture = null;
            }

            if (_speakerCapture != null)
            {
                _speakerCapture.DataAvailable -= OnSpeakerDataAvailable;
                _speakerCapture.RecordingStopped -= OnRecordingStopped;
                _speakerCapture.Dispose();
                _speakerCapture = null;
            }
        }
    }

    public class AudioDeviceInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }

        public override string ToString() => Name ?? Id ?? "(unknown)";
    }
}
