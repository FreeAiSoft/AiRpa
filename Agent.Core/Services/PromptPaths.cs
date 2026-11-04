using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Agent.Core.Services
{
    /// <summary>
    /// Resolves the deployed "Prompts" directory on disk. The directory is shipped
    /// next to the extension assemblies (copied to output and included in the VSIX),
    /// so it is located relative to the loaded assembly rather than hard-coded.
    /// </summary>
    public static class PromptPaths
    {
        /// <summary>Folder name that holds all prompt categories.</summary>
        public const string PromptsFolderName = "Prompts";

        /// <summary>User-level prompts folder under %AppData% (current name).</summary>
        public const string AppDataFolderName = "Agent";

        /// <summary>
        /// Прежнее имя каталога в %AppData%: сборка была переименована VibeCoder.Core -> Agent.Core.
        /// Оставлено только как фолбэк на чтение, чтобы у существующей установки не пропали
        /// пользовательские промпты. Удалить через релиз после переименования.
        /// </summary>
        public const string LegacyAppDataFolderName = "VibeCoder";

        /// <summary>
        /// Returns the deployed prompts directory. Candidates, in order:
        /// 1) next to this assembly, 2) next to the entry assembly,
        /// 3) %AppData%\Agent\Prompts, 4) %AppData%\VibeCoder\Prompts (legacy).
        /// Returns the first existing folder; otherwise the first best-effort candidate.
        /// </summary>
        public static string ResolvePromptsDirectory()
        {
            IReadOnlyList<string> candidates = EnumerateCandidates();

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    return candidate;
            }

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return "";
        }

        /// <summary>
        /// Candidate prompts directories in priority order. Paths are best-effort and may not
        /// exist; the list is a pure function of the environment (no disk probing), so the
        /// priority stays assertable in tests.
        /// </summary>
        public static IReadOnlyList<string> EnumerateCandidates()
        {
            return new[]
            {
                CombineDir(Path.GetDirectoryName(typeof(PromptPaths).Assembly.Location)),
                CombineDir(EntryAssemblyDir()),
                CombineAppDataDir(AppDataFolderName),
                CombineAppDataDir(LegacyAppDataFolderName)
            };
        }

        private static string CombineAppDataDir(string folderName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                folderName,
                PromptsFolderName);
        }

        private static string CombineDir(string baseDir)
        {
            return string.IsNullOrWhiteSpace(baseDir) ? "" : Path.Combine(baseDir, PromptsFolderName);
        }

        private static string EntryAssemblyDir()
        {
            try
            {
                var entry = Assembly.GetEntryAssembly();
                if (entry == null) return "";
                return Path.GetDirectoryName(entry.Location);
            }
            catch
            {
                return "";
            }
        }
    }
}