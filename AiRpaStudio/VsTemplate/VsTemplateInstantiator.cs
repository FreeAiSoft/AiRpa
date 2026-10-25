using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RPA.VsTemplate
{
    /// <summary>Instantiates a parsed template into an output directory.</summary>
    public static class VsTemplateInstantiator
    {
        public static VsTemplateInstantiationResult Instantiate(VsTemplate template, VsTemplateInstantiationRequest request)
        {
            var result = new VsTemplateInstantiationResult();

            if (template == null)
            {
                result.Success = false;
                result.Errors.Add("Template is null.");
                return result;
            }

            if (request == null)
            {
                result.Success = false;
                result.Errors.Add("Instantiation request is null.");
                return result;
            }

            if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                result.Success = false;
                result.Errors.Add("Output directory is required.");
                return result;
            }

            var resolver = new VsTemplateParameterResolver(request);

            try
            {
                Directory.CreateDirectory(request.OutputDirectory);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add("Failed to create output directory: " + ex.Message);
                return result;
            }

            foreach (VsTemplateFile file in template.Files)
            {
                try
                {
                    string targetRelative = ResolveTargetPath(file, resolver);
                    string fullTarget = Path.Combine(request.OutputDirectory, targetRelative);

                    string fullSource = Path.Combine(template.TemplateDirectory ?? string.Empty, file.SourceFile);
                    if (!File.Exists(fullSource))
                    {
                        result.Errors.Add("Source file not found: " + fullSource);
                        continue;
                    }

                    string targetDirectory = Path.GetDirectoryName(fullTarget);
                    if (!string.IsNullOrEmpty(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    if (file.ReplaceParameters)
                    {
                        string content = File.ReadAllText(fullSource);
                        string processed = TemplateTextProcessor.Process(content, resolver.Resolve);
                        File.WriteAllText(fullTarget, processed, new UTF8Encoding(true));
                    }
                    else
                    {
                        File.Copy(fullSource, fullTarget, true);
                    }

                    result.CreatedFiles.Add(fullTarget);
                }
                catch (Exception ex)
                {
                    result.Errors.Add("Failed to instantiate '" + file.SourceFile + "': " + ex.Message);
                }
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        private static string ResolveTargetPath(VsTemplateFile file, VsTemplateParameterResolver resolver)
        {
            string fileName;

            if (file.IsProjectFile)
            {
                // Visual Studio names the project file after the project (e.g. MyProject.csproj).
                string extension = ".csproj";
                if (!string.IsNullOrWhiteSpace(file.TargetFileName))
                    extension = Path.GetExtension(file.TargetFileName);

                if (string.IsNullOrEmpty(extension))
                    extension = ".csproj";

                fileName = resolver.Resolve("safeprojectname") + extension;
            }
            else
            {
                fileName = string.IsNullOrWhiteSpace(file.TargetFileName)
                    ? Path.GetFileName(file.SourceFile)
                    : file.TargetFileName;

                fileName = TemplateTextProcessor.ReplaceParameters(fileName, resolver.Resolve);
            }

            string folder = file.TargetFolder;
            if (!string.IsNullOrWhiteSpace(folder))
                folder = TemplateTextProcessor.ReplaceParameters(folder, resolver.Resolve);

            return string.IsNullOrWhiteSpace(folder) ? fileName : Path.Combine(folder, fileName);
        }
    }
}
