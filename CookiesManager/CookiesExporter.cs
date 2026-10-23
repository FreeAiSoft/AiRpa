using System.Data.SQLite;
using System.Text;
using Shared;

namespace CookiesManager
{
    public class CookiesExporter
    {
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
                    $"last_update_utc,source_type,has_cross_site_ancestor FROM cookies";
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
                            //byte[] encryptedData = (byte[])reader["encrypted_value"];
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
                            int has_cross_site_ancestor = reader.GetInt32(19);

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
                                source_type = source_type,
                                has_cross_site_ancestor = has_cross_site_ancestor
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
