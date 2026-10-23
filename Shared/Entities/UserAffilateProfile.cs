using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Shared.Entities
{
    public class UserAffilateProfile
    {
        public int Id { get; set; }

        [ForeignKey("User")]
        public int UserId { get; set; }

        public User User { get; set; }

        [Required]
        public string LinkTail { get; set; }

        public string QiwiWallet { get; set; }

        /// <summary>
        /// Кошелёк Юмани
        /// </summary>
        public string YoomoneyWallet { get; set; }

        /// <summary>
        /// Кошелёк СБП
        /// </summary>
        public string Sbp { get; set; }

        /// <summary>
        /// Кошелёк USDT
        /// </summary>
        public string Usdt { get; set; }

        public int VisitorsCount { get; set; }

        public int RegistersCount { get; set; }

        public int PaysCount { get; set; }

        [Range(0, 100000)]
        public decimal BonusBalance { get; set; }

        public decimal RubBalance { get; set; }

        /// <summary>
        /// Сумма покупок.
        /// </summary>
        public decimal TotalSum { get; set; }

        /// <summary>
        /// Все когда-либо заработанные бонусы.
        /// </summary>
        public decimal TotalBonuses { get; set; }
    }
}
