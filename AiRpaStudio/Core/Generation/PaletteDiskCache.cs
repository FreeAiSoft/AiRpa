using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RPA.Logging;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Дисковый кэш метаданных палитры для холодного старта
    /// (план <c>StartupPerformance_Fix_Plan.md</c>, п. 3.1).
    /// <para>
    /// <see cref="ActivityPaletteRegistry"/> на холодном старте платит рефлексией Browser/OS-типов
    /// (~15 с). Этот кэш сериализует уже построенные <see cref="PaletteActivityDescriptor"/> в
    /// <c>%LOCALAPPDATA%\AiRpaStudio\PaletteCache\&lt;hash&gt;.json</c> и возвращает их на следующем
    /// запуске, оставляя рефлексию только для промахов.
    /// </para>
    /// <para>
    /// Инвалидация — по отпечатку: схема кэша + состав типов палитры + MVID каждой задействованной
    /// сборки. Смена любой сборки (пересборка пакета/библиотеки) или состава типов даёт другой ключ,
    /// поэтому устаревший файл просто не читается. Для встроенных WF-типов MVID стабилен для
    /// установленной версии <c>System.Activities</c>.
    /// </para>
    /// </summary>
    internal static class PaletteDiskCache
    {
        private const int CacheVersion = 1;

        private static readonly object WriteLock = new object();

        internal static string RootDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiRpaStudio",
            "PaletteCache");

        /// <summary>
        /// Отпечаток состава палитры: схема кэша + MVID задействованных сборок + полные имена типов.
        /// По нему файл кэша считается актуальным.
        /// </summary>
        internal static string ComputeKey(IEnumerable<ActivityPaletteCategory> categories)
        {
            var assemblyMvids = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var typeSignatures = new List<string>();

            foreach (ActivityPaletteCategory category in categories ?? Enumerable.Empty<ActivityPaletteCategory>())
            {
                if (category == null)
                {
                    continue;
                }

                foreach (Type type in category.Types ?? Enumerable.Empty<Type>())
                {
                    if (type == null)
                    {
                        continue;
                    }

                    string assemblyName;
                    string mvid;
                    try
                    {
                        Assembly assembly = type.Assembly;
                        assemblyName = assembly?.FullName ?? assembly?.GetName()?.Name ?? "?";
                        mvid = assembly?.ManifestModule?.ModuleVersionId.ToString("N") ?? "?";
                    }
                    catch
                    {
                        assemblyName = "?";
                        mvid = "?";
                    }

                    assemblyMvids[assemblyName] = mvid;
                    typeSignatures.Add((type.FullName ?? type.Name) + "|" + assemblyName);
                }
            }

            typeSignatures.Sort(StringComparer.Ordinal);

            var fingerprint = new StringBuilder();
            fingerprint.Append("schema:").Append(CacheVersion).Append(';');
            foreach (KeyValuePair<string, string> pair in assemblyMvids)
            {
                fingerprint.Append("asm:").Append(pair.Key).Append('=').Append(pair.Value).Append(';');
            }

            foreach (string signature in typeSignatures)
            {
                fingerprint.Append("type:").Append(signature).Append(';');
            }

            return Hash(fingerprint.ToString());
        }

        /// <summary>
        /// Читает дескрипторы из кэша. Возвращает <c>null</c>, если файла нет или он не подходит
        /// (устаревшая схема, битый JSON), и словарь «тип → дескриптор» при успехе.
        /// </summary>
        internal static Dictionary<Type, PaletteActivityDescriptor> Load(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string path = Path.Combine(RootDirectory, key + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                CacheEntry entry = JsonConvert.DeserializeObject<CacheEntry>(File.ReadAllText(path, Encoding.UTF8));
                if (entry == null || entry.CacheVersion != CacheVersion || entry.Descriptors == null)
                {
                    return null;
                }

                var descriptors = new Dictionary<Type, PaletteActivityDescriptor>();
                foreach (CachedDescriptor dto in entry.Descriptors)
                {
                    PaletteActivityDescriptor descriptor = FromDto(dto);
                    if (descriptor?.Type == null)
                    {
                        continue;
                    }

                    descriptors[descriptor.Type] = descriptor;
                }

                Log.Write($"[Palette] Дисковый кэш палитры прочитан: {descriptors.Count} дескрипторов ({path})");
                return descriptors;
            }
            catch (Exception ex)
            {
                Log.Write($"[Palette] Не удалось прочитать дисковый кэш палитры: {ex}");
                return null;
            }
        }

        /// <summary>Записывает дескрипторы в кэш (атомарно: сначала во временный файл, затем подмена).</summary>
        internal static void Save(string key, IEnumerable<PaletteActivityDescriptor> descriptors)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            try
            {
                var entry = new CacheEntry
                {
                    CacheVersion = CacheVersion,
                    Descriptors = (descriptors ?? Enumerable.Empty<PaletteActivityDescriptor>())
                        .Select(ToDto)
                        .Where(dto => dto != null)
                        .ToList()
                };

                string json = JsonConvert.SerializeObject(entry, Formatting.Indented);
                string directory = RootDirectory;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, key + ".json");

                lock (WriteLock)
                {
                    string temp = path + ".tmp";
                    File.WriteAllText(temp, json, Encoding.UTF8);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    File.Move(temp, path);
                }

                Log.Write($"[Palette] Дисковый кэш палитры записан: {entry.Descriptors.Count} дескрипторов ({path})");
            }
            catch (Exception ex)
            {
                Log.Write($"[Palette] Не удалось записать дисковый кэш палитры: {ex}");
            }
        }

        /// <summary>Удаляет все файлы кэша (состав палитры изменился, ключ мог не смениться).</summary>
        internal static void Invalidate()
        {
            try
            {
                string directory = RootDirectory;
                if (!Directory.Exists(directory))
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(directory, "*.json"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Занятый/заблокированный файл не должен ронять инвалидацию.
                    }
                }
            }
            catch
            {
                // Ошибка перечисления каталога кэша не критична: следующий запуск просто промахнётся.
            }
        }

        private static CachedDescriptor ToDto(PaletteActivityDescriptor descriptor)
        {
            if (descriptor?.Type == null)
            {
                return null;
            }

            return new CachedDescriptor
            {
                Key = descriptor.Key,
                DisplayName = descriptor.DisplayName,
                Category = descriptor.Category,
                TypeName = descriptor.Type.FullName,
                TypeAssembly = AssemblyNameOf(descriptor.Type),
                Description = descriptor.Description,
                Arguments = descriptor.Arguments.Select(ToDto).ToList(),
                Containers = descriptor.Containers.Select(ToDto).ToList()
            };
        }

        private static CachedArgument ToDto(PaletteArgumentDescriptor argument)
        {
            return new CachedArgument
            {
                Name = argument.Name,
                PropertyTypeName = argument.PropertyType?.FullName,
                PropertyTypeAssembly = AssemblyNameOf(argument.PropertyType),
                Direction = argument.Direction.ToString(),
                ValueTypeName = argument.ValueType?.FullName,
                ValueTypeAssembly = AssemblyNameOf(argument.ValueType),
                Required = argument.Required,
                DefaultValue = argument.DefaultValue,
                Description = argument.Description
            };
        }

        private static CachedContainer ToDto(PaletteContainerDescriptor container)
        {
            return new CachedContainer
            {
                Name = container.Name,
                Kind = container.Kind.ToString(),
                ChildValueTypeName = container.ChildValueType?.FullName,
                ChildValueTypeAssembly = AssemblyNameOf(container.ChildValueType),
                Description = container.Description
            };
        }

        private static PaletteActivityDescriptor FromDto(CachedDescriptor dto)
        {
            if (dto == null)
            {
                return null;
            }

            Type type = ResolveType(dto.TypeName, dto.TypeAssembly);
            if (type == null)
            {
                return null;
            }

            var arguments = new List<PaletteArgumentDescriptor>();
            if (dto.Arguments != null)
            {
                foreach (CachedArgument argumentDto in dto.Arguments)
                {
                    PaletteArgumentDescriptor argument = FromDto(argumentDto);
                    if (argument == null)
                    {
                        return null;
                    }

                    arguments.Add(argument);
                }
            }

            var containers = new List<PaletteContainerDescriptor>();
            if (dto.Containers != null)
            {
                foreach (CachedContainer containerDto in dto.Containers)
                {
                    PaletteContainerDescriptor container = FromDto(containerDto);
                    if (container == null)
                    {
                        return null;
                    }

                    containers.Add(container);
                }
            }

            return new PaletteActivityDescriptor(
                dto.Key,
                dto.DisplayName,
                dto.Category,
                type,
                dto.Description,
                arguments,
                containers);
        }

        private static PaletteArgumentDescriptor FromDto(CachedArgument dto)
        {
            if (dto == null)
            {
                return null;
            }

            Type propertyType = ResolveType(dto.PropertyTypeName, dto.PropertyTypeAssembly);
            if (propertyType == null)
            {
                return null;
            }

            if (!Enum.TryParse(dto.Direction, out PaletteArgumentDirection direction))
            {
                direction = PaletteArgumentDirection.Property;
            }

            Type valueType = null;
            if (!string.IsNullOrWhiteSpace(dto.ValueTypeName))
            {
                valueType = ResolveType(dto.ValueTypeName, dto.ValueTypeAssembly);
                if (valueType == null)
                {
                    return null;
                }
            }

            return new PaletteArgumentDescriptor(
                dto.Name,
                propertyType,
                direction,
                valueType,
                dto.Required,
                dto.DefaultValue,
                dto.Description);
        }

        private static PaletteContainerDescriptor FromDto(CachedContainer dto)
        {
            if (dto == null)
            {
                return null;
            }

            if (!Enum.TryParse(dto.Kind, out PaletteContainerKind kind))
            {
                kind = PaletteContainerKind.ActivityChild;
            }

            Type childValueType = null;
            if (!string.IsNullOrWhiteSpace(dto.ChildValueTypeName))
            {
                childValueType = ResolveType(dto.ChildValueTypeName, dto.ChildValueTypeAssembly);
                if (childValueType == null)
                {
                    return null;
                }
            }

            return new PaletteContainerDescriptor(dto.Name, kind, childValueType, dto.Description);
        }

        private static string AssemblyNameOf(Type type)
        {
            if (type == null)
            {
                return null;
            }

            try
            {
                return type.Assembly?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveType(string typeName, string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            string qualifiedName = string.IsNullOrWhiteSpace(assemblyName)
                ? typeName
                : typeName + ", " + assemblyName;

            try
            {
                Type type = Type.GetType(qualifiedName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
                // Падаем в fallback ниже.
            }

            // Сборки пакетов грузятся через LoadFrom и могут не резолвиться через Type.GetType.
            // Ищем уже загруженную сборку по имени и берём тип из неё.
            try
            {
                Assembly assembly = null;
                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    AssemblyName wanted = new AssemblyName(assemblyName);
                    assembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(loaded => string.Equals(
                            loaded.GetName().FullName,
                            wanted.FullName,
                            StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    assembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(loaded => loaded.GetType(typeName, throwOnError: false) != null);
                }

                return assembly?.GetType(typeName, throwOnError: false);
            }
            catch
            {
                return null;
            }
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return Convert.ToBase64String(bytes)
                    .Replace('/', '-')
                    .Replace('+', '_')
                    .Replace('=', '-');
            }
        }

        internal sealed class CacheEntry
        {
            public int CacheVersion { get; set; }

            public List<CachedDescriptor> Descriptors { get; set; }
        }

        internal sealed class CachedDescriptor
        {
            public string Key { get; set; }

            public string DisplayName { get; set; }

            public string Category { get; set; }

            public string TypeName { get; set; }

            public string TypeAssembly { get; set; }

            public string Description { get; set; }

            public List<CachedArgument> Arguments { get; set; }

            public List<CachedContainer> Containers { get; set; }
        }

        internal sealed class CachedArgument
        {
            public string Name { get; set; }

            public string PropertyTypeName { get; set; }

            public string PropertyTypeAssembly { get; set; }

            public string Direction { get; set; }

            public string ValueTypeName { get; set; }

            public string ValueTypeAssembly { get; set; }

            public bool Required { get; set; }

            public string DefaultValue { get; set; }

            public string Description { get; set; }
        }

        internal sealed class CachedContainer
        {
            public string Name { get; set; }

            public string Kind { get; set; }

            public string ChildValueTypeName { get; set; }

            public string ChildValueTypeAssembly { get; set; }

            public string Description { get; set; }
        }
    }
}
