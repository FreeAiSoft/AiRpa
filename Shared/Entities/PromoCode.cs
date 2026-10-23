using System;

namespace Shared.Entities
{
    public class PromoCode
    {
        public int Id { get; set; }

        public int Discount { get; set; }

        public string Code { get; set; }

        public DateTime CreatedDateTime { get; set; }

        public DateTime EndDateTime { get; set; }

        public bool IsDelete { get; set; }
    }
}
