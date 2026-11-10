using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Чтение и запись <c>manifest.json</c> (`OR-03`).
    /// <para>
    /// Сериализатор — <see cref="DataContractJsonSerializer"/> из BCL: он есть и в
    /// <c>netstandard2.0</c> (сервер/студия), и в 4.7.2 (раннер), поэтому формат читается
    /// одинаково всеми сторонами без единого NuGet-пакета. Порядок полей задан
    /// <see cref="System.Runtime.Serialization.DataMemberAttribute.Order"/>, поэтому два одинаковых
    /// манифеста дают одинаковый байт-в-байт JSON — это нужно для хэшей и для диффов в ревью.
    /// </para>
    /// <para>
    /// Ошибки разбора не бросаются наружу: битый манифест — это отказ с текстом причины
    /// (для раннера — код 5, для пакетировщика — «пакет непригоден»), а не падение процесса.
    /// </para>
    /// </summary>
    public static class RobotPackageManifestJson
    {
        /// <summary>Манифест в JSON (одна строка, UTF-8 без BOM).</summary>
        public static string Write(RobotPackageManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var serializer = new DataContractJsonSerializer(typeof(RobotPackageManifest));

            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, manifest);

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>Разбирает манифест; исключения наружу не выходят.</summary>
        public static bool TryRead(string json, out RobotPackageManifest manifest, out string error)
        {
            manifest = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "манифест пуст";
                return false;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(RobotPackageManifest));

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    manifest = serializer.ReadObject(stream) as RobotPackageManifest;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (manifest == null)
            {
                error = "манифест не содержит объекта";
                return false;
            }

            return true;
        }

        /// <summary>Разбирает манифест из файла на диске.</summary>
        public static bool TryReadFile(string path, out RobotPackageManifest manifest, out string error)
        {
            manifest = null;
            error = null;

            try
            {
                return TryRead(File.ReadAllText(path, Encoding.UTF8), out manifest, out error);
            }
            catch (Exception ex)
            {
                error = "не удалось прочитать файл манифеста «" + path + "»: " + ex.Message;
                return false;
            }
        }
    }
}
