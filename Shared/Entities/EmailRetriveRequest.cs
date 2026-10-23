using System;
using System.ComponentModel.DataAnnotations;

namespace Shared.Entities
{
    /// <summary>
    /// Ненормализованная инфа о пользователе даст классу независимость
    /// </summary>
    public class EmailRetriveRequest
    {
        public int Id { get; set; }

        [Required]
        public string UserEmail { get; set; }

        [Required]
        public string UserLogin { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateTime DateTime { get; set; }

        [Required]
        public string Ip { get; set; }

        [Required]
        public string UserAgent { get; set; }

        [Required]
        public string RetriveKey { get; set; }
    }
}
