using System;
using Newtonsoft.Json;
using RpaCommon.Enums;
using RpaCommon.Etc;

namespace RpaCommon.ProxyModel
{
    public class Proxy : NotifyPropertyChanged
    {
        #region Fields

        private string externalIp;
        private string externalPort;
        private Anonimity anonimity;
        private ProxyWorkStatus status;
        private TZ tz;
        private IpInfo ipInfo;
        private string comment;

        #endregion Fields

        #region Props

        public string Ip { get; set; }

        public string Port { get; set; }

        public string Login { get; set; }

        public string Pwd { get; set; }

        [JsonIgnore]
        public string ExternalIp
        {
            get => externalIp;
            set
            {
                externalIp = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string ExternalPort
        {
            get => externalPort;
            set
            {
                externalPort = value;
                OnPropertyChanged();
            }
        }

        public ProxyProtocol ProxyProtocol { get; set; }

        public Anonimity Anonimity
        {
            get => anonimity;
            set
            {
                anonimity = value;
                OnPropertyChanged();
            }
        }

        public ProxyWorkStatus Status
        {
            get => status;
            set
            {
                status = value;
                OnPropertyChanged();
            }
        }

        public TZ Tz
        {
            get => tz;
            set
            {
                tz = value;
                OnPropertyChanged();
            }
        }

        public string Url => $"http://{Ip}:{Port}";

        public IpInfo IpInfo
        {
            get => ipInfo;
            set
            {
                ipInfo = value;
                OnPropertyChanged();
            }
        }

        public string Comment
        {
            get => comment;
            set
            {
                comment = value;
                OnPropertyChanged();
            }
        }

        #endregion Props

        public Proxy() { }

        public Proxy(string ip, string port, string login, string pwd, ProxyProtocol protocol)
        {
            Ip = ip;
            Port = port;
            Login = login;
            Pwd = pwd;
            ProxyProtocol = protocol;
        }

        public Proxy(string proxyString)
        {
            string uncorrectFormat = $"uncorrectable format string of proxy-server: {proxyString}";
            if (proxyString.Contains("@"))
            {
                string[] s = proxyString.Split('@');
                if (s.Length < 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                string[] ipPort = s[0].Split(':');
                if (ipPort.Length < 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                Ip = ipPort[0];
                Port = ipPort[1];

                string[] loginPass = s[1].Split(':');
                if (loginPass.Length < 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                Login = loginPass[0];
                Pwd = loginPass[1];
            }
            else
            {
                string[] s = proxyString.Split(':');
                if (s.Length != 2)
                {
                    throw new ArgumentException(uncorrectFormat);
                }

                Ip = s[0];
                Port = s[1];
            }
        }

        public override string ToString()
        {
            return $"{Ip}:{Port}";
        }

        public string ToStringWithAuth()
        {
            return $"{Ip}:{Port}@{Login}:{Pwd}";
        }
    }
}
