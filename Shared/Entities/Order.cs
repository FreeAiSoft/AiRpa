using System;
using System.ComponentModel.DataAnnotations;
using Shared.Enums;

namespace Shared.Entities
{
    public class Order
    {
        public int Id { get; set; }

        [Required]
        public User User { get; set; }

        [Required]
        public DateTime Created { get; set; }

        /// <summary>
        /// Сумма, подлежащая оплате, установленная в ордере.
        /// </summary>
        [Required]
        public decimal OriginalSum { get; set; }

        /// <summary>
        /// Фактически оплаченная сумма.
        /// </summary>
        [Required]
        public decimal PaySum { get; set; }

        /// <summary>
        /// Промокод
        /// </summary>
        public string PromoCode { get; set; }

        /// <summary>
        /// Скидка по промокоду
        /// </summary>
        public int DiscontPercent { get; set; }

        /// <summary>
        /// Внешний идентификатор заказа
        /// </summary>
        public string BillId { get; set; }

        public OrderState OrderState { get; set; }

        public OrderType OrderType { get; set; }
    }
}
