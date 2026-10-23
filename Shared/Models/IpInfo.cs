using System.Text;
using Newtonsoft.Json;
using Shared.Enums;

namespace Shared.Models
{
    public class IpInfo
    {
        /// <summary>
        /// IP used for the query
        /// </summary>
        [JsonProperty("query")]
        public string QueryIp { get; set; }

        /// <summary>
        /// success or fail
        /// </summary>
        [JsonProperty("status")]
        public IpApiClientStatus Status { get; set; }

        /// <summary>
        /// included only when status is fail
        /// Can be one of the following: private range, reserved range, invalid query
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Continent name
        /// </summary>
        [JsonProperty("continent")]
        public string Continent { get; set; }

        /// <summary>
        /// Two-letter continent code
        /// </summary>
        [JsonProperty("continentCode")]
        public string ContinentCode { get; set; }

        /// <summary>
        /// Country name
        /// </summary>
        [JsonProperty("country")]
        public string Country { get; set; }

        /// <summary>
        /// Two-letter country code ISO 3166-1 alpha-2
        /// </summary>
        [JsonProperty("countryCode")]
        public string CountryCode { get; set; }

        /// <summary>
        /// Region/state short code (FIPS or ISO)
        /// </summary>
        [JsonProperty("region")]
        public string Region { get; set; }

        /// <summary>
        /// Region/state
        /// </summary>
        [JsonProperty("regionName")]
        public string RegionName { get; set; }

        /// <summary>
        /// City
        /// </summary>
        [JsonProperty("city")]
        public string City { get; set; }

        /// <summary>
        /// District (subdivision of city)
        /// </summary>
        [JsonProperty("district")]
        public string District { get; set; }

        /// <summary>
        /// Zip code
        /// </summary>
        [JsonProperty("zip")]
        public string Zip { get; set; }

        /// <summary>
        /// Latitude
        /// </summary>
        [JsonProperty("lat")]
        public decimal Lat { get; set; }

        /// <summary>
        /// Longitude
        /// </summary>
        [JsonProperty("lon")]
        public decimal Lon { get; set; }

        /// <summary>
        /// Timezone (tz)
        /// </summary>
        [JsonProperty("timezone")]
        public string Timezone { get; set; }

        /// <summary>
        /// Timezone UTC DST offset in seconds
        /// </summary>
        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonIgnore]
        public string TimeOffset
        {
            get
            {
                StringBuilder timeOffset = new();
                _ = Offset >= 0 ? timeOffset.Append('+') : timeOffset.Append('-');
                _ = timeOffset.Append(Offset / 3600);
                _ = timeOffset.Append('.');
                _ = Offset % 3600 > 0 ? timeOffset.Append(Offset % 3600 / 60) : timeOffset.Append("00");
                return timeOffset.ToString();
            }
        }



        /// <summary>
        /// National currency
        /// </summary>
        [JsonProperty("currency")]
        public string Currency { get; set; }

        /// <summary>
        /// ISP name
        /// </summary>
        [JsonProperty("isp")]
        public string Isp { get; set; }

        /// <summary>
        /// Organization name
        /// </summary>
        [JsonProperty("org")]
        public string Org { get; set; }

        /// <summary>
        /// AS number and organization, separated by space (RIR). Empty for IP blocks not being announced in BGP tables.
        /// </summary>
        [JsonProperty("as")]
        public string As { get; set; }

        /// <summary>
        /// AS name (RIR). Empty for IP blocks not being announced in BGP tables.
        /// </summary>
        [JsonProperty("asname")]
        public string Asname { get; set; }

        /// <summary>
        /// Reverse DNS of the IP (can delay response)
        /// </summary>
        [JsonProperty("reverse")]
        public string Reverse { get; set; }

        /// <summary>
        /// Mobile (cellular) connection
        /// </summary>
        [JsonProperty("mobile")]
        public bool Mobile { get; set; }

        /// <summary>
        /// Proxy, VPN or Tor exit address
        /// </summary>
        [JsonProperty("proxy")]
        public bool Proxy { get; set; }

        /// <summary>
        /// Hosting, colocated or data center
        /// </summary>
        [JsonProperty("hosting")]
        public bool Hosting { get; set; }
    }
}
