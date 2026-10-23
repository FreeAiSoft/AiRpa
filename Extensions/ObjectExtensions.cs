using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extensions
{
    public static class ObjectExtensions
    {
        public static string ToJson(this object source, Formatting formatting = Formatting.Indented)
        {
            JsonSerializerSettings setting = new JsonSerializerSettings
            {
                Formatting = formatting,
                NullValueHandling = NullValueHandling.Include,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                PreserveReferencesHandling = PreserveReferencesHandling.None,

                ObjectCreationHandling = ObjectCreationHandling.Replace
            };

            return source == null ? string.Empty : JsonConvert.SerializeObject(source, setting);
        }

        public static T CloneObject<T>(this object obj)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
        }
    }
}
