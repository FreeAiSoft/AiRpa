using System;
using System.IO;
using System.Text;
using FW.EasyMvvm.Core;
using RPA.Helpers;

namespace RPA.Models
{
    /// <summary>
    /// Model for a code file opened in the studio code editor.
    /// </summary>
    public class CodeDocument : BaseModel
    {
        private string filePath;
        private string text;
        private string title;
        private bool isDirty;

        public string FilePath
        {
            get => filePath;
            private set
            {
                filePath = value;
                OnPropertyChanged();
            }
        }

        public string Text
        {
            get => text;
            set
            {
                if (string.Equals(text, value, StringComparison.Ordinal))
                {
                    return;
                }

                text = value;
                OnPropertyChanged();
                IsDirty = true;
            }
        }

        public bool IsDirty
        {
            get => isDirty;
            set
            {
                if (isDirty == value)
                {
                    return;
                }

                isDirty = value;
                OnPropertyChanged();
            }
        }

        public string Title => string.IsNullOrEmpty(title)
            ? (string.IsNullOrEmpty(filePath) ? "Untitled" : Path.GetFileName(filePath))
            : title;

        public string Language => CodeLanguageProvider.GetLanguageForExtension(filePath);

        /// <summary>Loads a code document from disk. The document starts in a clean state.</summary>
        public static CodeDocument Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("File not found.", path);
            }

            string content = File.ReadAllText(path);
            return new CodeDocument
            {
                filePath = path,
                text = content,
                title = Path.GetFileName(path),
                isDirty = false
            };
        }

        /// <summary>Saves the current text back to disk and clears the dirty flag.</summary>
        public void Save()
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new InvalidOperationException("File path is not set.");
            }

            File.WriteAllText(filePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            IsDirty = false;
        }
    }
}
