using System.ComponentModel;
using RPA.NuGet.Models;

namespace RPA.NuGet.ViewModels
{
    /// <summary>
    /// Строка таблицы оверлея «Package sources»: галочка «Enabled», имя и адрес источника.
    /// <para>
    /// Это редактируемая копия <see cref="NuGetSourceDefinition"/>: правки попадают в
    /// <c>NuGet.Config</c> только по команде панели, поэтому неудачное сохранение не ломает список.
    /// </para>
    /// </summary>
    public class NuGetSourceItemViewModel : ObservableObject
    {
        private string _name;
        private string _source;
        private bool _isEnabled;

        public NuGetSourceItemViewModel(NuGetSourceDefinition definition)
        {
            definition = definition ?? new NuGetSourceDefinition();
            _name = definition.Name;
            _source = definition.Url;
            _isEnabled = definition.IsEnabled;
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>Адрес источника (для offline-пакетов — путь к папке).</summary>
        public string Source
        {
            get => _source;
            set => SetProperty(ref _source, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public NuGetSourceDefinition ToDefinition()
        {
            return new NuGetSourceDefinition
            {
                Name = Name,
                Url = Source,
                IsEnabled = IsEnabled
            };
        }

        public override string ToString()
        {
            return Name + " = " + Source;
        }
    }
}
