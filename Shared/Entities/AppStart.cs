using System;
using Shared.Enums;

namespace Shared.Entities
{
    public class AppStart
    {
        public int Id { get; set; }

        public DateTime StartDateTime { get; set; }

        public int UserId { get; set; }

        public StartType StartType { get; set; }

        public string SessionGuid { get; set; }
    }
}
