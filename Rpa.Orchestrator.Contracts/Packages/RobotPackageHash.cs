using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Хэши пакета робота (`OR-03`): пофайловые sha256 и хэш содержимого.
    /// <para>
    /// Хэш содержимого считается <b>по манифесту файлов</b> (путь, размер, sha256), а не по байтам
    /// <c>zip</c>: пересборка того же робота другим архиватором или с другим порядком файлов не
    /// должна выглядеть как новая версия. Обратная сторона — байты архива проверяются отдельно
    /// (<see cref="OfFile"/>), поэтому «пакет подменили в кэше» тоже видно.
    /// </para>
    /// </summary>
    public static class RobotPackageHash
    {
        /// <summary>Длина sha256 в шестнадцатеричном виде.</summary>
        public const int Sha256HexLength = 64;

        /// <summary>sha256 файла.</summary>
        public static string OfFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                return OfStream(stream);
            }
        }

        /// <summary>sha256 потока (поток читается до конца, позиция не восстанавливается).</summary>
        public static string OfStream(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        /// <summary>sha256 массива байтов.</summary>
        public static string OfBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        /// <summary>
        /// Хэш содержимого пакета: канонический список файлов (сортировка по пути, нижний регистр)
        /// в виде «путь, размер, sha256» — порядок перечисления в манифесте на результат не влияет.
        /// </summary>
        public static string OfContent(IEnumerable<RobotPackageFile> files)
        {
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            var text = new StringBuilder();

            foreach (RobotPackageFile file in files
                .Where(f => f != null)
                .OrderBy(f => RobotPackageFormat.NormalizeEntryPath(f.Path), StringComparer.Ordinal))
            {
                text.Append(RobotPackageFormat.NormalizeEntryPath(file.Path).ToLowerInvariant())
                    .Append('\n')
                    .Append(file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\n')
                    .Append((file.Sha256 ?? string.Empty).ToLowerInvariant())
                    .Append('\n');
            }

            return OfBytes(Encoding.UTF8.GetBytes(text.ToString()));
        }

        /// <summary>Строка похожа на sha256 в шестнадцатеричном виде.</summary>
        public static bool IsSha256Hex(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != Sha256HexLength)
            {
                return false;
            }

            foreach (char symbol in value)
            {
                bool hex = (symbol >= '0' && symbol <= '9')
                    || (symbol >= 'a' && symbol <= 'f')
                    || (symbol >= 'A' && symbol <= 'F');

                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToHex(byte[] hash)
        {
            var text = new StringBuilder(hash.Length * 2);

            foreach (byte value in hash)
            {
                text.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }
    }
}
