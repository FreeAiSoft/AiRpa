using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace Shared.Entities
{
    public class ServiceSettings
    {
        [DefaultValue("1")]
        [Key]
        public int Id { get; set; }

        public string LastPPVersion { get; set; }

        public string LastFreeVersion { get; set; }

        public string LastSwitchVersion { get; set; }

        public string UpdatePP { get; set; }

        public string UpdateFree { get; set; }

        public string UpdateSwitch { get; set; }
    }
}
