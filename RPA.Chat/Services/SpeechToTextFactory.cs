using System;
using System.Threading;
using System.Threading.Tasks;
using VibeCoder.Core.Contracts;
using VibeCoder.Core.Services;

namespace RPA.Chat.Services
{
    /// <summary>Creates the speech-to-text pipeline from settings (Whisper + optional Vosk fallback).</summary>
    public static class SpeechToTextFactory
    {
        /// <summary>
        /// Optional factory for the offline Vosk service. RPA.Chat does not reference the
        /// Vosk package directly (it needs native DLLs); the studio host may inject a real
        /// implementation, otherwise Vosk requests report that the provider is unavailable.
        /// </summary>
        public static Func<string, ISpeechToTextService> VoskFactory { get; set; }

        public static ISpeechToTextService Create(SettingsData settings)
        {
            if (settings == null) settings = new SettingsData();

            var proxy = WebProxyBuilder.Build(
                settings.ProxyEnabled,
                settings.ProxyHost,
                settings.ProxyPort,
                settings.ProxyUsername,
                settings.ProxyPassword);

            var whisper = new OpenAiWhisperSttService(
                settings.BaseUrl,
                settings.ApiKey,
                settings.SpeechToTextModel,
                WebProxyBuilder.CreateHttpClient(proxy),
                ChatLog.Info);

            var provider = (settings.SpeechToTextProvider ?? "").Trim();
            if (string.Equals(provider, "Vosk", StringComparison.OrdinalIgnoreCase))
                return CreateVosk(settings.VoskModelPath);

            if (string.Equals(provider, "OpenAiWhisper", StringComparison.OrdinalIgnoreCase))
                return whisper;

            // "Auto": Whisper first, fall back to Vosk when a model directory is configured.
            return new FallbackSpeechToTextService(whisper, CreateVosk(settings.VoskModelPath));
        }

        private static ISpeechToTextService CreateVosk(string modelPath)
        {
            var factory = VoskFactory;
            if (factory != null)
                return factory(modelPath ?? "");
            return new UnavailableVoskService(modelPath ?? "");
        }

        private sealed class UnavailableVoskService : ISpeechToTextService
        {
            private readonly string _modelPath;

            public UnavailableVoskService(string modelPath)
            {
                _modelPath = modelPath;
            }

            public Task<string> TranscribeAsync(byte[] pcmAudio, string language, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException(
                    "Vosk speech recognition is not available in this build (no Vosk provider was injected). Model path: " + _modelPath);
            }
        }
    }
}
