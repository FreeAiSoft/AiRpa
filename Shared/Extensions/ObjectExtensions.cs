using Newtonsoft.Json;

namespace Shared.Extensions
{
    public static class ObjectExtensions
    {
        public static string ToJson(this object source, Formatting formatting = Formatting.Indented)
        {
            JsonSerializerSettings setting = new()
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
