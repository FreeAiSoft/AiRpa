using System.ComponentModel.DataAnnotations;

namespace Shared.Entities
{
    public class DiagnosticsInfo
    {
        [Key]
        public int Id { get; set; }

        public string Info { get; set; }

        public string UserName { get; set; }
    }
}
