using System.Security.Cryptography;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace CookiesManager
{
    public class AesGcm256
    {
        private static readonly SecureRandom Random = new();

        /// <summary>
        /// Извлечение ключа из хранилища "Local State"(например C:\Users\Maxxx\AppData\Local\MyBot\ft2tp3xd.b51\Local State)
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static byte[] GetKey(string path)
        {
            string v = File.ReadAllText(path);

            dynamic json = JsonConvert.DeserializeObject(v);
            string key = json.os_crypt.encrypted_key;

            byte[] src = Convert.FromBase64String(key);
            byte[] encryptedKey = src.Skip(5).ToArray();

            byte[] decryptedKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);

            return decryptedKey;
        }

        public static byte[] Decrypt(byte[] encryptedBytes, byte[] key, byte[] iv)
        {
            try
            {
                GcmBlockCipher cipher = new(new AesEngine());
                AeadParameters parameters = new(new KeyParameter(key), 128, iv, null);

                cipher.Init(false, parameters);
                byte[] plainBytes = new byte[cipher.GetOutputSize(encryptedBytes.Length)];
                int retLen = cipher.ProcessBytes(encryptedBytes, 0, encryptedBytes.Length, plainBytes, 0);
                _ = cipher.DoFinal(plainBytes, retLen);

                return plainBytes;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            return null;
        }

        /// <summary>
        /// первые 3 байта - v10 от Chrome, следующие 12 байт nonce/IV/вектор инициализации, по научному - IV
        /// </summary>
        /// <param name="encryptedData"></param>
        /// <param name="nonce"></param>
        /// <param name="ciphertextTag"></param>
        public static void Prepare(byte[] encryptedData, out byte[] vi, out byte[] ciphertextTag)
        {
            vi = new byte[12];
            ciphertextTag = new byte[encryptedData.Length - 3 - vi.Length];

            Array.Copy(encryptedData, 3, vi, 0, vi.Length);
            Array.Copy(encryptedData, 3 + vi.Length, ciphertextTag, 0, ciphertextTag.Length);
        }

        /// <summary>
        /// Encrypt cookie
        /// </summary>
        /// <param name="plainBytes"></param>
        /// <param name="key"></param>
        /// <param name="iv"></param>
        /// <returns></returns>
        public static byte[] Encrypt(byte[] plainBytes, byte[] key, byte[] iv)
        {
            try
            {
                GcmBlockCipher cipher = new(new AesFastEngine());
                AeadParameters parameters = new(new KeyParameter(key), 128, iv, null);

                cipher.Init(true, parameters);

                byte[] encryptedBytes = new byte[cipher.GetOutputSize(plainBytes.Length)];
                int retLen = cipher.ProcessBytes(plainBytes, 0, plainBytes.Length, encryptedBytes, 0);
                _ = cipher.DoFinal(encryptedBytes, retLen);
                byte[] resultBytes = new byte[encryptedBytes.Length + 3 + 12];

                //пишем "v10"
                resultBytes[0] = 118;
                resultBytes[1] = 49;
                resultBytes[2] = 48;

                // пишем IV
                Array.Copy(iv, 0, resultBytes, 3, iv.Length);

                // пишем шифрованную строку
                Array.Copy(encryptedBytes, 0, resultBytes, 3 + iv.Length, encryptedBytes.Length);
                return resultBytes;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            return null;
        }

        /// <summary>
        /// Новый вектор инициализации
        /// </summary>
        /// <returns></returns>
        public static byte[] NewIv()
        {
            byte[] iv = new byte[12];
            Random.NextBytes(iv);
            return iv;
        }
    }
}
