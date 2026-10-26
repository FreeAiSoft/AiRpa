namespace RPA.NuGet.Models
{
    /// <summary>
    /// Search result / package metadata returned from a package source.
    /// </summary>
    public class NuGetPackageInfo
    {
        public string Id { get; set; }

        public string Version { get; set; }

        public string Description { get; set; }

        public string Authors { get; set; }

        public long TotalDownloads { get; set; }
    }
}
