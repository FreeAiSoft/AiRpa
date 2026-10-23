using Shared.Helpers;

namespace CookiesManager
{
    public class InstantCookie
    {
#pragma warning disable IDE1006 // Naming Styles
        public long creation_utc { get; set; }
        public string host_key { get; set; }
        public string top_frame_site_key { get; set; }
        public string name { get; set; }
        public string value { get; set; }
        public byte[] encrypted_value { get; set; }
        public string path { get; set; }
        public long expires_utc { get; set; }
        public bool is_secure { get; set; }
        public bool is_httponly { get; set; }
        public long last_access_utc { get; set; }
        public bool has_expires { get; set; }
        public bool is_persistent { get; set; }
        public int priority { get; set; }
        public int samesite { get; set; }
        public int source_scheme { get; set; }
        public int source_port { get; set; }
        public bool is_same_party { get; set; }
        public long last_update_utc { get; set; }
        public int source_type { get; set; }
        public int has_cross_site_ancestor { get; set; } = 1;
#pragma warning restore IDE1006 // Naming Styles

        private const int WINDOWS_TICK = 1000000;
        private const long SEC_TO_UNIX_EPOCH = 11644473600;

        public InstantCookie() { }

        public InstantCookie(string domain, bool httpOnly, string path_, bool secure, double expires, string name_, string value_, int sameSite_)
        {
            host_key = domain;
            path = path_;
            is_httponly = httpOnly;
            is_secure = secure;
            expires_utc = ConvertToInstantFormat(expires);
            name = name_;
            value = value_;

            creation_utc = GetCreationUtc();
            has_expires = expires_utc > 0;
            is_persistent = expires_utc > 0;
            priority = 1;
            samesite = sameSite_;
            //source_scheme = secure ? 2 : 1;
            //source_port = secure ? 443 : 80;
            source_scheme = 2;
            source_port = 443;
            last_update_utc = creation_utc;
            source_type = 2;
        }

#pragma warning disable IDE0051 // Remove unused private members
        private long GetCreationUtc()
#pragma warning restore IDE0051 // Remove unused private members
        {
            double unixTime = LinkHelpers.GetUtcUnixTime();
            return ConvertToInstantFormat(unixTime);
        }

        /// <summary>
        /// Convert from unix to windows
        /// </summary>
        /// <param name="expires"></param>
        /// <returns></returns>
        public static long ConvertToInstantFormat(double expires)
        {
            if (expires == 0)
            {
                return 0;
            }

            long ticksCount = (long)((expires + SEC_TO_UNIX_EPOCH) * WINDOWS_TICK);
            return ticksCount;
        }

        public static double WindowsTickToUnixSeconds(double windowsTicks)
        {
            double ticksCount = Math.Round((windowsTicks / WINDOWS_TICK) - SEC_TO_UNIX_EPOCH, 6);
            return ticksCount;
        }

        public static string ConvertSameSite(int samesite)
        {
            return samesite switch
            {
                -1 => "unspecified",
                0 => "no_restriction",
                1 => "lax",
                2 => "strict",
                _ => throw new Exception($"unknow sameSite value {samesite}")
            };
        }

        public static int ConvertSameSiteToInt(string sameSiteString)
        {
            return sameSiteString.ToLower() switch
            {
                "unspecified" => -1,
                "no_restriction" => 0,
                "none" => 0,
                "lax" => 1,
                "strict" => 2,
                _ => throw new Exception($"unknow sameSite value «{sameSiteString}»")
            };
        }
    }
}
