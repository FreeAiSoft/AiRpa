using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shared.Etc
{
    public class NotifyPropertyChanged : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged code

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion
    }
}
