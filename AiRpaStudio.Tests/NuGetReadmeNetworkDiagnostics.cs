using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Диагностика вкладки README на реальной сети: печатает, что вернул поиск, скачался ли
/// <c>.nupkg</c> из выбранного источника и сколько символов README прочитано.
/// Явный (Explicit) — в обычном прогоне не выполняется; запускать при разборе «вкладка README
/// не появляется».
/// </summary>
[TestFixture, Explicit("Диагностика: требует доступа к nuget.org")]
public class NuGetReadmeNetworkDiagnostics
{
    [Test]
    public async Task LoadsReadmeFromNuGetOrg()
    {
        var lines = new List<string>();
        var search = new NuGetSearchService();
        var service = new NuGetPackageDetailsService();

        // Newtonsoft.Json — README в корне пакета, Microsoft.Extensions.Logging — README в nuspec.
        foreach (string id in new[] { "Newtonsoft.Json", "Microsoft.Extensions.Logging" })
        {
            NuGetPackageInfo info = await search.GetPackageInfoAsync(id, false);
            if (info == null)
            {
                lines.Add($"{id}: not found in source");
                continue;
            }

            byte[] nupkg = await search.DownloadNupkgAsync(id, info.Version);
            lines.Add($"{id} {info.Version}: nupkg={(nupkg == null ? "NULL" : nupkg.Length + " bytes")}");

            string readme = await service.TryReadReadmeAsync(search, id, info.Version);
            lines.Add($"   readme={(readme == null ? "NULL" : readme.Length + " chars")} err={service.LastReadmeError ?? "-"}");
            if (readme != null)
                lines.Add("   first line: " + readme.Split('\n')[0].Trim());
        }

        Assert.Pass(string.Join(Environment.NewLine, lines));
    }
}
