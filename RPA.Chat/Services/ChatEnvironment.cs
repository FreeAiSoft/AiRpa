using System;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Host-provided integration points for the chat module. The studio host (AiRpaStudio)
    /// assigns these before the chat panel is shown; RPA.Chat itself only depends on the
    /// defaults so it can run standalone.
    /// </summary>
    public static class ChatEnvironment
    {
        /// <summary>
        /// Returns the current studio project/solution folder. Used by the MSBuild feedback
        /// service and the agent tools to resolve relative paths. Defaults to empty.
        /// </summary>
        public static Func<string> SolutionPathProvider { get; set; } = () => string.Empty;

        /// <summary>
        /// Reads the user's open editor tabs (AvalonEdit buffers). Defaults to the null object
        /// until the studio host injects a real implementation.
        /// </summary>
        public static IStudioDocumentService DocumentService { get; set; } = NullStudioDocumentService.Instance;
    }
}
