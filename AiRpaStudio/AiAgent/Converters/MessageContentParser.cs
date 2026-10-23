using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Parses message content to extract text and code blocks
    /// </summary>
    public static class MessageContentParser
    {
        // Regex pattern to match markdown code blocks: ```language\n...content...\n```
        private static readonly Regex CodeBlockPattern = new Regex(
            @"```(\w+)?\s*\n(.*?)\n```",
            RegexOptions.Singleline | RegexOptions.Compiled
        );

        /// <summary>
        /// Static constructor to warm up the Regex compilation
        /// This prevents the JIT compilation freeze on first use
        /// </summary>
        static MessageContentParser()
        {
            // Прогреваем Regex компиляцию с тестовым паттерном
            // Это заставит .NET скомпилировать Regex заранее
            string warmupText = "```json\n{\"test\": \"value\"}\n```";
            _ = CodeBlockPattern.Matches(warmupText);
        }

        /// <summary>
        /// Parses message content into a list of content parts (text and code blocks)
        /// </summary>
        /// <param name="content">The message content to parse</param>
        /// <returns>List of content parts</returns>
        public static List<MessageContentPart> Parse(string content)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<MessageContentPart> parts = new List<MessageContentPart>();

            if (string.IsNullOrEmpty(content))
            {
                return parts;
            }

            Stopwatch regexStopwatch = Stopwatch.StartNew();
            int currentIndex = 0;
            MatchCollection matches = CodeBlockPattern.Matches(content);
            regexStopwatch.Stop();
            foreach (Match match in matches)
            {
                // Add text before the code block
                if (match.Index > currentIndex)
                {
                    string textContent = content.Substring(currentIndex, match.Index - currentIndex).Trim();
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        parts.Add(new MessageContentPart
                        {
                            Type = ContentPartType.Text,
                            Content = textContent
                        });
                    }
                }

                // Add the code block
                string language = match.Groups[1].Success ? match.Groups[1].Value : "text";
                string codeContent = match.Groups[2].Value;

                parts.Add(new MessageContentPart
                {
                    Type = ContentPartType.CodeBlock,
                    Content = codeContent,
                    Language = language
                });

                currentIndex = match.Index + match.Length;
            }

            // Add any remaining text after the last code block
            if (currentIndex < content.Length)
            {
                string textContent = content.Substring(currentIndex).Trim();
                if (!string.IsNullOrEmpty(textContent))
                {
                    parts.Add(new MessageContentPart
                    {
                        Type = ContentPartType.Text,
                        Content = textContent
                    });
                }
            }

            // If no code blocks were found, return the entire content as text
            if (parts.Count == 0)
            {
                parts.Add(new MessageContentPart
                {
                    Type = ContentPartType.Text,
                    Content = content
                });
            }

            sw.Stop();

            return parts;
        }
    }
}
