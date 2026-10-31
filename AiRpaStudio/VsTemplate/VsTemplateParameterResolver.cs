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
            {
                return string.Empty;
            }

            string name = parameterName.Trim();

            return name.StartsWith("guid", StringComparison.OrdinalIgnoreCase)
                ? ResolveGuid(name)
                : name.ToLowerInvariant() switch
                {
                    "safeprojectname" => _request.SafeProjectName ?? string.Empty,
                    "projectname" => _request.ProjectName ?? string.Empty,
                    "rootnamespace" => string.IsNullOrWhiteSpace(_request.RootNamespace)
                                            ? _request.SafeProjectName ?? string.Empty
                                            : _request.RootNamespace,
                    "targetframeworkversion" => _request.TargetFrameworkVersion ?? string.Empty,
                    "registeredorganization" => _request.RegisteredOrganization ?? string.Empty,
                    "username" => _request.UserName ?? string.Empty,
                    "machinename" => _request.MachineName ?? string.Empty,
                    "year" => string.IsNullOrWhiteSpace(_request.Year) ? DateTime.Now.Year.ToString() : _request.Year,
                    "time" => string.IsNullOrWhiteSpace(_request.Time) ? DateTime.Now.ToString("HH:mm:ss") : _request.Time,
                    "safeitemname" => _request.SafeItemName ?? string.Empty,
                    "fileinputname" => _request.FileInputName ?? string.Empty,
                    _ => "$" + parameterName + "$",
                };
        }

        /// <summary>Produces a safe project name by removing characters invalid in file names.</summary>
        public static string MakeSafeProjectName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                return "Project";
            }

            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder();
            foreach (char character in projectName.Trim())
            {
                if (!invalid.Contains(character))
                {
                    _ = builder.Append(character);
                }
            }

            string result = builder.ToString().Trim().TrimEnd('.', ' ');
            return string.IsNullOrEmpty(result) ? "Project" : result;
        }

        private string ResolveGuid(string name)
        {
            if (!_guids.TryGetValue(name, out string value))
            {
                // The C# compiler validates [assembly: Guid(...)] strictly in the "D" format
                // (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx, no braces); the braced "B"/registry format
                // is rejected with CS0591. Braces are added in the template itself where they are
                // required (e.g. <ProjectGuid>{$guid1$}</ProjectGuid>).
                value = Guid.NewGuid().ToString("D").ToUpperInvariant();
                _guids[name] = value;
            }

            return value;
        }
    }
}
