using System;

namespace Shared.Entities
{
    public class EmailMessage
    {
        public int Id { get; set; }
        public DateTime SendDateTime { get; set; }
        public string Email { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }
}
