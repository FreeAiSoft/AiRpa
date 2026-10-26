using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VibeCoder.Core.Services
{
    /// <summary>A single documentation file captured by the index.</summary>
    public sealed class DocumentationEntry
    {
        /// <summary>Path relative to the docs directory, using '/' separators.</summary>
        public string RelativePath { get; set; }

        /// <summary>Document title parsed from the first level-1 markdown heading.</summary>
        public string Title { get; set; }

        /// <summary>Full file content.</summary>
        public string Content { get; set; }
    }

    /// <summary>A single search hit inside the documentation index.</summary>
    public sealed class DocumentationSearchResult
    {
        /// <summary>Path relative to the docs directory, using '/' separators.</summary>
        public string RelativePath { get; set; }

        /// <summary>Document title.</summary>
        public string Title { get; set; }

        /// <summary>Short excerpt around the first matching term.</summary>
        public string Snippet { get; set; }

        /// <summary>Relevance score (higher is better).</summary>
        public int Score { get; set; }
    }

    /// <summary>
    /// Builds a searchable in-memory index over a directory of markdown documentation
    /// files. Used by the Plan chat mode to answer "how does the studio work" questions
    /// from the built-in user documentation without performing any mutating action.
    /// </summary>
    public sealed class DocumentationIndex
    {
        private static readonly DocumentationIndex EmptyInstance =
            new DocumentationIndex(new List<DocumentationEntry>());

        private readonly IReadOnlyList<DocumentationEntry> _entries;

        private DocumentationIndex(IReadOnlyList<DocumentationEntry> entries)
        {
            _entries = entries ?? new List<DocumentationEntry>();
        }

        /// <summary>The indexed documents.</summary>
        public IReadOnlyList<DocumentationEntry> Entries => _entries;

        /// <summary>True when no documentation files were found.</summary>
        public bool IsEmpty => _entries.Count == 0;

        /// <summary>Returns an empty index.</summary>
        public static DocumentationIndex Empty => EmptyInstance;

        /// <summary>Builds an index over every <c>*.md</c> file under <paramref name="docsDirectory"/>.</summary>
        public static DocumentationIndex Build(string docsDirectory)
        {
            if (string.IsNullOrWhiteSpace(docsDirectory) || !Directory.Exists(docsDirectory))
                return Empty;

            var files = new List<string>(Directory.GetFiles(docsDirectory, "*.md", SearchOption.AllDirectories));
            files.Sort(StringComparer.OrdinalIgnoreCase);

            var entries = new List<DocumentationEntry>();
            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file, Encoding.UTF8);
                    entries.Add(new DocumentationEntry
                    {
                        RelativePath = NormalizeRelative(docsDirectory, file),
                        Title = ExtractTitle(content, Path.GetFileNameWithoutExtension(file)),
                        Content = content
                    });
                }
                catch
                {
                    // Ignore unreadable files.
                }
            }

            return entries.Count == 0 ? Empty : new DocumentationIndex(entries);
        }

        /// <summary>Builds an index over the deployed docs directory (next to the assemblies).</summary>
        public static DocumentationIndex FromDeployedDocs()
        {
            return Build(HelpDocumentation.ResolveDocsDirectory());
        }

        /// <summary>
        /// Searches the index for the given query and returns up to <paramref name="maxResults"/>
        /// matches ordered by relevance. Title matches are weighted higher than body matches.
        /// </summary>
        public IReadOnlyList<DocumentationSearchResult> Search(string query, int maxResults = 8)
        {
            if (maxResults <= 0) maxResults = 8;
            if (maxResults > 50) maxResults = 50;

            var terms = Tokenize(query).Where(t => t.Length >= 2).Distinct(StringComparer.Ordinal).ToList();
            if (terms.Count == 0)
                return new List<DocumentationSearchResult>();

            var results = new List<DocumentationSearchResult>();
            foreach (var entry in _entries)
            {
                var title = (entry.Title ?? string.Empty).ToLowerInvariant();
                var content = (entry.Content ?? string.Empty).ToLowerInvariant();

                var score = 0;
                var firstMatch = -1;
                foreach (var term in terms)
                {
                    if (title.IndexOf(term, StringComparison.Ordinal) >= 0)
                        score += 5;

                    var occurrences = CountOccurrences(content, term);
                    if (occurrences > 0)
                    {
                        score += Math.Min(occurrences, 10);
                        var idx = content.IndexOf(term, StringComparison.Ordinal);
                        if (firstMatch < 0 || idx < firstMatch)
                            firstMatch = idx;
                    }
                }

                if (score <= 0)
                    continue;

                results.Add(new DocumentationSearchResult
                {
                    RelativePath = entry.RelativePath,
                    Title = entry.Title,
                    Snippet = ExtractSnippet(entry.Content, firstMatch),
                    Score = score
                });
            }

            return results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .ToList();
        }

        /// <summary>Returns the full content of a document by its relative path, or null when not found.</summary>
        public string Read(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            var normalized = NormalizePath(relativePath);
            foreach (var entry in _entries)
            {
                if (string.Equals(NormalizePath(entry.RelativePath), normalized, StringComparison.OrdinalIgnoreCase))
                    return entry.Content;
            }

            return null;
        }


        /// <summary>
        /// Builds a compact reference (title + path per document) intended to enrich the
        /// Plan system prompt so the agent knows what documentation topics exist.
        /// </summary>
        public string BuildReference()
        {
            if (_entries.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var entry in _entries)
            {
                sb.Append("- ").Append(entry.RelativePath);
                var title = (entry.Title ?? string.Empty).Trim();
                if (title.Length > 0)
                    sb.Append(" — ").Append(title);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string NormalizeRelative(string root, string file)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullFile = Path.GetFullPath(file);

            if (fullFile.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return NormalizePath(fullFile.Substring(fullRoot.Length + 1));

            return NormalizePath(Path.GetFileName(fullFile));
        }


        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Trim()
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/')
                .TrimStart('/');
        }

        private static string ExtractTitle(string content, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                foreach (var rawLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith("# ", StringComparison.Ordinal))
                    {
                        var title = line.Substring(2).Trim();
                        if (title.Length > 0)
                            return title;
                    }
                }
            }

            return string.IsNullOrWhiteSpace(fallback) ? "Documentation" : fallback;
        }

        private static int CountOccurrences(string text, string term)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
                return 0;

            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += term.Length;
            }

            return count;
        }

        private static string ExtractSnippet(string content, int matchIndex)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            if (matchIndex < 0)
                matchIndex = 0;

            const int before = 120;
            const int after = 220;

            var start = Math.Max(0, matchIndex - before);
            var end = Math.Min(content.Length, matchIndex + after);

            var snippet = content.Substring(start, end - start);
            snippet = string.Join(" ", snippet.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

            if (start > 0)
                snippet = "…" + snippet;
            if (end < content.Length)
                snippet = snippet + "…";

            return snippet;
        }

        private static IEnumerable<string> Tokenize(string text)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }
    }
}


