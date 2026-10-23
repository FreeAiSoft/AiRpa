using System.ComponentModel.DataAnnotations;

namespace Shared.Entities
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        public int OldId { get; set; }

        [Required]
        public string Login { get; set; }

        [Required]
        public string Email { get; set; }

        public long RegTime { get; set; }

        public UserAccount UserAccount { get; set; }

        public UserPosterProfile UserPosterProfile { get; set; }

        public UserSwitchProfile UserSwitchProfile { get; set; }

        public UserCaptchaProfile UserCaptchaProfile { get; set; }

        public UserAffilateProfile UserAffilateProfile { get; set; }
    }
}
