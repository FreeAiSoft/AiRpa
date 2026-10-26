using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace RPA.NuGet.ViewModels
{
    /// <summary>
    /// Lightweight view model for the dockable NuGet package manager panel.
    /// </summary>
    public class NuGetPanelViewModel : INotifyPropertyChanged
    {
        private readonly NuGetSearchService _search;
        private readonly NuGetPackageDownloader _downloader;

        private NuGetProjectPackages _project;
        private string _searchQuery;
        private bool _includePrerelease;
        private string _projectPath;
        private string _statusText;
        private bool _isBusy;

        public NuGetPanelViewModel(NuGetSearchService search = null, NuGetPackageDownloader downloader = null)
        {
            _search = search ?? new NuGetSearchService();
            _downloader = downloader ?? new NuGetPackageDownloader();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<NuGetPackageInfo> Available { get; } = new ObservableCollection<NuGetPackageInfo>();

        public ObservableCollection<NuGetPackageReference> Installed { get; } = new ObservableCollection<NuGetPackageReference>();

        public string SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(); }
        }

        public bool IncludePrerelease
        {
            get => _includePrerelease;
            set { _includePrerelease = value; OnPropertyChanged(); }
        }

        public string ProjectPath
        {
            get => _projectPath;
            set { _projectPath = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public bool HasProject => _project != null;

        /// <summary>Detects a .csproj inside the given folder (current studio project folder).</summary>
        public bool TryDetectProject(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return false;

            string csproj = Directory.EnumerateFiles(folder, "*.csproj", SearchOption.TopDirectoryOnly)
                .FirstOrDefault()
                ?? Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories)
                    .FirstOrDefault();

            if (csproj == null)
            {
                StatusText = "No .csproj found in the current project folder. Open a Visual Studio project or browse for one.";
                return false;
            }

            return LoadProject(csproj);
        }

        public bool LoadProject(string projectPath)
        {
            try
            {
                _project = NuGetProjectConfig.Load(projectPath);
                ProjectPath = _project.ProjectPath;
                RefreshInstalled();
                OnPropertyChanged(nameof(HasProject));
                StatusText = $"Loaded {(_project.IsSdkStyle ? "SDK-style" : "packages.config")} project: {Path.GetFileName(projectPath)}";
                return true;
            }
            catch (Exception ex)
            {
                StatusText = "Failed to load project: " + ex.Message;
                return false;
            }
        }

        public async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return;

            IsBusy = true;
            StatusText = "Searching...";
            try
            {
                var results = await _search.SearchAsync(SearchQuery, 20, IncludePrerelease);
                Available.Clear();
                foreach (var item in results)
                    Available.Add(item);

                StatusText = results.Count == 0 ? "No packages found." : $"Found {results.Count} package(s).";
            }
            catch (Exception ex)
            {
                StatusText = "Search failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task InstallAsync(NuGetPackageInfo package)
        {
            if (package == null || _project == null)
                return;

            IsBusy = true;
            StatusText = $"Installing {package.Id} {package.Version}...";
            try
            {
                if (!_project.IsSdkStyle)
                {
                    byte[] nupkg = await _downloader.DownloadNupkgAsync(package.Id, package.Version);
                    _downloader.Extract(nupkg, package.Id, package.Version, GetPackagesFolder());
                }

                NuGetProjectConfig.Add(_project, package.Id, package.Version);
                NuGetProjectConfig.Save(_project);
                RefreshInstalled();
                StatusText = $"Installed {package.Id} {package.Version}.";
            }
            catch (Exception ex)
            {
                StatusText = "Install failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task UpdateAsync(NuGetPackageReference package)
        {
            if (package == null || _project == null)
                return;

            IsBusy = true;
            StatusText = $"Updating {package.Id}...";
            try
            {
                var versions = await _search.GetVersionsAsync(package.Id, IncludePrerelease);
                string best = NuGetVersionResolver.SelectBestVersion(versions, package.Version, IncludePrerelease);
                if (best == null || string.Equals(best, package.Version, StringComparison.OrdinalIgnoreCase))
                {
                    StatusText = $"{package.Id} is already up to date.";
                    return;
                }

                if (!_project.IsSdkStyle)
                {
                    byte[] nupkg = await _downloader.DownloadNupkgAsync(package.Id, best);
                    _downloader.Extract(nupkg, package.Id, best, GetPackagesFolder());
                }

                NuGetProjectConfig.Update(_project, package.Id, best);
                NuGetProjectConfig.Save(_project);
                RefreshInstalled();
                StatusText = $"Updated {package.Id} to {best}.";
            }
            catch (Exception ex)
            {
                StatusText = "Update failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Uninstall(NuGetPackageReference package)
        {
            if (package == null || _project == null)
                return;

            NuGetProjectConfig.Remove(_project, package.Id);
            NuGetProjectConfig.Save(_project);
            RefreshInstalled();
            StatusText = $"Uninstalled {package.Id}.";
        }

        private void RefreshInstalled()
        {
            Installed.Clear();
            if (_project == null)
                return;

            foreach (var package in _project.Packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
                Installed.Add(package);
        }

        private string GetPackagesFolder()
        {
            string projectDir = Path.GetDirectoryName(_project.ProjectPath) ?? string.Empty;
            return Path.Combine(projectDir, "packages");
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

