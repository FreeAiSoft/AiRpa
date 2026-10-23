using System.Collections.Generic;

namespace Shared.Entities
{
    public class EnotPayment
    {
#pragma warning disable IDE1006 // Naming Styles
        public decimal amount { get; set; }

        public string order_id { get; set; }

        public string shop_id { get; set; }

        public string comment { get; set; }

        public List<string> include_service { get; set; }
#pragma warning restore IDE1006 // Naming Styles
    }
}
