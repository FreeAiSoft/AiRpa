using CookiesManager;
using Shared;
using System.Data.SQLite;
using System.Text;

namespace CookiesTest
{
    /// <summary>
    /// Сравнение кук двух профилей
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            long sourceCreationUtc = 13364203242734064;
            double unixTime = InstantCookie.WindowsTickToUnixSeconds(sourceCreationUtc);
            long result = InstantCookie.ConvertToInstantFormat(unixTime);
            long difference = sourceCreationUtc - result;
            if (difference != 0)
            {
                throw new Exception("конвертер пиздит");
            }

            string profilePath1 = "C:\\Users\\Maxxx\\AppData\\Local\\MyBot\\wfyukcxh.wja";
            string profilePath2 = "C:\\Users\\Maxxx\\AppData\\Local\\MyBot\\kz4ibs1z.t21";

            CallResult<List<InstantCookie>> callResult1 = GetCookies(profilePath1);
            CallResult<List<InstantCookie>> callResult2 = GetCookies(profilePath2);

            if (!callResult1.Success)
            {
                throw new Exception(callResult1.Error.Message);
            }

            if (!callResult2.Success)
            {
                throw new Exception(callResult2.Error.Message);
            }

            foreach (InstantCookie cookie1 in callResult1.Data)
            {
                InstantCookie cookie2 = callResult2.Data.FirstOrDefault(c => c.host_key == cookie1.host_key && c.name == cookie1.name);
                if (cookie2 == null)
                {
                    Console.WriteLine($"куки {cookie1.host_key} {cookie1.name} не найден");
                    continue;
                }

                Console.WriteLine($"куки {cookie1.host_key} {cookie1.name}");

                //path = path_;
                if (cookie1.path != cookie2.path)
                {
                    throw new Exception("не совпадают значения path");
                }

                //is_httponly = httpOnly;
                if (cookie1.is_httponly != cookie2.is_httponly)
                {
                    throw new Exception("не совпадают значения is_httponly");
                }

                //is_secure = secure;
                if (cookie1.is_secure != cookie2.is_secure)
                {
                    throw new Exception("не совпадают значения is_httponly");
                }

                //expires_utc = ConvertToInstantFormat(expires);
                var diff = cookie1.expires_utc - cookie2.expires_utc;
                if (Math.Abs(diff) > 10)
                {
                    throw new Exception("не совпадают значения expires_utc");
                }

                //value = value_;
                if (cookie1.value != cookie2.value)
                {
                    throw new Exception("не совпадают значения");
                }

                //has_expires = expires_utc > 0;
                if (cookie1.has_expires != cookie2.has_expires)
                {
                    throw new Exception("не совпадают значения has_expires");
                }

                //is_persistent = expires_utc > 0;
                if (cookie1.is_persistent != cookie2.is_persistent)
                {
                    throw new Exception("не совпадают значения is_persistent");
                }

                //samesite = 1; Netscape формате не экспортируется
                //if (cookie1.samesite != cookie2.samesite)
                //{
                //    throw new Exception("не совпадают значения samesite");
                //}

                //source_scheme = secure ? 2 : 1;
                if (cookie1.source_scheme != cookie2.source_scheme)
                {
                    throw new Exception("не совпадают значения source_scheme");
                }

                //source_port = secure ? 443 : 80;
                if (cookie1.source_port != cookie2.source_port)
                {
                    throw new Exception("не совпадают значения source_port");
                }

                //source_type = is_httponly ? 1 : 2;
                //if (cookie1.source_type != cookie2.source_type)
                //{
                //    throw new Exception("не совпадают значения source_type");
                //}
            }

            Console.WriteLine("всё ок");
            Console.ReadKey();
        }

        public static CallResult<List<InstantCookie>> GetCookies(string profilePath)
        {
            if (!Directory.Exists(profilePath))
            {
                throw new Exception("profile dir not exists");
            }

            string chromeCookiePath = Path.Combine(profilePath, @"Default\Network\Cookies");
            string localStatePath = Path.Combine(profilePath, "Local State");

            if (!File.Exists(chromeCookiePath))
            {
                throw new Exception("cookies file not exists");
            }

            if (!File.Exists(localStatePath))
            {
                throw new Exception("\"Local State\" file not exists");
            }

            List<string> errors = new();
            List<InstantCookie> cookies = new();

            using (SQLiteConnection conn = new($"Data Source={chromeCookiePath}"))
            using (SQLiteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT creation_utc,host_key,top_frame_site_key,name,value,encrypted_value,path,expires_utc," +
                    $"is_secure,is_httponly,last_access_utc,has_expires,is_persistent,priority,samesite,source_scheme,source_port," +
                    $"last_update_utc,source_type FROM cookies";
                byte[] key = AesGcm256.GetKey(localStatePath);

                conn.Open();

                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        try
                        {
                            long creation_utc = reader.GetInt64(0);
                            string host_key = reader.GetString(1);
                            string top_frame_site_key = reader.GetString(2);
                            string name = reader.GetString(3);

                            byte[] encryptedData = GetBytes(reader, 5);
                            AesGcm256.Prepare(encryptedData, out byte[] nonce, out byte[] ciphertextTag);
                            byte[] valueBytes = AesGcm256.Decrypt(ciphertextTag, key, nonce);
                            string value = Encoding.UTF8.GetString(valueBytes);

                            string path = reader.GetString(6);
                            long expires_utc = reader.GetInt64(7);
                            bool is_secure = reader.GetBoolean(8);
                            bool is_httponly = reader.GetBoolean(9);
                            long last_access_utc = reader.GetInt64(10);
                            bool has_expires = reader.GetBoolean(11);
                            bool is_persistent = reader.GetBoolean(12);
                            int priority = reader.GetInt32(13);
                            int samesite = reader.GetInt32(14);
                            int source_scheme = reader.GetInt32(15);
                            int source_port = reader.GetInt32(16);
                            long last_update_utc = reader.GetInt64(17);
                            int source_type = reader.GetInt32(18);

                            cookies.Add(new InstantCookie()
                            {
                                creation_utc = creation_utc,
                                host_key = host_key,
                                top_frame_site_key = top_frame_site_key,
                                name = name,
                                value = value,
                                path = path,
                                expires_utc = expires_utc,
                                is_secure = is_secure,
                                is_httponly = is_httponly,
                                last_access_utc = last_access_utc,
                                has_expires = has_expires,
                                is_persistent = is_persistent,
                                priority = priority,
                                samesite = samesite,
                                source_scheme = source_scheme,
                                source_port = source_port,
                                last_update_utc = last_update_utc,
                                source_type = source_type
                            });
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex.Message);
                        }
                    }
                }

                conn.Close();
            }

            Error error = errors.Count > 0 ? new Error(string.Join(Environment.NewLine, errors)) : null;
            return new CallResult<List<InstantCookie>>() { Data = cookies, Error = error };
        }

        private static byte[] GetBytes(SQLiteDataReader reader, int columnIndex)
        {
            const int CHUNK_SIZE = 2 * 1024;
            byte[] buffer = new byte[CHUNK_SIZE];
            long bytesRead;
            long fieldOffset = 0;
            using MemoryStream stream = new();
            while ((bytesRead = reader.GetBytes(columnIndex, fieldOffset, buffer, 0, buffer.Length)) > 0)
            {
                stream.Write(buffer, 0, (int)bytesRead);
                fieldOffset += bytesRead;
            }
            return stream.ToArray();
        }
    }
}
