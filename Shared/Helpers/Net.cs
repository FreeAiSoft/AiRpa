using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Shared.Models;
using Shared.Extensions;

namespace Shared.Helpers
{
    public class Net
    {
        public static async Task<string> RespAsync(string requestPath, Proxy proxy = null, int attemptCount = 5)
        {

            for (int i = 0; i < attemptCount; i++)
            {
                try
                {
                    using HttpClient httpClient = proxy == null ? new() : GetProxyClient(proxy);
                    HttpResponseMessage resp = await httpClient.GetAsync(requestPath);
                    string content = await resp.Content.ReadAsStringAsync();

                    if (content == "")
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    return content;
                }
                catch (Exception ex)
                {
                    await Task.Delay(500);
                    Console.WriteLine($"Сбой метода RespAsync:\n{ex}");
                }
            }
            return null;
        }

        public static async Task<string> RespPostAsync(string requestPath, string parameters, Proxy proxy)
        {
            for (int i = 0; i <= 5; i++)
            {
                int k = i + 1;
                Console.WriteLine($"Отправляем запрос на сервер ВК({k})");
                try
                {
                    using HttpClient httpClient = proxy == null ? new() : GetProxyClient(proxy);

                    byte[] b = Encoding.UTF8.GetBytes(parameters);
                    Uri uri = new(requestPath);

                    StringContent stringContent = new(parameters, Encoding.UTF8, "application/x-www-form-urlencoded");
                    HttpResponseMessage responsePost = await httpClient.PostAsync(uri, stringContent);
                    string content = await responsePost.Content.ReadAsStringAsync();

                    if (content == "")
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    return content;
                }
                catch
                {
                    await Task.Delay(500);
                    Console.WriteLine("Вызвано исключение в методе RespPostAsync");
                }
            }
            Console.WriteLine("Что-то пошло не так, сервер не отвечает\n");
            return null;
        }

        private static HttpClient GetProxyClient(Proxy proxy)
        {
            HttpClientHandler handler = new()
            {
                Proxy = proxy.GetProxy()
            };

            return new(handler);
        }
    }
}
