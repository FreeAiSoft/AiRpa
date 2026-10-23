using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Extensions
{
    public static class StringExtensions
    {
        public static T GetObjectFromJson<T>(this string source) where T : class
        {
            return JsonConvert.DeserializeObject<T>(source);
        }
    }
}
