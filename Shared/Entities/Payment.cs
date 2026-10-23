using System;
using System.ComponentModel.DataAnnotations;

namespace Shared.Entities
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public Order Order { get; set; }

        [Required]
        public DateTime DateTime { get; set; }

        [Required]
        public string RequestData { get; set; }

        [Required]
        public decimal Sum { get; set; }

        public string PaySystem { get; set; }

        /// <summary>
        /// счёт, номер карты, кому уходит перевод
        /// </summary>
        public string Account { get; set; }
    }
}
