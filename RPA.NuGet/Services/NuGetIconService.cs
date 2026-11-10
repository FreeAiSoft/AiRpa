using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Загружает иконки пакетов для строк списка и шапки деталей. Адрес иконки приходит из
    /// метаданных выбранного источника (<see cref="NuGetPackageInfo.IconUrl"/>); жёсткого фолбэка
    /// на nuget.org больше нет — если источника-адреса нет, панель рисует монограмм.
    /// <para>
    /// Иконки кешируются дважды: в памяти (повторный выбор пакета не ходит в сеть) и на диске
    /// (<c>%LocalAppData%\AiRpaStudio\NuGetIcons</c>) — при следующем запуске студии панель
    /// отрисовывается без ожидания. Ошибки сети не пробрасываются: панель рисует монограмм-заглушку
    /// вместо иконки (см. шаблон строки в <c>NuGetPanel.Styles.xaml</c>).
    /// </para>
    /// </summary>
    public class NuGetIconService
    {
        private const int MaxIconBytes = 4 * 1024 * 1024;
        private const int Attempts = 3;

        private static readonly Dictionary<string, ImageSource> MemoryCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        private readonly HttpClient _http;

        static NuGetIconService()
        {
            try
            {
                // У .NET Framework 4.8 по умолчанию SystemDefault, и соединение с https-источником
                // может обрываться на записи. TLS 1.2 включаем явно — как это делает NuGet.Protocol.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // Старая ОС без TLS 1.2 — остаётся системное значение.
            }
        }

        public NuGetIconService(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        /// <summary>Текст последней ошибки загрузки (диагностика).</summary>
        public string LastError { get; private set; }

        /// <summary>Каталог дискового кеша иконок.</summary>
        public static string CacheDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiRpaStudio",
            "NuGetIcons");

        /// <summary>Иконка по адресу; <c>null</c> — адреса нет или загрузить не удалось.</summary>
        public virtual async Task<ImageSource> LoadAsync(string iconUrl)
        {
            LastError = null;
            if (string.IsNullOrWhiteSpace(iconUrl))
                return null;

            lock (MemoryCache)
            {
                if (MemoryCache.TryGetValue(iconUrl, out ImageSource cached))
                {
                    NuGetLog.Debug("Icon loaded from memory cache: " + iconUrl);
                    return cached;
                }
            }

            byte[] data = ReadCacheFile(iconUrl);
            if (data == null || data.Length == 0)
            {
                data = await DownloadWithRetryAsync(iconUrl).ConfigureAwait(false);
                if (data == null || data.Length == 0)
                    return null;

                WriteCacheFile(iconUrl, data);
            }

            ImageSource image = CreateImage(data);
            if (image == null)
            {
                LastError = "icon is not a valid image";
                NuGetLog.Warn("Icon is not a valid image: " + iconUrl);
                return null;
            }

            lock (MemoryCache)
            {
                MemoryCache[iconUrl] = image;
            }

            return image;
        }

        /// <summary>Создаёт замороженное изображение из байтов (без потока-владельца и без файловых блокировок).</summary>
        public static ImageSource CreateImage(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            // BitmapImage, созданный на потоке пула, молча не привязывается к Image.Source.
            Dispatcher dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => CreateImageCore(data));

            return CreateImageCore(data);
        }

        private static ImageSource CreateImageCore(byte[] data)
        {
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    var image = new BitmapImage();
                    image.BeginInit();

                    // IgnoreImageCache здесь нельзя: он работает только с UriSource, а со
                    // StreamSource бросает ArgumentNullException («Ключ не может быть неопределенным»),
                    // из-за чего иконка не создавалась ни для одного пакета.
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception)
            {
                // Не картинка (например, HTML-страница ошибки) — остаётся монограмм.
                return null;
            }
        }

        private async Task<byte[]> DownloadWithRetryAsync(string iconUrl)
        {
            Exception last = null;
            NuGetLog.Info("Icon download: " + iconUrl);

            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    using (var response = await _http.GetAsync(iconUrl).ConfigureAwait(false))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            if (data.Length > 0 && data.Length <= MaxIconBytes)
                            {
                                NuGetLog.Debug("Icon downloaded: " + data.Length + " bytes from " + iconUrl);
                                return data;
                            }

                            last = new InvalidOperationException("unexpected icon size: " + data.Length);
                        }
                        else
                        {
                            last = new InvalidOperationException("HTTP " + (int)response.StatusCode);
                            if (response.StatusCode == HttpStatusCode.NotFound)
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                if (attempt < Attempts)
                    await Task.Delay(200 * attempt).ConfigureAwait(false);
            }

            LastError = last == null ? "unknown error" : last.GetBaseException().Message;
            NuGetLog.Warn("Icon download failed: " + iconUrl + " -> " + LastError);
            return null;
        }

        private static byte[] ReadCacheFile(string iconUrl)
        {
            try
            {
                string path = CacheFilePath(iconUrl);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteCacheFile(string iconUrl, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllBytes(CacheFilePath(iconUrl), data);
            }
            catch (Exception)
            {
                // Кеш на диске — оптимизация: без него иконка живёт только в памяти.
            }
        }

        private static string CacheFilePath(string iconUrl)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(iconUrl.ToLowerInvariant()));
                return Path.Combine(CacheDirectory, BitConverter.ToString(hash).Replace("-", string.Empty) + ".img");
            }
        }
    }
}
