using System.Resources;

namespace RPA.Helpers
{
    public class LocalizeHelper
    {
        private static readonly ResourceManager _resourceManager = new ResourceManager(typeof(Resources.Main));

        public static string Get(string key)
        {
            return _resourceManager.GetString(key);
        }

        public static string Get(string key, ResourceManager resourceManager)
        {
            return resourceManager.GetString(key);
        }

        public static string Get(string key, object data)
        {
            string localizedMessageFormat = Get(key);
            string localizedMessage = string.Format(localizedMessageFormat, data);
            return localizedMessage;
        }

        public static string Get(string key, object data1, object data2)
        {
            string localizedMessageFormat = Get(key);
            string localizedMessage = string.Format(localizedMessageFormat, data1, data2);
            return localizedMessage;
        }

        public static string Get(string key, object data1, object data2, object data3)
        {
            string localizedMessageFormat = Get(key);
            string localizedMessage = string.Format(localizedMessageFormat, data1, data2, data3);
            return localizedMessage;
        }
    }
}
