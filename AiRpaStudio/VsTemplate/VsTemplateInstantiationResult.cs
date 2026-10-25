using System.Collections.Generic;

namespace RPA.VsTemplate
{
    /// <summary>Result of a template instantiation.</summary>
    public class VsTemplateInstantiationResult
    {
        public bool Success { get; set; }
        public List<string> CreatedFiles { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
