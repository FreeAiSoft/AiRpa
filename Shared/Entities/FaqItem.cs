namespace Shared.Entities
{
    public class FaqItem
    {
        public int Id { get; set; }

        public int Order { get; set; }

        public string Title { get; set; }

        public string ShortDescription { get; set; }

        public string Description { get; set; }

        public string LinkText { get; set; }

        public string Link { get; set; }

        public bool IsDelete { get; set; }

        public string TitleEn { get; set; }

        public string LinkTextEn { get; set; }
    }
}
