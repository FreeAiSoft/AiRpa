using Newtonsoft.Json.Linq;
using ProxyChecker.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Windows;
using Shared.Helpers;
using Shared.Models;

namespace ProxyChecker.Views
{
    /// <summary>
    /// Interaction logic for ProxyImport.xaml
    /// </summary>
    public partial class ProxyImport : Window, INotifyPropertyChanged
    {
        public ProxyMod ProxyMod { get; set; }
        public string ErrorMessage { get; set; }

        public ProxyImport(ProxyMod proxyMod)
        {
            ProxyMod = proxyMod;
            InitializeComponent();
            DataContext = this;
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string reqStr = $"https://proxys.io/ru/api/v2/ip?key={ProxyMod.ProxyioKey}";
                string json = await Net.RespAsync(reqStr);
                //Log.Write("Ответ сервера: " + json);
                JObject jo = JObject.Parse(json);
                Type t2 = jo["data"].GetType();
                if (t2.Name == nameof(JArray))
                {
                    foreach (JToken order in jo["data"])
                    {
                        AddProxiesFromOrder(order);
                    }
                }
                if (t2.Name == nameof(JObject))
                {
                    AddProxiesFromOrder(jo["data"]);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка при попытке импортировать прокси:\n{ex.Message}";
            }
            Close();
        }

        private void AddProxiesFromOrder(JToken order)
        {
            string login = order["username"].ToString();
            string pwd = order["password"].ToString();
            foreach (JToken pr in order["list_ip"])
            {
                string ip = pr["ip"].ToString();
                int port = int.Parse(pr["port_https"].ToString());
                Proxy proxy = new() { Ip = ip, Port = port, Login = login, Pwd = pwd };
                _ = ProxyMod.AddNewProxy(proxy);
            }
        }

        #region INotifyPropertyChanged code

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        #endregion
    }
}
