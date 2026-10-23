using System.Data;
using System.Data.SQLite;
using System.Text;
using Shared;

namespace CookiesManager
{
    public class CookiesImporter
    {
        public static int CookiesCounter { get; set; }

        public static CallResult SetCookies(string profilePath, List<InstantCookie> cookies, bool isOverride)
        {
            CookiesCounter = 0;

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

            using (SQLiteConnection conn = new($"Data Source={chromeCookiePath}"))
            {
                conn.Open();

                if (isOverride)
                {
                    using SQLiteCommand cmdDelete = conn.CreateCommand();
                    cmdDelete.CommandText = $"delete from cookies";
                    _ = cmdDelete.ExecuteNonQuery();
                }

                foreach (InstantCookie cookie in cookies)
                {
                    CookiesCounter++;

                    try
                    {
                        // шифруем куки ключом профиля, в который экспортируем куки
                        byte[] key = AesGcm256.GetKey(localStatePath);
                        byte[] decriptedValue = Encoding.UTF8.GetBytes(cookie.value);
                        byte[] newIv = AesGcm256.NewIv();
                        byte[] encriptedValue = AesGcm256.Encrypt(decriptedValue, key, newIv);

                        // проверяем, есть ли такие куки
                        // CREATE UNIQUE INDEX cookies_unique_index ON cookies(host_key, top_frame_site_key, name, path);
                        //cmd.CommandText = $"SELECT rowid FROM cookies WHERE host_key='{cookie.host_key}' AND name='{cookie.name}' AND path='{cookie.path}'";
                        using SQLiteCommand cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT rowid,creation_utc,host_key,top_frame_site_key,name,value,encrypted_value,path,expires_utc," +
                            $"is_secure,is_httponly,last_access_utc,has_expires,is_persistent,priority,samesite,source_scheme,source_port," +
                            $"last_update_utc FROM cookies WHERE host_key='{cookie.host_key}' AND top_frame_site_key='{cookie.top_frame_site_key}' AND name='{cookie.name}' AND path='{cookie.path}'";

                        using SQLiteDataReader reader = cmd.ExecuteReader();
                        if (reader.Read())
                        {
                            // update
                            int rowid = reader.GetInt32(0);

                            using SQLiteCommand cmdUpdate = conn.CreateCommand();
                            cmdUpdate.Parameters.Add("@encriptedValue", DbType.Binary).Value = encriptedValue;

                            cmdUpdate.CommandText = $"UPDATE cookies SET " +
                                $"creation_utc={cookie.creation_utc}," +
                                $"host_key='{cookie.host_key}'," +
                                $"top_frame_site_key='{cookie.top_frame_site_key}'," +
                                $"name='{cookie.name}'," +
                                $"encrypted_value=@encriptedValue," +
                                $"path='{cookie.path}'," +
                                $"expires_utc={cookie.expires_utc}," +
                                $"is_secure={cookie.is_secure}," +
                                $"is_httponly={cookie.is_httponly}," +
                                $"last_access_utc={cookie.last_access_utc}," +
                                $"has_expires={cookie.has_expires}," +
                                $"is_persistent={cookie.is_persistent}," +
                                $"priority={cookie.priority}," +
                                $"samesite={cookie.samesite}," +
                                $"source_scheme={cookie.source_scheme}," +
                                $"source_port={cookie.source_port}," +
                                $"last_update_utc={cookie.last_update_utc}," +
                                $"has_cross_site_ancestor={cookie.has_cross_site_ancestor}" +
                                $" WHERE rowid={rowid}";

                            _ = cmdUpdate.ExecuteNonQuery();
                        }
                        else
                        {
                            // insert
                            using SQLiteCommand cmdInsert = conn.CreateCommand();
                            cmdInsert.Parameters.Add("@encriptedValue", DbType.Binary).Value = encriptedValue;
                            cmdInsert.CommandText = $"INSERT INTO cookies (creation_utc,host_key,top_frame_site_key,name,value,encrypted_value,path,expires_utc,is_secure,is_httponly,last_access_utc,has_expires,is_persistent,priority,samesite,source_scheme,source_port,last_update_utc,source_type,has_cross_site_ancestor) " +
                                $"VALUES ({cookie.creation_utc},'{cookie.host_key}','{cookie.top_frame_site_key}','{cookie.name}','{cookie.top_frame_site_key}',@encriptedValue,'{cookie.path}',{cookie.expires_utc},{cookie.is_secure},{cookie.is_httponly},{cookie.last_access_utc},{cookie.has_expires},{cookie.is_persistent},{cookie.priority},{cookie.samesite},{cookie.source_scheme},{cookie.source_port},{cookie.last_update_utc},{cookie.source_type},{cookie.has_cross_site_ancestor});";

                            _ = cmdInsert.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex.Message);
                    }
                }
                conn.Close();
            }

            return errors.Count > 0 ? new() { Error = new Error(string.Join(Environment.NewLine, errors)) } : new();
        }
    }
}
