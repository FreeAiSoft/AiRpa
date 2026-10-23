using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Entities
{
    public class UserPosterProfile
    {
        public int Id { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }

        public User User { get; set; }

        public bool IsBuy { get; set; }

        public long EndAccount { get; set; }

        public string Token { get; set; }

        public bool IsTokenActive { get; set; }

        public int AppStartCount { get; set; }

        [NotMapped]
        public DateTime EndAccountDateTime => new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(EndAccount);
    }
}
