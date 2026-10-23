using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FW.EasyMvvm.Core
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
        /// Окно загружено
        /// </summary>
        public virtual void Loaded() { }

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
                switch (params_.Count)
                {
                    case 2:
                        mc = () => GetType().GetMethod(params_[0] as string)?.Invoke(this, new object[] { params_[1] });
                        break;
                    case 3:
                        mc = () => GetType().GetMethod(params_[0] as string)?.Invoke(this, new object[] { params_[1], params_[2] });
                        break;
                    case 4:
                        mc = () => GetType().GetMethod(params_[0] as string)?.Invoke(this, new object[] { params_[1], params_[2], params_[3] });
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            mc?.Invoke();
        }

        protected void Close()
        {
            ((Window)ViewModel.View).Dispatcher.Invoke(() => ((Window)ViewModel.View).Close());
        }

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
