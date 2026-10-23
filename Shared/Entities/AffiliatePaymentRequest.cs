using System;
using System.ComponentModel.DataAnnotations;
using Shared.Enums;

namespace Shared.Entities
{
    public class AffiliatePaymentRequest
    {
        public int Id { get; set; }

        public DateTime DateTime { get; set; }

        public User User { get; set; }

        [Range(10, 1500)]
        public decimal Sum { get; set; }

        public RequestState RequestState { get; set; }

        public WalletType WalletType { get; set; }

        [Required]
        public string WalletIdentifier { get; set; }

        public string ConfirmKey { get; set; }

        public bool IsComplete { get; set; }
    }
}
