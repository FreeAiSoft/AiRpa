using System;
using System.ComponentModel.DataAnnotations;

namespace Shared.Entities
{
    public class BonusTransaction
    {
        public int Id { get; set; }

        [Required]
        public DateTime DateTime { get; set; }

        [Required]
        public User Sender { get; set; }

        [Required]
        public User Reciver { get; set; }

        [Required]
        public decimal BonusSumm { get; set; }
    }
}
