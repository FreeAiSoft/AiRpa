using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace EasyMvvm.Core
{
    public abstract class BaseModel : INotifyPropertyChanged
    {
        [JsonIgnore]
        public BaseViewModel ViewModel { get; set; }

        private delegate void MethodContainer();

        #region Events

        public event EventHandler<string> SendMessage;

        /// <summary>
        /// События в дочернем классе будут работать только через обёртку
        /// </summary>
        /// <param name="message"></param>
        protected virtual void OnSendMessage(string message)
        {
            SendMessage?.Invoke(this, message);
        }

        #endregion Events

        /// <summary>
        /// Стартовый метод
        /// </summary>
        public virtual void Run() { }

        /// <summary>
        /// Окно загружено, но не показано
        /// </summary>
        public virtual void Loaded() { }

        public virtual void AfterLoaded() { }

#pragma warning disable IDE0390 // Make method synchronous
        public virtual async Task AfterLoadedAsync() { }
#pragma warning restore IDE0390 // Make method synchronous

        internal void SendCommand(object parameters)
        {
            MethodContainer mc;
            if (parameters is string)
            {
                mc = () => GetType().GetMethod(parameters.ToString())?.Invoke(this, null);
            }
            else
            {
                List<object> params_ = (List<object>)parameters;
                mc = params_.Count switch
                {
                    2 => () => GetType().GetMethod(params_[0] as string)?.Invoke(this, new object[] { params_[1] }),
                    3 => () => GetType().GetMethod(params_[0] as string)?.Invoke(this, new object[] { params_[1], params_[2] }),
                    4 => () => GetType().GetMethod(params_[0] as string)?.Invoke(this, new object[] { params_[1], params_[2], params_[3] }),
                    _ => () => throw new NotImplementedException()
                };
            }
            mc?.Invoke();
        }

        protected void Close()
        {
            ((Window)ViewModel.View).Dispatcher.Invoke(() => ((Window)ViewModel.View).Close());
        }

        public virtual void Closing() { }

        #region INotifyPropertyChanged code

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
            ViewModel?.OnPropertyChanged(prop);
        }

        #endregion
    }
}
