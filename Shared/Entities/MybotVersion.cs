namespace Shared.Entities
{
    public class MybotVersion
    {
        public int Major { get; set; }

        public int Minor { get; set; }

        public int Fix { get; set; }

        public int Build { get; set; }

        public MybotVersion(int major, int minor, int fix, int build)
        {
            Major = major;
            Minor = minor;
            Fix = fix;
            Build = build;
        }

        public MybotVersion() { }
    }
}
