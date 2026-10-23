using EasyMvvm.Core;
using Shared.Enums;
using Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shared;
using Shared.Helpers;
using ProxyChecker.Views;
using ProxyChecker.Helpers;
using IpApiClient.Client;
using System.Windows;

namespace ProxyChecker.Models
{
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public class ProxyMod : BaseModel
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        #region Events

        //public delegate void LogHandler(string message, Exception ex = null);
        //public event LogHandler Log;

        //public delegate void OpenLinkHandler(string link);
        //public event OpenLinkHandler OpenLink;

        public event Action<string> OpenLink;
        public event Action<string, Exception> WriteLog;

        #endregion Events

        #region MVVM

        #region Fields

        private string textProxyList;
        private int proxyFormatIndex;
        private int proxyTypeIndex;
        private ObservableCollection<Proxy> proxiesColl = new();
        private string proxyioKey;
        private string ownIp;

        #endregion

        #region Properties

        public string TextProxyList
        {
            get => textProxyList;
            set
            {
                textProxyList = value;
                OnPropertyChanged();
            }
        }

        public int ProxyFormatIndex
        {
            get => proxyFormatIndex;
            set
            {
                proxyFormatIndex = value;
                OnPropertyChanged();
            }
        }

        public int ProxyTypeIndex
        {
            get => proxyTypeIndex;
            set
            {
                proxyTypeIndex = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<Proxy> ProxiesColl
        {
            get => proxiesColl;
            private set
            {
                proxiesColl = value;
                OnPropertyChanged();
            }
        }

        public string ProxyioKey
        {
            get => proxyioKey;
            set
            {
                proxyioKey = value;
                OnPropertyChanged();
            }
        }

        public string OwnIp
        {
            get => ownIp;
            set
            {
                ownIp = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Commands

        public void AddProxyListCommand()
        {
            AddProxyList();
        }

        public void AddProxyFromFileCommand()
        {
            AddProxyFromFile();
        }

        public void CheckProxyAllCommand()
        {
            CheckAllProxy();
        }

        public void DelAllProxyCommand()
        {
            DelAllProxy();
        }

        public void BuyProxyCommand()
        {
            OpenLink("https://proxys.io/ru?refid=159219");
        }

        public async void CheckProxyCommand(object proxy)
        {
            await CheckProxy((Proxy)proxy);
        }

        public void DelProxyCommand(object proxy_)
        {
            _ = ProxiesColl.Remove((Proxy)proxy_);
        }

        #endregion Commands

        #endregion MVVM

        #region Fields

        private static Dictionary<string, bool> revealingheader;
        private StringCollection proxiesStrings;
        private ProxyProtocol protocol;
        private readonly string proxiesFile = "proxies.json";
        private Regex reg;
        private readonly IpClient ipClient;

        #endregion

        #region Init

        #region Singleton

        private ProxyMod()
        {
            if (ProxiesColl.Count == 0)
            {
                ProxiesColl = ObjectHelper.InitObject<ObservableCollection<Proxy>>(proxiesFile).Data ?? ProxiesColl;
            }

            ipClient = new();
            ProxiesColl.CollectionChanged += ProxiesColl_CollectionChanged;
        }

        public override void Loaded()
        {
            ((Window)ViewModel.View).Closing += ProxyMod_Closing;
        }

        private void ProxyMod_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _ = ObjectHelper.SaveObject(proxiesFile, ProxiesColl);
        }

        private static readonly ProxyMod instance = new();

        public static ProxyMod GetInstance(Action<string, Exception> writeLog, Action<string> openLink)
        {
            if (writeLog == null || openLink == null)
            {
                throw new ArgumentException("writeLog and openLink can't be null");
            }

            instance.WriteLog = writeLog;
            instance.OpenLink = openLink;
            return instance;
        }

        public static ProxyMod Get()
        {
            return instance.WriteLog == null ? throw new Exception("please init proxyMod") : instance;
        }

        #endregion Singleton

        private void ProxiesColl_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            _ = ObjectHelper.SaveObject(proxiesFile, ProxiesColl);
        }

        #endregion Init

        #region Actions

        #region AddProxyList

        private void AddProxyList()
        {
            if (string.IsNullOrWhiteSpace(TextProxyList))
            {
                string localizedMessage = (string)Application.Current.FindResource("lang_proxy_checker_messages_InputProxyListInTextField");
                OnSendMessage(localizedMessage);
                return;
            }
            proxiesStrings = TextHelper.Text2StringCollection(TextProxyList);
            AddProxiesToMod();
        }

        #endregion AddProxyList

        #region AddProxyFromFile

        private void AddProxyFromFile()
        {
            proxiesStrings = TextHelper.GetStringCollectionFromFile();
            AddProxiesToMod();
        }

        #endregion AddProxyFromFile

        #region CheckProxy

        public async Task CheckProxy(Proxy proxy)
        {
            try
            {
                proxy.Status = ProxyWorkStatus.Checking;
                proxy.Anonimity = Anonimity.Unknow;
                proxy.ExternalIp = null;
                proxy.ExternalPort = null;

                ProxyJudge pj = new();
                string judge = "http://azenv.mybot.su/";

                //здесь узнаём свой настоящий ip
                string result_ = await Net.RespAsync(judge);
                if (string.IsNullOrEmpty(result_))
                {
                    proxy.Status = ProxyWorkStatus.Unknow;
                    string localizedMessageFormat = (string)Application.Current.FindResource("lang_proxy_checker_messages_CanNotAccessToWithoutProxy");
                    string localizedMessage = string.Format(localizedMessageFormat, judge);
                    Logging(localizedMessage);
                    return;
                }

                Dictionary<string, string> values_ = ProxyJudge.Parse(result_);
                OwnIp = (values_.TryGetValue("REMOTE_ADDR", out string ip) ? ip : null) ?? throw new Exception("REMOTE_ADDR missing in field");
                if (revealingheader == null)
                {
                    RevealingheaderInit();
                }

                //здесь проверяем работоспособность и анонимность прокси
                string result = await Net.RespAsync(judge, proxy, 1);
                Dictionary<string, string> values = null;
                if (result != null)
                {
                    values = ProxyJudge.Parse(result);
                    bool findIp = values.TryGetValue("REMOTE_ADDR", out string visibleIp);
                    bool findPort = values.TryGetValue("REMOTE_PORT", out string visiblePort);
                    if (findIp)
                    {
                        proxy.Status = ProxyWorkStatus.IsAvailable;
                        proxy.ExternalIp = visibleIp;
                        proxy.ExternalPort = visiblePort;

                        try
                        {
                            CallResult<IpInfo> ipInfoResponse = ipClient.GetIpInfo(proxy.ExternalIp);
                            proxy.IpInfo = ipInfoResponse.Data;
                        }
                        catch (Exception ex)
                        {
                            string localizedMessage = (string)Application.Current.FindResource("lang_proxy_checker_messages_CanNotCheckProxy");
                            Logging(localizedMessage, ex);
                        }
                    }
                    else
                    {
                        proxy.Anonimity = Anonimity.Unknow;
                        proxy.Status = ProxyWorkStatus.NotAvailable;
                        return;
                    }
                }

                // Test to se if our ip adress exist in the result
                if (result == null)
                {
                    proxy.Anonimity = Anonimity.Unknow;
                    proxy.Status = ProxyWorkStatus.NotAvailable;
                    throw new Exception("Empty response");
                }

                if (!result.Contains("REQUEST_URI", StringComparison.CurrentCulture))
                {
                    proxy.Anonimity = Anonimity.Unknow;
                    throw new Exception("Did not find header info. The proxy may tamper with the response");
                }

                // Will go troght black and whitlist to see what level of anonymity this server provides
                foreach (KeyValuePair<string, string> value in values)
                {
                    if (revealingheader.TryGetValue(value.Key, out bool revealing)) // Returns true.
                    {
                        if (revealing)
                        {
                            proxy.Anonimity = Anonimity.Low;
                            Logging($"Found revealing header {value.Key} = {value.Value}. ");
                        }
                    }
                    else
                    {
                        proxy.Anonimity = Anonimity.Low;
                        Logging($"Have unknown header '{value.Key} : {value.Value}. ");
                    }
                }

                if (result.Contains(OwnIp))
                {
                    Logging("Have your ip in results. ");
                    proxy.Anonimity = Anonimity.None;
                }

                if (proxy.Anonimity == Anonimity.Unknow)
                {
                    proxy.Anonimity = Anonimity.High;
                }
            }
            catch (Exception ex)
            {
                Logging(null, ex);
            }
            _ = ObjectHelper.SaveObject(proxiesFile, ProxiesColl);
        }

        private void RevealingheaderInit()
        {
            // Hardcoded list of bad and good headers. 
            revealingheader = new Dictionary<string, bool>()
            {
                {"HTTP_CACHE_CONTROL", true},
                {"HTTP_CDN_SRC_IP", true},
                {"HTTP_CLIENT_IP", true},
                {"HTTP_REFERER", true},
                {"HTTP_IF_NONE_MATCH", true},
                {"HTTP_IF_MODIFIED_SINCE", true},
                {"HTTP_MAX_FORWARDS", true},
                {"HTTP_OCT_MAX_AGE", true},
                {"HTTP_PROXY_AGENT", true},
                {"HTTP_PROXY_CONNECTION", true},
                {"HTTP_VIA", true},
                {"HTTP_X_ACCEPT_ENCODING_PRONTOWIFI", true},
                {"HTTP_X_BLUECOAT_VIA", true},
                {"HTTP_X_FORWARDED_FOR", true},
                {"HTTP_X_FORWARD_FOR", true},
                {"HTTP_X_FORWARDED_HOST", true},
                {"HTTP_X_FORWARDED_SERVER", true},
                {"HTTP_X_MATO_PARAM", true},
                {"HTTP_X_NAI_ID", true},
                {"HTTP_X_PROXY_ID", true},
                {"HTTP_X_REAL_IP", true},
                {"HTTP_X_VIA", true},
                {"HTTP_XCNOOL_REMOTE_ADDR", true},
                {"HTTP_XROXY_CONNECTION", true},
                {"HTTP_XXPECT", true},
                {"HTTP_ACCEPT", false},
                {"HTTP_ACCEPT_ENCODING", false},
                {"HTTP_ACCEPT_LANGUAGE", false},
                {"HTTP_ACCEPT_CHARSET", false},
                {"HTTP_CONNECTION", false},
                {"HTTP_HOST", false},
                {"HTTP_USER_AGENT", false},
                {"REMOTE_ADDR", false},
                {"REMOTE_PORT", false},
                {"REQUEST_METHOD", false},
                {"REQUEST_TIME", false},
                {"REQUEST_TIME_FLOAT", false},
                {"REQUEST_SCHEME", false},
                {"REQUEST_URI", false}
            };
        }

        #endregion CheckProxy

        #region CheckAllProxy

#pragma warning disable IDE0390 // Make method synchronous
        private async void CheckAllProxy()
#pragma warning restore IDE0390 // Make method synchronous
        {
            for (int i = 0; i < proxiesColl.Count; i++)
            {
                //TODO:сдель по феншую
                //await CheckProxy(proxiesColl[i]);
                Proxy pr = proxiesColl[i];
                _ = Task.Run(() => CheckProxy(pr));
            }
        }

        #endregion CheckAllProxy

        #region DelAllProxy

        private void DelAllProxy()
        {
            ProxiesColl.Clear();
            TextProxyList = null;
        }

        #endregion DelAllProxy

        #region Common

        private void AddProxiesToMod()
        {
            protocol = ProxyTypeIndex == 0 ? ProxyProtocol.HTTP : ProxyProtocol.SOCKS5;
            reg = ProxyFormatIndex switch
            {
                //ip:port:login:password
                0 => new Regex(@"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5}):(?<login>[\S]{1,128}):(?<pwd>[\S]{1,128})$"),
                //ip:port@login:password
                1 => new Regex(@"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})@(?<login>[\S]{1,128}):(?<pwd>[\S]{1,128})$"),
                //ip:port|login:password
                2 => new Regex(@"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})\|(?<login>[\S]{1,128}):(?<pwd>[\S]{1,128})$"),
                //login:password@ip:port
                3 => new Regex(@"^(?<login>[\S]{1,128}):(?<pwd>[\S]{1,128})@(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})$"),
                //login:password:ip:port
                4 => new Regex(@"^(?<login>[\S]{1,128}):(?<pwd>[\S]{1,128}):(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})$"),
                //ip:port
                5 => new Regex(@"^(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(?<port>\d{1,5})$"),
                //domen:port
                6 => new Regex(@"^(?<domain>[a-zA-Z0-9][-a-zA-Z0-9]{0,62}(\.[a-zA-Z0-9][-a-zA-Z0-9]{0,62})+):(?<port>\d{1,5})$"),
                //login:password@domen:port
                7 => new Regex(@"^(?<login>[\S]{1,128}):(?<pwd>[\S]{1,128})@(?<domain>[^\s:]+):(?<port>\d{1,5})$"),
                //unknow proxy format
                _ => throw new ArgumentException("unknow proxy format")
            };

            StringBuilder errorsMessage = new();
            foreach (string proxy_ in proxiesStrings)
            {
                try
                {
                    string proxy = proxy_.Trim();
                    Match match = reg.Matches(proxy.Trim()).FirstOrDefault();
                    if (match == null)
                    {
                        string localizedMessageFormat = (string)Application.Current.FindResource("lang_proxy_checker_messages_BadFormatProxy");
                        string localizedMessage = string.Format(localizedMessageFormat, proxy);
                        _ = errorsMessage.AppendLine(localizedMessage);
                        continue;
                    }
                    string ip = reg.Matches(proxy).FirstOrDefault().Groups["ip"].Value;
                    string domain = reg.Matches(proxy).FirstOrDefault().Groups["domain"].Value;
                    int port = int.Parse(reg.Matches(proxy).FirstOrDefault().Groups["port"].Value);
                    string login = reg.Matches(proxy).FirstOrDefault().Groups["login"].Value;
                    string password = reg.Matches(proxy).FirstOrDefault().Groups["pwd"].Value;
                    Proxy pr = string.IsNullOrWhiteSpace(ip)
                        ? new Proxy() { Domain = domain, Port = port, Login = login, Pwd = password, ProxyProtocol = protocol }
                        : new Proxy() { Ip = ip, Port = port, Login = login, Pwd = password, ProxyProtocol = protocol };
                    CallResult res = AddNewProxy(pr);
                    if (!res.Success)
                    {
                        _ = errorsMessage.Append($"{res.Error.Message}\n");
                    }
                }
                catch (Exception ex)
                {
                    string localizedMessageFormat = (string)Application.Current.FindResource("lang_proxy_checker_messages_ParsingStringProxyError");
                    string localizedMessage = string.Format(localizedMessageFormat, proxy_);
                    _ = errorsMessage.Append(localizedMessage);
                    Logging(null, ex);
                }
            }

            if (errorsMessage.Length > 0)
            {
                OnSendMessage(errorsMessage.ToString());
            }
        }

        public CallResult AddNewProxy(Proxy pr)
        {
            Proxy proxies = ProxiesColl.FirstOrDefault(proxy => proxy.Host == pr.Host && proxy.Port == pr.Port);
            if (proxies == null)
            {
                ProxiesColl.Add(pr);
                return new();
            }
            string localizedMessageFormat = (string)Application.Current.FindResource("lang_proxy_checker_messages_ProxyAlreadyExist");
            string localizedMessage = string.Format(localizedMessageFormat, pr.Host, pr.Port);
            return new CallResult() { Error = new Error(localizedMessage) };
        }

        #endregion Common

        #region Import

        public void ImportProxy()
        {
            ProxyImport form = new(this);
            _ = form.ShowDialog();
            if (form.ErrorMessage != null)
            {
                OnSendMessage(form.ErrorMessage);
            }
        }

        #endregion Import

        #region ExportProxy

        public void ExportProxy()
        {
            if (ProxiesColl.Count == 0)
            {
                string localizedMessage = (string)Application.Current.FindResource("lang_proxy_checker_messages_ProxyListIsEmpty");
                OnSendMessage(localizedMessage);
                return;
            }

            StringBuilder sb = new();
            ProxiesColl.ToList().ForEach(p => sb.AppendLine($"{p.Host}:{p.Port}:{p.Login}:{p.Pwd}"));

            Microsoft.Win32.SaveFileDialog dialog = new()
            {
                Filter = "All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "proxies"
            };
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                Stream stream;

                if ((stream = dialog.OpenFile()) != null)
                {
                    string fileName = dialog.FileName;
                    stream.Close();

                    using StreamWriter file = new(@fileName);
                    file.Write(sb.ToString().Trim());
                }
            }
        }

        /// <summary>
        /// Чтобы null 10 не писать
        /// </summary>
        /// <param name="text"></param>
        private void Logging(string text, Exception ex = null)
        {
            WriteLog(text, ex);
        }

        #endregion ExportProxy

        #endregion Actions
    }
}
