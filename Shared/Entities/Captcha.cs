namespace Shared.Entities
{
    public class Captcha
    {
        public int Id { get; set; }

        public long CreateTimeStamp { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "<Pending>")]
        public string Guid { get; set; }

        public bool IsDownload { get; set; }

        public string Answer { get; set; }
    }
}
