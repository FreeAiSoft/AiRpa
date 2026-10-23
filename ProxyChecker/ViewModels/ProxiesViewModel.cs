using EasyMvvm.Core;
using ProxyChecker.Models;
using Shared.Models;
using System.Collections.ObjectModel;

namespace ProxyChecker.ViewModels
{
    public class ProxiesViewModel : BaseViewModel
    {
        private ProxyMod proxyMod;

        public override void InitializeViewModel()
        {
            proxyMod = (ProxyMod)Model;
        }

        #region Properties

        public string TextProxyList
        {
            get => proxyMod.TextProxyList;
            set
            {
                proxyMod.TextProxyList = value;
                OnPropertyChanged();
            }
        }

        public int ProxyFormatIndex
        {
            get => proxyMod.ProxyFormatIndex;
            set
            {
                proxyMod.ProxyFormatIndex = value;
                OnPropertyChanged();
            }
        }

        public int ProxyTypeIndex
        {
            get => proxyMod.ProxyTypeIndex;
            set
            {
                proxyMod.ProxyTypeIndex = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Proxy> ProxiesColl => proxyMod.ProxiesColl;

        public string ProxyioKey
        {
            get => proxyMod.ProxyioKey;
            set
            {
                proxyMod.ProxyioKey = value;
                OnPropertyChanged();
            }
        }

        public string OwnIp => proxyMod.OwnIp;

        #endregion
    }
}
