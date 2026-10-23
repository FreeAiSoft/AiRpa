using System;
using Activities.Browser.Enums;

namespace Activities.Browser.Entities
{
    public class Proxy
    {

        #region Fields

        #endregion Fields

        #region Props

        public string Ip { get; set; }

        public string Port { get; set; }

        public string Login { get; set; }

        public string Pwd { get; set; }

        public ProxyProtocol ProxyProtocol { get; set; }

        public TZ Tz { get; set; }

        public string Url => $"http://{Ip}:{Port}";

        #endregion Props

        public override string ToString()
        {
            return $"{Ip}:{Port}";
        }

        public Proxy()
        {

        }

        public Proxy(string proxyString)
        {
            string uncorrectFormat = $"uncorrectable format string of proxy-server: {proxyString}";
            if (proxyString.Contains("@"))
            {
                string[] s = proxyString.Split('@');
                if (s.Length < 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                string[] ipPort = s[0].Split(':');
                if (ipPort.Length < 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                Ip = ipPort[0];
                Port = ipPort[1];

                string[] loginPass = s[1].Split(':');
                if (loginPass.Length < 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                Login = loginPass[0];
                Pwd = loginPass[1];
            }
            else
            {
                string[] s = proxyString.Split(':');
                if (s.Length != 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                Ip = s[0];
                Port = s[1];
            }
        }
    }
}
