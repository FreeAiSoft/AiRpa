using System;
using System.Net;
using Shared.Models;

namespace Shared.Extensions
{
    public static class ProxyExtensions
    {
        public static WebProxy GetProxy(this Proxy proxy)
        {
            WebProxy pr = new()
            {
                Address = new Uri($"{proxy.ProxyProtocol.ToString().ToLower()}://{proxy.Host}:{proxy.Port}")
            };

            if (proxy.Login != null && proxy.Pwd != null)
            {
                pr.Credentials = new NetworkCredential(proxy.Login, proxy.Pwd);
            }

            return pr;
        }
    }
}
