using System;
using System.Text.Json.Serialization;
using Shared.Enums;
using Shared.Etc;

namespace Shared.Models
{
    public class Proxy : NotifyPropertyChanged
    {
        #region Fields

        private string externalIp;
        private string externalPort;
        private string lastError;
        private DateTime? lastCheckedUtc;
        private Anonimity anonimity;
        private ProxyWorkStatus status;
        private TZ tz;
        private IpInfo ipInfo;
        private string comment;

        #endregion Fields

        #region Props

        public string Ip { get; set; }

        public string Domain { get; set; }

        [JsonIgnore]
        public string Host => Ip ?? Domain;

        public int Port { get; set; }

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

        // Newtonsoft (profiles.json / proxies.json) игнорирует System.Text.Json.JsonIgnore, поэтому явно.
        [JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string LastError
        {
            get => lastError;
            set
            {
                lastError = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public DateTime? LastCheckedUtc
        {
            get => lastCheckedUtc;
            set
            {
                lastCheckedUtc = value;
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

        public string Url => $"http://{Host}:{Port}";

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

        /// <summary>
        /// Внешний IP может меняться
        /// </summary>
        public bool IsDynamic { get; set; }

        #endregion Props

        public Proxy() { }

        public override string ToString()
        {
            return $"{Host}:{Port}";
        }

        public string ToStringWithAuth()
        {
            return $"{Host}:{Port}@{Login}:{Pwd}";
        }
    }
}
