using System;
using System.Threading.Tasks;
using System.Windows.Media;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace RPA.NuGet.ViewModels
{
    /// <summary>
    /// Строка списка пакетов (Browse / Installed / Updates): иконка, идентификатор, авторы, описание,
    /// количество скачиваний, версия и признак «есть обновление».
    /// <para>
    /// Данные карточки могут дополниться позже (<see cref="ApplyMetadata"/>): список пакетов проекта
    /// строится сразу из <c>packages.config</c>, а описание и иконка приходят из источника уже после
    /// этого. Иконка грузится лениво и один раз — список виртуализован, запрос за картинкой уходит
    /// только когда строку реально показали.
    /// </para>
    /// </summary>
    public class NuGetPackageItemViewModel : ObservableObject
    {
        private readonly Func<string, Task<ImageSource>> _iconLoader;

        private ImageSource _icon;
        private bool _iconRequested;
        private string _installedVersion;
        private string _latestVersion;
        private string _description;
        private string _iconUrl;
        private string _authors;
        private long _totalDownloads;

        public NuGetPackageItemViewModel(NuGetPackageInfo package, Func<string, Task<ImageSource>> iconLoader = null)
        {
            Package = package ?? new NuGetPackageInfo();
            _iconLoader = iconLoader;
            _iconUrl = Package.IconUrl;
            _description = Package.Description;
            _authors = Package.Authors;
            _totalDownloads = Package.TotalDownloads;

            // Версия из карточки — это версия в источнике, а не установленная: «в проекте» панель
            // выставляет явно через InstalledVersion (иначе все результаты поиска выглядели бы
            // подключёнными).
        }

        /// <summary>Данные карточки из источника.</summary>
        public NuGetPackageInfo Package { get; }

        public string Id => Package.Id;

        /// <summary>Версия, которую показывает строка: установленная, а если её нет — версия источника.</summary>
        public string Version => string.IsNullOrWhiteSpace(_installedVersion) ? Package.Version : _installedVersion;

        public string Authors
        {
            get => _authors;
            private set
            {
                if (SetProperty(ref _authors, value))
                    RaisePropertyChanged(nameof(AuthorLine));
            }
        }

        /// <summary>Строка «by Microsoft» под идентификатором; пустая, если авторов нет.</summary>
        public string AuthorLine => string.IsNullOrWhiteSpace(_authors) ? string.Empty : "by " + _authors;

        public string Description
        {
            get => _description;
            private set => SetProperty(ref _description, value);
        }

        public string IconUrl
        {
            get => _iconUrl;
            private set
            {
                if (!SetProperty(ref _iconUrl, value))
                    return;

                // Адрес иконки мог прийти позже (метаданные из источника): снимаем отметку, чтобы
                // строка попробовала загрузить картинку ещё раз.
                _iconRequested = false;
            }
        }

        /// <summary>«10.4M downloads» — только если источник отдал счётчик.</summary>
        public string DownloadsText
        {
            get
            {
                string formatted = NuGetPackageStateResolver.FormatDownloads(_totalDownloads);
                return string.IsNullOrEmpty(formatted) ? string.Empty : formatted + " downloads";
            }
        }

        /// <summary>Признак «популярный пакет» — звёздочка перед идентификатором (как в VS).</summary>
        public bool IsPopular => _totalDownloads >= 1000000;

        /// <summary>Монограмм-заглушка вместо иконки (первая буква идентификатора).</summary>
        public string Monogram => string.IsNullOrWhiteSpace(Id) ? "?" : Id.Substring(0, 1).ToUpperInvariant();

        public ImageSource Icon
        {
            get => _icon;
            private set
            {
                if (SetProperty(ref _icon, value))
                    RaisePropertyChanged(nameof(HasIcon));
            }
        }

        public bool HasIcon => _icon != null;

        /// <summary>Версия, подключённая к проекту; <c>null</c>/пусто — пакета в проекте нет.</summary>
        public string InstalledVersion
        {
            get => _installedVersion;
            set
            {
                if (!SetProperty(ref _installedVersion, value))
                    return;

                RaisePropertyChanged(nameof(IsInstalled));
                RaisePropertyChanged(nameof(Version));
                RaisePropertyChanged(nameof(StateText));
                RaisePropertyChanged(nameof(HasStateBadge));
                RaisePropertyChanged(nameof(HasUpdate));
                RaisePropertyChanged(nameof(CanInstallFromRow));
                RaisePropertyChanged(nameof(RowActionText));
            }
        }

        public bool IsInstalled => !string.IsNullOrWhiteSpace(_installedVersion);

        /// <summary>Лучшая доступная версия (по правилам <see cref="NuGetVersionResolver"/>).</summary>
        public string LatestVersion
        {
            get => _latestVersion;
            set
            {
                if (!SetProperty(ref _latestVersion, value))
                    return;

                RaisePropertyChanged(nameof(HasUpdate));
                RaisePropertyChanged(nameof(StateText));
                RaisePropertyChanged(nameof(HasStateBadge));
                RaisePropertyChanged(nameof(CanInstallFromRow));
                RaisePropertyChanged(nameof(RowActionText));
            }
        }

        public bool HasUpdate => IsInstalled
                                 && !string.IsNullOrWhiteSpace(_latestVersion)
                                 && !string.Equals(_latestVersion, _installedVersion, StringComparison.OrdinalIgnoreCase);

        /// <summary>Подпись состояния справа от версии (во вкладках Installed/Updates).</summary>
        public string StateText
        {
            get
            {
                if (!IsInstalled)
                    return string.Empty;

                return HasUpdate ? "Update " + _latestVersion : "Installed";
            }
        }

        public bool HasStateBadge => !string.IsNullOrEmpty(StateText);

        /// <summary>
        /// Кнопка установки на плашке нужна пакету, которого нет в проекте, или устаревшему пакету.
        /// </summary>
        public bool CanInstallFromRow => !IsInstalled || HasUpdate;

        /// <summary>Подпись кнопки на плашке: «Install» для нового пакета, «Update» для устаревшего.</summary>
        public string RowActionText => IsInstalled ? "Update" : "Install";

        /// <summary>Дополняет карточку данными из источника (описание, авторы, иконка, счётчик).</summary>
        public void ApplyMetadata(NuGetPackageInfo info)
        {
            if (info == null)
                return;

            Authors = info.Authors;
            Description = info.Description;
            IconUrl = info.IconUrl;
            _totalDownloads = info.TotalDownloads;
            RaisePropertyChanged(nameof(DownloadsText));
            RaisePropertyChanged(nameof(IsPopular));
        }

        /// <summary>Загружает иконку (идемпотентно). Ошибки не пробрасываются: остаётся монограмм.</summary>
        /// <summary>
        /// Address used to load the icon: source IconUrl, otherwise the nuget.org flat-container path
        /// (…/{id}/{version}/icon - the .png variant responds 404).
        /// </summary>
        public string IconSourceUrl =>
            !string.IsNullOrWhiteSpace(_iconUrl)
                ? _iconUrl
                : NuGetIconService.BuildDefaultIconUrl(Id, Package?.Version);

        public async Task LoadIconAsync()
        {
            // Отметку ставим только когда действительно идём за картинкой: при пустом адресе
            // запрос обязан повториться после прихода метаданных.
            string url = IconSourceUrl;
            if (_iconRequested || _iconLoader == null || string.IsNullOrWhiteSpace(url))
                return;

            _iconRequested = true;

            try
            {
                Icon = await _iconLoader(url);
            }
            catch (Exception)
            {
                Icon = null;
            }
        }

        public override string ToString()
        {
            return (Id ?? string.Empty) + " " + (Version ?? string.Empty);
        }
    }
}
