namespace RPA.AiAgent.Models
{
    /// <summary>
    /// Represents a part of a message content (either text or code block)
    /// </summary>
    public class MessageContentPart
    {
        /// <summary>
        /// Type of content part
        /// </summary>
        public ContentPartType Type { get; set; }

        /// <summary>
        /// Content text
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Language for code blocks (e.g., "json")
        /// </summary>
        public string Language { get; set; }
    }

    /// <summary>
    /// Type of message content part
    /// </summary>
    public enum ContentPartType
    {
        /// <summary>
        /// Regular text content
        /// </summary>
        Text,

        /// <summary>
        /// Code block (e.g., ```json ... ```)
        /// </summary>
        CodeBlock
    }
}
