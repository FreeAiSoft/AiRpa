using System.Collections.Generic;

namespace RpaCommon.ProxyModel
{
    public class TZ
    {
        public string Value { get; set; }

        public string Abbr { get; set; }

        public int Offset { get; set; }

        public string IsDST { get; set; }

        public string Text { get; set; }

        public List<string> UTC { get; set; } = new List<string>();

        public string TextWithUTC
        {
            get
            {
                string utc = UTC.Count > 1 ? UTC[0] : "";
                return $"{Text} {utc}";
            }
        }
    }
}
