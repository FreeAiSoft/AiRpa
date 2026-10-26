namespace RPA.Chat.Services
{
    /// <summary>
    /// Studio adapter for reading the user's editor tabs (AvalonEdit buffers) rather than
    /// only the on-disk files. The host (AiRpaStudio) implements this against its
    /// AvalonEdit tabs + ProjectManager; agent tools use it to read what the user actually
    /// sees in the editor, including unsaved changes.
    /// </summary>
    public interface IStudioDocumentService
    {
        /// <summary>
        /// Returns the current (possibly unsaved) text of the open editor tab for
        /// <paramref name="path"/>, or null when the file is not open or not a text document.
        /// </summary>
        string GetOpenDocumentContent(string path);

        /// <summary>
        /// Reads a numbered window of the open document around <paramref name="errorLine"/>.
        /// Returns null when the document is not open or no lines can be read.
        /// </summary>
        string ReadOpenDocumentWindow(string path, int errorLine, int contextLines);

        /// <summary>
        /// Opens <paramref name="path"/> in a new editor tab and activates it.
        /// Returns false when the file cannot be opened.
        /// </summary>
        bool OpenFile(string path);
    }

    /// <summary>Null-object document service used when the studio host does not inject one.</summary>
    public sealed class NullStudioDocumentService : IStudioDocumentService
    {
        public static readonly NullStudioDocumentService Instance = new NullStudioDocumentService();

        public string GetOpenDocumentContent(string path) => null;

        public string ReadOpenDocumentWindow(string path, int errorLine, int contextLines) => null;

        public bool OpenFile(string path) => false;
    }
}
