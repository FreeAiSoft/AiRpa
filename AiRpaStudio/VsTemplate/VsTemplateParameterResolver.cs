using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RPA.VsTemplate
{
    /// <summary>Resolves $parameter$ tokens used by Visual Studio templates.</summary>
    public class VsTemplateParameterResolver
    {
        private readonly VsTemplateInstantiationRequest _request;
        private readonly Dictionary<string, string> _guids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public VsTemplateParameterResolver(VsTemplateInstantiationRequest request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
        }

        public string Resolve(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
                return string.Empty;

            string name = parameterName.Trim();

            if (name.StartsWith("guid", StringComparison.OrdinalIgnoreCase))
                return ResolveGuid(name);

            switch (name.ToLowerInvariant())
            {
                case "safeprojectname":
                    return _request.SafeProjectName ?? string.Empty;
                case "projectname":
                    return _request.ProjectName ?? string.Empty;
                case "rootnamespace":
                    return string.IsNullOrWhiteSpace(_request.RootNamespace)
                        ? _request.SafeProjectName ?? string.Empty
                        : _request.RootNamespace;
                case "targetframeworkversion":
                    return _request.TargetFrameworkVersion ?? string.Empty;
                case "registeredorganization":
                    return _request.RegisteredOrganization ?? string.Empty;
                case "username":
                    return _request.UserName ?? string.Empty;
                case "machinename":
                    return _request.MachineName ?? string.Empty;
                case "year":
                    return string.IsNullOrWhiteSpace(_request.Year) ? DateTime.Now.Year.ToString() : _request.Year;
                case "time":
                    return string.IsNullOrWhiteSpace(_request.Time) ? DateTime.Now.ToString("HH:mm:ss") : _request.Time;
                case "safeitemname":
                    return _request.SafeItemName ?? string.Empty;
                case "fileinputname":
                    return _request.FileInputName ?? string.Empty;
                default:
                    return "$" + parameterName + "$";
            }
        }

        /// <summary>Produces a safe project name by removing characters invalid in file names.</summary>
        public static string MakeSafeProjectName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                return "Project";

            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder();
            foreach (char character in projectName.Trim())
            {
                if (!invalid.Contains(character))
                    builder.Append(character);
            }

            string result = builder.ToString().Trim().TrimEnd('.', ' ');
            return string.IsNullOrEmpty(result) ? "Project" : result;
        }

        private string ResolveGuid(string name)
        {
            if (!_guids.TryGetValue(name, out string value))
            {
                value = Guid.NewGuid().ToString("B").ToUpperInvariant();
                _guids[name] = value;
            }

            return value;
        }
    }
}
