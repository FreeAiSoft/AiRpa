using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Диагностика логотипов на реальной сети: печатает, что вернул поиск и удалось ли скачать картинку.
/// Явный (Explicit) — в обычном прогоне не выполняется; запускать при разборе «логотипы не видны».
/// </summary>
[TestFixture, Explicit("Диагностика: требует доступа к nuget.org")]
public class NuGetIconNetworkDiagnostics
{
    [Test, Apartment(ApartmentState.STA)]
    public async Task DownloadsIconsFromNuGetOrg()
    {
        var lines = new List<string>();
        var search = new NuGetSearchService();
        List<NuGetPackageInfo> packages = await search.SearchAsync("newtonsoft.json", 2, false, 0);
        var icons = new NuGetIconService();

        foreach (NuGetPackageInfo package in packages)
        {
            string url = NuGetIconService.BuildDefaultIconUrl(package.Id, package.Version);

            try
            {
                ImageSource direct = await icons.LoadAsync(url);
                lines.Add($"DIRECT {package.Id}: {(direct == null ? "NULL" : direct.Width + "x" + direct.Height)} err={icons.LastError ?? "-"}");
            }
            catch (Exception ex)
            {
                lines.Add($"DIRECT EX {package.Id}: {ex.GetType().Name}: {ex.Message}");
            }

            var item = new NuGetPackageItemViewModel(package);
            await item.LoadIconAsync();
            lines.Add($"   item {package.Id}: hasIcon={item.HasIcon} url={item.IconSourceUrl}");
        }

        Assert.Pass(string.Join(Environment.NewLine, lines));
    }
}
