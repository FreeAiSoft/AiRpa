using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EasyMvvm.Core
{
    public abstract class BaseEntity : INotifyPropertyChanged
    {
        internal bool IsPropertyChangedNull => PropertyChanged == null;

        #region INotifyPropertyChanged code

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion
    }
}
