using System.Collections.Generic;
using System.Linq;
using NuGet.Versioning;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Version resolution helpers built on <c>NuGet.Versioning</c> (part of NuGet.Client).
    /// Replaces the deprecated <c>NuGet.Resolver</c> package with equivalent, testable logic
    /// for choosing the best version to install/update to.
    /// </summary>
    public static class NuGetVersionResolver
    {
        public static bool IsValidVersion(string version)
        {
            return NuGetVersion.TryParse(version, out _);
        }

        public static bool TryParse(string version, out NuGetVersion parsed)
        {
            return NuGetVersion.TryParse(version, out parsed);
        }

        /// <summary>
        /// Returns the highest stable (or prerelease, when allowed) version that is strictly
        /// greater than <paramref name="currentVersion"/>. If nothing is greater, returns the
        /// normalized form of <paramref name="currentVersion"/>. When <paramref name="currentVersion"/>
        /// is <c>null</c>/blank, returns the highest available version.
        /// </summary>
        public static string SelectBestVersion(IEnumerable<string> versions, string currentVersion = null, bool includePrerelease = false)
        {
            List<NuGetVersion> parsed = ParseAll(versions);
            if (parsed.Count == 0)
                return null;

            if (!includePrerelease)
                parsed = parsed.Where(v => !v.IsPrerelease).ToList();
            if (parsed.Count == 0)
                return null;

            NuGetVersion floor = null;
            if (!string.IsNullOrWhiteSpace(currentVersion) && NuGetVersion.TryParse(currentVersion, out NuGetVersion current))
                floor = current;

            List<NuGetVersion> candidates = parsed;
            if (floor != null)
                candidates = parsed.Where(v => v > floor).ToList();

            if (candidates.Count == 0)
                return floor?.ToNormalizedString();

            return candidates.OrderByDescending(v => v).First().ToNormalizedString();
        }

        public static string GetLatestStable(IEnumerable<string> versions)
        {
            return SelectBestVersion(versions, null, false);
        }

        public static string GetLatest(IEnumerable<string> versions, bool includePrerelease)
        {
            return SelectBestVersion(versions, null, includePrerelease);
        }

        public static bool IsUpdateAvailable(IEnumerable<string> versions, string currentVersion, bool includePrerelease = false)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
                return false;

            string best = SelectBestVersion(versions, currentVersion, includePrerelease);
            return best != null && !string.Equals(best, currentVersion, System.StringComparison.OrdinalIgnoreCase);
        }

        private static List<NuGetVersion> ParseAll(IEnumerable<string> versions)
        {
            if (versions == null)
                return new List<NuGetVersion>();

            return versions
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => NuGetVersion.TryParse(v, out NuGetVersion parsed) ? parsed : null)
                .Where(v => v != null)
                .ToList();
        }
    }
}
