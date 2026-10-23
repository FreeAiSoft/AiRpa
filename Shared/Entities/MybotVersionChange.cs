using System.Collections.Generic;

namespace Shared.Entities
{
    public class MybotVersionChange
    {
        public int Id { get; set; }

        public string App { get; set; }

        public string Version { get; set; }

        public List<string> Changes { get; set; }

        public List<string> ChangesEn { get; set; }
    }
}
