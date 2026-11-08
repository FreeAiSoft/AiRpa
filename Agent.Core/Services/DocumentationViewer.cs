using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Agent.Core.Services
{
    /// <summary>
    /// Locale-aware viewer for the shipped user documentation. It opens the locale
    /// <c>index.md</c> first and resolves relative <c>.md</c> links strictly within the
    /// active locale folder, so clicking a link in the rendered documentation opens
    /// another shipped document without ever leaving the docs directory.
    /// </summary>
    public sealed class DocumentationViewer
    {
        /// <summary>Name of the home page that is shown first.</summary>
        public const string IndexFileName = "index.md";

        private readonly string _docsDirectory;
        private readonly string _localeOverride;

        /// <summary>Uses the deployed docs directory and the current UI culture.</summary>
        public DocumentationViewer() : this(HelpDocumentation.ResolveDocsDirectory())
        {
        }

        /// <summary>
        /// Points the viewer at an explicit docs directory. <paramref name="locale"/> pins the
        /// active locale ("ru"/"en") for tests; when null the current UI culture decides.
        /// </summary>
        public DocumentationViewer(string docsDirectory, string locale = null)
        {
            _docsDirectory = docsDirectory ?? string.Empty;
            _localeOverride = string.IsNullOrWhiteSpace(locale) ? null : NormalizeLocale(locale);
        }

        /// <summary>Root directory this viewer reads from.</summary>
        public string DocsDirectory => _docsDirectory;

        /// <summary>Active locale name ("ru"/"en"), or empty when no locale folder exists.</summary>
        public string Locale
        {
            get
            {
                var dir = ActiveLocaleDirectory();
                return string.IsNullOrEmpty(dir) ? string.Empty : Path.GetFileName(dir);
            }
        }

        /// <summary>
        /// Reads the home page of the active locale. When the active locale has no
        /// <c>index.md</c>, falls back to the other locale's home page; returns "" when
        /// neither exists.
        /// </summary>
        public string ReadIndex()
        {
            if (!HasDocsDirectory())
                return string.Empty;

            var primary = CurrentLocaleName();
            var content = ReadFromLocale(primary, IndexFileName);
            if (content != null)
                return content;

            content = ReadFromLocale(OtherLocaleName(primary), IndexFileName);
            return content ?? string.Empty;
        }

        /// <summary>
        /// Reads a document by its relative path within the active locale folder. Returns ""
        /// when the path is missing, not a <c>.md</c> file, or escapes the docs directory.
        /// </summary>
        public string ReadDocument(string relativePath)
        {
            var dir = ActiveLocaleDirectory();
            if (string.IsNullOrEmpty(dir))
                return string.Empty;

            var file = SafeCombine(dir, relativePath);
            if (file == null || !IsMarkdown(file))
                return string.Empty;

            return File.Exists(file) ? ReadAllText(file) : string.Empty;
        }

        /// <summary>
        /// Resolves a markdown link target relative to the current document and returns the
        /// target's normalized relative path within the locale folder, or null when the link
        /// is external (http/https/mailto), a fragment, not a <c>.md</c> file, escapes the
        /// docs directory, or points to a missing file.
        /// </summary>
        public string ResolveLink(string currentRelativePath, string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;

            var trimmed = target.Trim();

            // External links (browser/mail client) are not documentation navigation.
            if (IsExternal(trimmed))
                return null;

            // Anchor-only links stay in place.
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                return null;

            var clean = StripFragmentAndQuery(trimmed);
            if (string.IsNullOrWhiteSpace(clean) || !IsMarkdown(clean))
                return null;

            var dir = ActiveLocaleDirectory();
            if (string.IsNullOrEmpty(dir))
                return null;

            // Resolve relative to the directory of the current document (usually the locale root).
            var currentDir = dir;
            var current = NormalizeRelative(currentRelativePath);
            if (!string.IsNullOrEmpty(current))
            {
                var parent = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(parent))
                    currentDir = Path.Combine(dir, parent);
            }

            var resolved = SafeCombine(currentDir, clean);
            if (resolved == null || !File.Exists(resolved))
                return null;

            return ToRelative(dir, resolved);
        }

        private bool HasDocsDirectory()
        {
            return !string.IsNullOrEmpty(_docsDirectory) && Directory.Exists(_docsDirectory);
        }

        private string ActiveLocaleDirectory()
        {
            var dir = LocaleDirectoryFor(CurrentLocaleName());
            if (!string.IsNullOrEmpty(dir))
                return dir;

            return LocaleDirectoryFor(OtherLocaleName(CurrentLocaleName()));
        }

        private string ReadFromLocale(string locale, string fileName)
        {
            var dir = LocaleDirectoryFor(locale);
            if (string.IsNullOrEmpty(dir))
                return null;

            var file = SafeCombine(dir, fileName);
            if (file == null || !IsMarkdown(file) || !File.Exists(file))
                return null;

            return ReadAllText(file);
        }

        private string LocaleDirectoryFor(string locale)
        {
            if (!HasDocsDirectory() || string.IsNullOrWhiteSpace(locale))
                return string.Empty;

            var dir = Path.Combine(_docsDirectory, locale);
            return Directory.Exists(dir) ? dir : string.Empty;
        }

        private string CurrentLocaleName()
        {
            if (_localeOverride != null)
                return _localeOverride;

            try
            {
                return NormalizeLocale(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            }
            catch
            {
                return "en";
            }
        }

        private static string OtherLocaleName(string locale)
        {
            return string.Equals(locale, "ru", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
        }

        private static string NormalizeLocale(string name)
        {
            return string.Equals(name, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        }

        private static bool IsExternal(string target)
        {
            return target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMarkdown(string path)
        {
            return string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripFragmentAndQuery(string target)
        {
            var hash = target.IndexOf('#');
            var query = target.IndexOf('?');
            var cut = hash >= 0 ? hash : query;
            if (query >= 0 && (cut < 0 || query < cut))
                cut = query;

            return cut >= 0 ? target.Substring(0, cut) : target;
        }

        private static string NormalizeRelative(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Trim().Replace('\\', '/').Trim('/');
        }

        /// <summary>
        /// Combines <paramref name="root"/> with a relative path and verifies the result stays
        /// inside <paramref name="root"/>. Returns null on traversal, rooted paths or invalid input.
        /// </summary>
        private static string SafeCombine(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath))
                return null;

            var normalized = NormalizeRelative(relativePath);
            if (normalized.Length == 0 || Path.IsPathRooted(normalized))
                return null;

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, normalized));
            }
            catch
            {
                return null;
            }

            var rootFull = Path.GetFullPath(root);
            if (!string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;

            return full;
        }

        private static string ToRelative(string root, string full)
        {
            var rootFull = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(full);

            if (fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return NormalizeRelative(fullPath.Substring(rootFull.Length + 1));

            return NormalizeRelative(Path.GetFileName(fullPath));
        }

        private static string ReadAllText(string path)
        {
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
