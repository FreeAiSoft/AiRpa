using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Entities
{
    public class UserCaptchaProfile
    {
        public int Id { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }

        public User User { get; set; }

        public int CaptchaCount { get; set; }
    }
}
