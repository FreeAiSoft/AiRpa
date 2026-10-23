using CookiesManager;
using Shared;
using System.IO;
using System.Xml.Linq;

namespace CookiesManagerTests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            string currentDir = Directory.GetCurrentDirectory();
            string projectDir = Path.GetFullPath(Path.Combine(currentDir, @"..\..\..\"));
            string profilesSourceDir = Path.Combine(projectDir, "Profiles");
            string profilesTargetDir = Path.Combine(currentDir, "Profiles");
            Directory.Delete(profilesTargetDir, true);
            CopyDir.CopyAll(new DirectoryInfo(profilesSourceDir), new DirectoryInfo(profilesTargetDir));

            // test вставки
            string source = Path.Combine(profilesTargetDir, "Source");
            string destination = Path.Combine(profilesTargetDir, "Destination");
            CallResult<List<InstantCookie>> cookies = CookiesExporter.GetCookies(source);
            CallResult result = CookiesImporter.SetCookies(destination, cookies.Data, true);
            CallResult<List<InstantCookie>> cookiesResult = CookiesExporter.GetCookies(destination);

            if (!cookies.Success || !result.Success || !cookiesResult.Success)
            {
                throw new Exception();
            }

            // все куки из cookies должны переписаться в cookiesResult
            // идентификация по: CREATE UNIQUE INDEX cookies_unique_index ON cookies(host_key, top_frame_site_key, name, path);
            foreach (var cookie in cookies.Data)
            {
                InstantCookie resultCookie = cookiesResult.Data.Where(c => c.host_key == cookie.host_key
                                                                        && c.top_frame_site_key == cookie.top_frame_site_key
                                                                        && c.name == cookie.name
                                                                        && c.path == cookie.path)
                                                                .First();
                if (resultCookie.is_httponly != cookie.is_httponly
                    || resultCookie.last_access_utc != cookie.last_access_utc
                    || resultCookie.is_persistent != cookie.is_persistent
                    || resultCookie.is_secure != cookie.is_secure
                    || resultCookie.is_same_party != cookie.is_same_party
                    || resultCookie.creation_utc != cookie.creation_utc
                    || resultCookie.expires_utc != cookie.expires_utc
                    || resultCookie.has_expires != cookie.has_expires
                    || resultCookie.last_update_utc != cookie.last_update_utc
                    || resultCookie.priority != cookie.priority
                    || resultCookie.samesite != cookie.samesite
                    || resultCookie.source_port != cookie.source_port
                    || resultCookie.source_scheme != cookie.source_scheme
                    || resultCookie.value != cookie.value)
                {
                    throw new Exception($"cookie not equal: host {cookie.host_key} name {cookie.name} value {cookie.value}");
                }
            }

            // тест обновления
            cookiesResult.Data.ForEach(c => c.expires_utc += 1000);
            CallResult updateResult = CookiesImporter.SetCookies(destination, cookiesResult.Data, true);
            CallResult<List<InstantCookie>> updatedCookiesResult = CookiesExporter.GetCookies(destination);
            if (!updateResult.Success || !updatedCookiesResult.Success)
            {
                throw new Exception("update test fail");
            }

            foreach (InstantCookie cookie in cookies.Data)
            {
                var updatedCookie = updatedCookiesResult.Data
                    .Where(c => c.host_key == cookie.host_key
                        && c.top_frame_site_key == cookie.top_frame_site_key
                        && c.name == cookie.name
                        && c.path == cookie.path);
                if (updatedCookie.Count() == 0)
                {
                    throw new Exception("updated cookie not found");
                }

                if (updatedCookie.Count() > 1)
                {
                    throw new Exception("updated cookie too much");
                }

                if (updatedCookie.First().expires_utc != cookie.expires_utc + 1000)
                {
                    throw new Exception("updated cookie do not correspond");
                }
            }

            Assert.Pass();
        }
    }
}