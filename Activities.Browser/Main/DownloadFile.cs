using System;
using System.Activities;
using System.IO;
using System.Threading.Tasks;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using Activities.Browser.Resources;

namespace Activities.Browser
{
    [LocalizedDescription("download_file_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("download_file", typeof(ActivitiesBrowser))]
    public sealed class DownloadFile : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        [LocalizedDescription("url_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("url", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Url { get; set; }

        [LocalizedDescription("css_selector_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("css_selector", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> CSSSelector { get; set; }

        [LocalizedDescription("xpath_selector_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("xpath_selector", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> XPathSelector { get; set; }

        // If SavePath is a directory, file will be saved inside it with detected filename.
        // If SavePath is a full file path, bytes are written exactly there.
        public InArgument<string> SavePath { get; set; }

        [LocalizedDescription("saved_file_path_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("saved_file_path", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<string> SavedFilePath { get; set; }

        [LocalizedDescription("file_bytes_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("file_bytes", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<byte[]> Data { get; set; }

        [LocalizedDescription("is_success_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("is_success", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<bool> IsSuccess { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            string url = Url.Get(context);
            string css = CSSSelector.Get(context);
            string xpath = XPathSelector.Get(context);
            string savePathArg = SavePath.Get(context);

            var task = Task.Run(async () =>
            {
                string targetUrl = url;

                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    // Проверяем, что заполнен хотя бы один селектор
                    if (string.IsNullOrWhiteSpace(css) && string.IsNullOrWhiteSpace(xpath))
                    {
                        throw new ArgumentException("Необходимо указать URL или CSS-селектор/XPath-селектор");
                    }

                    // Если заполнены оба селектора, приоритет у CSS
                    bool useXPath = string.IsNullOrWhiteSpace(css) && !string.IsNullOrWhiteSpace(xpath);

                    if (useXPath)
                    {
                        targetUrl = await GetHrefByXPathAsync(page, xpath);
                    }
                    else if (!string.IsNullOrWhiteSpace(css))
                    {
                        targetUrl = await GetHrefByCSSAsync(page, css);
                    }
                }

                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    return (success: false, path: (string)null, data: (byte[])null);
                }

                var fetched = await FetchAsync(page, targetUrl);
                var bytes = fetched.Data;
                var response = fetched.Response;

                if (bytes == null || bytes.Length == 0)
                {
                    return (success: false, path: (string)null, data: (byte[])null);
                }

                string fileName = GetFileName(response, targetUrl);
                string finalPath = ComputeSavePath(savePathArg, fileName);

                string directory = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(finalPath, bytes);
                return (success: true, path: finalPath, data: bytes);
            });

            task.Wait();
            var result = task.Result;

            IsSuccess.Set(context, result.success);
            SavedFilePath.Set(context, result.path);
            Data.Set(context, result.data);
        }

        private async Task<(byte[] Data, IResponse Response)> FetchAsync(IPage page, string targetUrl)
        {
            // Navigate directly to the file URL and capture the response body via PuppeteerSharp
            IResponse response = await page.GoToAsync(targetUrl);
            if (response == null)
            {
                return (null, null);
            }

            try
            {
                byte[] buffer = await response.BufferAsync();
                return (buffer, response);
            }
            catch
            {
                // Some downloads may not expose a body via navigation. In such cases, return empty.
                return (null, response);
            }
        }

        private async Task<string> GetHrefByCSSAsync(IPage page, string css)
        {
            var element = await page.QuerySelectorAsync(css);
            if (element == null)
            {
                return null;
            }

            try
            {
                // Получаем href элемента, сначала свойство, потом атрибут
                string href = await page.EvaluateFunctionAsync<string>("el => el.href || el.getAttribute('href')", element);
                if (string.IsNullOrWhiteSpace(href))
                {
                    return null;
                }

                if (Uri.IsWellFormedUriString(href, UriKind.Absolute))
                {
                    return href;
                }

                // Преобразуем относительные URL в абсолютные
                var baseUri = new Uri(page.Url);
                var abs = new Uri(baseUri, href);
                return abs.ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> GetHrefByXPathAsync(IPage page, string xpath)
        {
            try
            {
                var element = await page.WaitForXPathAsync(xpath, new WaitForSelectorOptions { Timeout = 100 });
                if (element == null)
                {
                    return null;
                }

                // Получаем href элемента, сначала свойство, потом атрибут
                string href = await page.EvaluateFunctionAsync<string>("el => el.href || el.getAttribute('href')", element);
                if (string.IsNullOrWhiteSpace(href))
                {
                    return null;
                }

                if (Uri.IsWellFormedUriString(href, UriKind.Absolute))
                {
                    return href;
                }

                // Преобразуем относительные URL в абсолютные
                var baseUri = new Uri(page.Url);
                var abs = new Uri(baseUri, href);
                return abs.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string ComputeSavePath(string savePathArg, string fileName)
        {
            if (string.IsNullOrWhiteSpace(savePathArg))
            {
                return Path.Combine(Path.GetTempPath(), fileName);
            }

            // If SavePath points to an existing directory, append filename
            if (Directory.Exists(savePathArg))
            {
                return Path.Combine(savePathArg, fileName);
            }

            // If SavePath is a file path (directory may not exist yet), return as-is
            string dir = Path.GetDirectoryName(savePathArg);
            if (string.IsNullOrWhiteSpace(dir))
            {
                // Treat as filename in temp directory
                return Path.Combine(Path.GetTempPath(), savePathArg);
            }

            return savePathArg;
        }

        private string GetFileName(IResponse response, string url)
        {
            // Try Content-Disposition header
            try
            {
                if (response != null && response.Headers != null &&
                    response.Headers.TryGetValue("content-disposition", out string cd) && !string.IsNullOrWhiteSpace(cd))
                {
                    string name = ParseFileNameFromContentDisposition(cd);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            catch { /* ignore */ }

            // Fallback to URL path
            try
            {
                var u = new Uri(url);
                string name = Path.GetFileName(u.LocalPath);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch { /* ignore */ }

            // Ultimate fallback
            return $"download_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
        }

        private string ParseFileNameFromContentDisposition(string contentDisposition)
        {
            try
            {
                // filename*=UTF-8''encoded_name OR filename="name"
                int idxStar = contentDisposition.IndexOf("filename*=", StringComparison.OrdinalIgnoreCase);
                if (idxStar >= 0)
                {
                    string part = contentDisposition.Substring(idxStar + 10).Trim().Trim(';').Trim();
                    if (part.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
                    {
                        part = part.Substring(7);
                        part = Uri.UnescapeDataString(part);
                    }
                    return TrimQuotes(part);
                }

                int idx = contentDisposition.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string part = contentDisposition.Substring(idx + 9).Trim().Trim(';').Trim();
                    return TrimQuotes(part);
                }
            }
            catch { /* ignore */ }

            return null;
        }

        private string TrimQuotes(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                return s.Substring(1, s.Length - 2);
            }
            return s;
        }
    }
}
