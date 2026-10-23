using System;
using System.IO;
using Newtonsoft.Json;

namespace Shared.Helpers
{
    public class ObjectHelper
    {
        public static CallResult SaveObject(string filePath, object obj)
        {
            try
            {
                string output = JsonConvert.SerializeObject(obj);
                File.WriteAllText(filePath, output);
                return new();
            }
            catch (Exception ex)
            {
                return new() { Error = new(ex) };
            }
        }

        public static CallResult<T> InitObject<T>(string filePath)
        {
            try
            {
                return File.Exists(filePath)
                    ? (new() { Data = JsonConvert.DeserializeObject<T>(File.ReadAllText(filePath)) })
                    : (new() { Error = new($"Не найден файл {filePath}") });
            }
            catch (Exception ex)
            {
                return new() { Error = new(ex) };
            }
        }

        public static T CloneObject<T>(T obj)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
        }
    }
}
