using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Linq;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace RPA.NuGet.ViewModels
{
    /// <summary>
    /// Правая панель деталей: шапка (иконка, идентификатор, источник), информационная плашка,
    /// выбор версии с большой кнопкой действия, «Options», вкладки README / Package Details.
    /// <para>
    /// Действие большой кнопки считает <see cref="NuGetPackageStateResolver"/>: Install — пакета нет в
    /// проекте, Update/Downgrade — выбранная версия выше/ниже подключённой, Uninstall — выбрана
    /// подключённая версия. Само выполнение живёт в <c>NuGetPanelViewModel</c>.
    /// </para>
    /// </summary>
    public class NuGetPackageDetailsViewModel : ObservableObject
    {
        private NuGetPackageVersionInfo _selectedVersion;
        private NuGetPackageAction _action = NuGetPackageAction.None;
        private bool _isReadmeSelected = true;
        private string _readme;
        private string _sourceName;
        private NuGetPackageItemViewModel _item;

        public NuGetPackageDetailsViewModel()
        {
            Versions = new ObservableCollection<NuGetPackageVersionInfo>();
        }

        /// <summary>Сведения о пакете; <c>null</c> — в панели ничего не выбрано.</summary>
        public NuGetPackageDetails Details { get; private set; }

        /// <summary>Строка списка, из которой открыты детали (источник иконки, авторов и счётчика).</summary>
        public NuGetPackageItemViewModel Item
        {
            get => _item;
            private set
            {
                if (ReferenceEquals(_item, value))
                    return;

                // Смена строки обязана уведомлять шапку: картинка пакета приходит позже выбора
                // (LoadIconAsync), а привязка к Item.Icon запоминала null навсегда — из-за этого
                // правая колонка оставалась без логотипа и с «залипшим» монограммом.
                if (_item != null)
                    _item.PropertyChanged -= Item_PropertyChanged;

                _item = value;

                if (_item != null)
                    _item.PropertyChanged += Item_PropertyChanged;

                RaisePropertyChanged();
                RaiseIconChanged();
            }
        }

        /// <summary>Картинка пакета для шапки деталей; <c>null</c> — иконка ещё не загружена.</summary>
        public ImageSource PackageIcon => _item?.Icon;

        /// <summary>Иконка действительно есть: монограмм-заглушка показывается только без неё.</summary>
        public bool HasPackageIcon => _item?.HasIcon == true;

        /// <summary>Фолбэк шапки — та же монограмма, что и на плашке списка.</summary>
        public string PackageMonogram
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_item?.Monogram))
                    return _item.Monogram;

                string id = Id;
                return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Substring(0, 1).ToUpperInvariant();
            }
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NuGetPackageItemViewModel.Icon)
                || e.PropertyName == nameof(NuGetPackageItemViewModel.HasIcon))
            {
                RaiseIconChanged();
            }
            else if (e.PropertyName == nameof(NuGetPackageItemViewModel.Monogram))
            {
                RaisePropertyChanged(nameof(PackageMonogram));
            }
        }

        /// <summary>Уведомления шапки об иконке — и при смене карточки, и при её загрузке.</summary>
        private void RaiseIconChanged()
        {
            RaisePropertyChanged(nameof(PackageIcon));
            RaisePropertyChanged(nameof(HasPackageIcon));
            RaisePropertyChanged(nameof(PackageMonogram));
        }

        public ObservableCollection<NuGetPackageVersionInfo> Versions { get; }

        public bool HasDetails => Details != null;

        public string Id => Details?.Id ?? string.Empty;

        public string SourceName
        {
            get => _sourceName;
            set => SetProperty(ref _sourceName, value);
        }

        public string Authors => Details?.Authors;

        public string AuthorLine => string.IsNullOrWhiteSpace(Authors) ? string.Empty : "by " + Authors;

        public string Description => Details?.Description;

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public string ProjectUrl => Details?.ProjectUrl;

        public bool HasProjectUrl => !string.IsNullOrWhiteSpace(ProjectUrl);

        // ReportAbuseUrl удалён: ссылка вела на www.nuget.org.

        // HasReportAbuseUrl удалён.

        public string LicenseUrl => Details?.LicenseUrl;

        public bool HasLicenseUrl => !string.IsNullOrWhiteSpace(LicenseUrl);

        public string Tags => Details?.Tags;

        public bool HasTags => !string.IsNullOrWhiteSpace(Tags);

        /// <summary>Дата публикации выбранной версии в коротком формате (как в VS: «26.10.2026»).</summary>
        public string PublishedText => Details?.Published?.ToLocalTime().ToString("d") ?? string.Empty;

        public string PackageVersionText => SelectedVersion?.Version ?? Details?.Version ?? string.Empty;

        public string DownloadsText
        {
            get
            {
                string formatted = NuGetPackageStateResolver.FormatDownloads(Details?.TotalDownloads ?? 0);
                return string.IsNullOrEmpty(formatted) ? string.Empty : formatted + " downloads";
            }
        }

        public bool HasDownloads => !string.IsNullOrEmpty(DownloadsText);

        /// <summary>Выбранная в списке версия; от неё зависят подпись версии и действие кнопки.</summary>
        public NuGetPackageVersionInfo SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (!SetProperty(ref _selectedVersion, value))
                    return;

                UpdateAction();
            }
        }

        /// <summary>Версия, подключённая к проекту (для правил действия); <c>null</c> — пакета нет.</summary>
        public string InstalledVersion { get; private set; }

        /// <summary>Действие большой кнопки.</summary>
        public NuGetPackageAction Action
        {
            get => _action;
            private set
            {
                if (SetProperty(ref _action, value))
                    RaisePropertyChanged(nameof(ActionText));
            }
        }

        public string ActionText => NuGetPackageStateResolver.ActionText(Action);

        /// <summary>Вкладка «README» выбрана (по умолчанию — она, как в VS).</summary>
        public bool IsReadmeSelected
        {
            get => _isReadmeSelected;
            set
            {
                if (!SetProperty(ref _isReadmeSelected, value))
                    return;

                RaisePropertyChanged(nameof(IsPackageDetailsSelected));
                RaisePropertyChanged(nameof(IsReadmeVisible));
                RaisePropertyChanged(nameof(IsPackageDetailsVisible));
            }
        }

        public bool IsPackageDetailsSelected
        {
            get => !_isReadmeSelected;
            set
            {
                if (value)
                    IsReadmeSelected = false;
            }
        }

        /// <summary>Вкладка README показывается только когда README реально есть в пакете.</summary>
        public bool ShowReadmeTab => HasReadme;

        /// <summary>Текст README видим, если он есть и выбрана его вкладка.</summary>
        public bool IsReadmeVisible => _isReadmeSelected && HasReadme;

        /// <summary>«Package Details» видим всегда, кроме случая, когда показан README.</summary>
        public bool IsPackageDetailsVisible => !IsReadmeVisible;

        /// <summary>Текст README; <c>null</c>/пусто — README в пакете нет, вкладка не показывается.</summary>
        public string Readme
        {
            get => _readme;
            set
            {
                if (!SetProperty(ref _readme, value))
                    return;

                // README не пришёл — вкладка убирается, а содержимым становится «Package Details».
                if (!HasReadme && _isReadmeSelected)
                {
                    _isReadmeSelected = false;
                    RaisePropertyChanged(nameof(IsReadmeSelected));
                    RaisePropertyChanged(nameof(IsPackageDetailsSelected));
                }

                RaisePropertyChanged(nameof(HasReadme));
                RaisePropertyChanged(nameof(ShowReadmeTab));
                RaisePropertyChanged(nameof(IsReadmeVisible));
                RaisePropertyChanged(nameof(IsPackageDetailsVisible));
            }
        }

        public bool HasReadme => !string.IsNullOrWhiteSpace(_readme);

        public bool HasVersions => Versions.Count > 0;

        /// <summary>Заполняет панель новыми сведениями и выбирает версию (установленную или лучшую).</summary>
        public void Load(NuGetPackageDetails details, NuGetPackageItemViewModel item, string installedVersion, string sourceName)
        {
            Details = details;
            Item = item;
            InstalledVersion = installedVersion;
            SourceName = sourceName;
            Readme = null;
            IsReadmeSelected = true;

            Versions.Clear();
            if (details != null)
            {
                foreach (NuGetPackageVersionInfo version in details.Versions)
                {
                    // Признак «в проекте» берём из файла проекта (installedVersion), а не из ответа
                    // источника: источник не знает, какая версия подключена к этому проекту.
                    version.IsInstalled = !string.IsNullOrWhiteSpace(installedVersion)
                                          && string.Equals(version.Version, installedVersion, StringComparison.OrdinalIgnoreCase);
                    Versions.Add(version);
                }
            }

            // Выбор версии как в VS: подключённая версия, иначе первая стабильная, иначе первая доступная.
            SelectedVersion = Versions.FirstOrDefault(v => v.IsInstalled)
                              ?? Versions.FirstOrDefault(v => !v.IsPrerelease)
                              ?? Versions.FirstOrDefault();

            RaiseAllChanged();
        }

        public void Clear()
        {
            Details = null;
            Item = null;
            InstalledVersion = null;
            Versions.Clear();
            _selectedVersion = null;
            Action = NuGetPackageAction.None;
            Readme = null;
            RaiseAllChanged();
        }

        /// <summary>Пересчитывает действие после установки/удаления (вызывается панелью).</summary>
        public void ApplyInstalledVersion(string installedVersion)
        {
            InstalledVersion = installedVersion;

            foreach (NuGetPackageVersionInfo version in Versions)
            {
                version.IsInstalled = !string.IsNullOrWhiteSpace(installedVersion)
                                      && string.Equals(version.Version, installedVersion, StringComparison.OrdinalIgnoreCase);
            }

            UpdateAction();
        }

        private void UpdateAction()
        {
            Action = NuGetPackageStateResolver.ResolveAction(SelectedVersion?.Version, InstalledVersion);
            RaisePropertyChanged(nameof(PackageVersionText));
            RaisePropertyChanged(nameof(PublishedText));
        }

        private void RaiseAllChanged()
        {
            RaisePropertyChanged(nameof(HasDetails));
            RaisePropertyChanged(nameof(Id));
            RaisePropertyChanged(nameof(Authors));
            RaisePropertyChanged(nameof(AuthorLine));
            RaisePropertyChanged(nameof(Description));
            RaisePropertyChanged(nameof(HasDescription));
            RaisePropertyChanged(nameof(ProjectUrl));
            RaisePropertyChanged(nameof(HasProjectUrl));
            // ReportAbuseUrl удалён.
            // HasReportAbuseUrl удалён.
            RaisePropertyChanged(nameof(LicenseUrl));
            RaisePropertyChanged(nameof(HasLicenseUrl));
            RaisePropertyChanged(nameof(Tags));
            RaisePropertyChanged(nameof(HasTags));
            RaisePropertyChanged(nameof(PublishedText));
            RaisePropertyChanged(nameof(PackageVersionText));
            RaisePropertyChanged(nameof(DownloadsText));
            RaisePropertyChanged(nameof(HasDownloads));
            RaisePropertyChanged(nameof(HasVersions));
            RaisePropertyChanged(nameof(PackageIcon));
            RaisePropertyChanged(nameof(HasPackageIcon));
            RaisePropertyChanged(nameof(PackageMonogram));
            RaisePropertyChanged(nameof(ActionText));
        }
    }
}
