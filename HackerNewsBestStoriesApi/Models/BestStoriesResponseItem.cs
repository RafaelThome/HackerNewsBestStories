namespace HackerNewsBestStoriesApi.Models
{
    public class BestStoriesResponseItem
    {
        public string Title { get; set; } = "";
        public string? Uri { get; set; }
        public string PostedBy { get; set; } = "";
        public string Time { get; set; } = ""; // ISO-8601 with +00:00
        public int Score { get; set; }
        public int CommentCount { get; set; }
    }
}
