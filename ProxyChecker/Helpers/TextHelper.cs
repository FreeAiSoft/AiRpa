using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;

namespace ProxyChecker.Helpers
{
    internal class TextHelper
    {
        public static StringCollection Text2StringCollection(string text)
        {
            StringCollection coll = new();
            if (string.IsNullOrWhiteSpace(text))
            {
                return coll;
            }

            string[] lines = text.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string str in lines)
            {
                if (!string.IsNullOrWhiteSpace(str))
                {
                    _ = coll.Add(str);
                }
            }
            return coll;
        }

        public static StringCollection GetStringCollectionFromFile()
        {
            StringCollection coll = new();
            try
            {
                Microsoft.Win32.OpenFileDialog dialog = new()
                {
                    Filter = "All files (*.*)|*.*"
                };
                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    string line;
                    StreamReader file = new(dialog.FileName);
                    while ((line = file.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }
                        _ = coll.Add(line);
                    }
                    file.Close();
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex);
            }
            return coll;
        }

        public static string StringCollection2Text(StringCollection coll)
        {
            StringBuilder text = new();
            foreach (string id in coll)
            {
                _ = text.AppendLine(id);
            }
            return text.ToString();
        }
    }
}
