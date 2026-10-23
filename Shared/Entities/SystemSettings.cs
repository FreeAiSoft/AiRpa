using System;

namespace Shared.Entities
{
    public class SystemSettings
    {
        public int Id { get; set; }

        public string PosterProVersion { get; set; }

        public DateTime PosterProLastUpdate { get; set; }

        public string PosterFreeVersion { get; set; }

        public DateTime PosterFreeLastUpdate { get; set; }

        public string SwitchVersion { get; set; }

        public DateTime SwitchLastUpdate { get; set; }

        public string? StudioVersion { get; set; }

        public DateTime? StudioLastUpdate { get; set; }

        public int Discount { get; set; }

        public string ChromeLastVersion { get; set; }
    }
}
