using System;
using System.IO;
using System.Reflection;

namespace RPA.AiAgent.Prompts
{
    public static class ActivityPrompts
    {
        public static string GetSystemPrompt()
        {
            try
            {
                // Get the directory where the assembly is located
                string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // Construct the path to the prompt file
                string promptFilePath = Path.Combine(assemblyDirectory, "AiAgent", "Prompts", "ActivitySystemPrompt.txt");

                // Read and return the file content
                if (File.Exists(promptFilePath))
                {
                    return File.ReadAllText(promptFilePath);
                }
                else
                {
                    // Fallback error message if file is not found
                    return "Error: ActivitySystemPrompt.txt file not found at: " + promptFilePath;
                }
            }
            catch (Exception ex)
            {
                return $"Error loading system prompt: {ex.Message}";
            }
        }
    }
}
