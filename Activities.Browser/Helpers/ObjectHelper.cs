using System;
using System.IO;
using Newtonsoft.Json;

namespace Activities.Browser.Helpers
{
    internal class ObjectHelper
    {
        /// <summary>
        /// Сериализация и сохранение объектов по указанному пути.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static void SaveObject(string filePath, object obj)
        {
            string output = JsonConvert.SerializeObject(obj);
            File.WriteAllText(filePath, output);
        }

        /// <summary>
        /// Десериализация json объектов.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static T InitObject<T>(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string jsonText = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<T>(jsonText);
                }
            }
            catch (Exception)
            {

            }
            return default;
        }

        //public static T CloneObject<T>(object obj)
        //{
        //    return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
        //}

        //internal static void SaveInstanse(object instance, string fileName)
        //{
        //    if (!Directory.Exists("data"))
        //    {
        //        _ = Directory.CreateDirectory("data");
        //    }

        //    _ = SaveObject(Path.Combine("data", fileName), instance);
        //}

        //internal static T LoadInstanse<T>(string fileName)
        //{
        //    return InitObject<T>(Path.Combine("data", fileName));
        //}
    }
}
