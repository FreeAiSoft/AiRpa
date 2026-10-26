using System;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Writes chat diagnostics/progress to the studio's output. In AiRpaStudio this is
    /// backed by RPA.Logging.Log (viewed through LogControlLibrary); RPA.Chat itself
    /// provides a file-based default so the service works without a host.
    /// </summary>
    public interface IOutputWindowService
    {
        /// <summary>Shows/activates the output surface.</summary>
        void Activate();

        /// <summary>Writes one line of text.</summary>
        void WriteLine(string message);

        /// <summary>Creates an <see cref="IProgress{T}"/> that writes each reported line.</summary>
        IProgress<string> CreateProgress();
    }

    /// <summary>Default file-based output service used when the studio host does not inject one.</summary>
    public sealed class OutputWindowService : IOutputWindowService
    {
        /// <summary>Host-provided sink (AiRpaStudio sets this to RPA.Logging.Log).</summary>
        public static Action<string> WriteSink { get; set; }

        /// <summary>Host-provided activation callback (AiRpaStudio shows the log panel).</summary>
        public static Action ActivateSink { get; set; }

        public void Activate()
        {
            var sink = ActivateSink;
            if (sink != null)
            {
                sink();
                return;
            }
            ChatLog.Info("Output window activated.");
        }

        public void WriteLine(string message)
        {
            var sink = WriteSink;
            if (sink != null)
            {
                sink(message ?? string.Empty);
                return;
            }
            ChatLog.Info(message ?? string.Empty);
        }

        public IProgress<string> CreateProgress() => new Progress<string>(WriteLine);
    }
}
