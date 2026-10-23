using System.ComponentModel.DataAnnotations.Schema;

namespace Shared.Entities
{
    public class UserAccount
    {
        public int Id { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }

        public User User { get; set; }

        public int? PartnerId { get; set; }

        public int? PartnerOldId { get; set; }

        public bool IsEmailConfirm { get; set; }

        public bool IsEmailNewsEnable { get; set; }

        public string EmailConfirmKey { get; set; }

        public string Password { get; set; }

        /// <summary>
        /// Является ли пользователь старым, до 07.03.2015
        /// </summary>
        public bool Is07032015 { get; set; }

        public decimal? OldBalance { get; set; }

        public long? OldPayedSeconds { get; set; }
    }
}
