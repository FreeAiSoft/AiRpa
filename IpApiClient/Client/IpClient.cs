using System.Globalization;
using Extensions;
using Shared;
using Shared.Models;

namespace IpApiClient.Client
{
    public class IpClient : IDisposable
    {
        private readonly HttpClient httpClient;
        private const string baseUrl = "http://ip-api.com/json";

        public IpClient()
        {
            httpClient = new HttpClient()
            {
                Timeout = new TimeSpan(0, 0, 0, 20)
            };

            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public CallResult<IpInfo> GetIpInfo(string ip)
        {
            CultureInfo cultureInfo = Thread.CurrentThread.CurrentUICulture;
            string route = $"/{ip}?fields=66846719&lang={(cultureInfo.Name == "ru-RU" ? "ru" : "en")}";

            try
            {
                HttpResponseMessage result = httpClient.GetAsync($"{baseUrl}{route}").Result;
                string cont = result.Content.ReadAsStringAsync().Result;
                return new() { Data = cont.GetObjectFromJson<IpInfo>() };
            }
            catch (Exception ex)
            {
                return new() { Error = new(ex) };
            }
        }

        public void Dispose()
        {
            try
            {
                httpClient.Dispose();
            }
            catch { }
            GC.SuppressFinalize(this);
        }
    }
}
