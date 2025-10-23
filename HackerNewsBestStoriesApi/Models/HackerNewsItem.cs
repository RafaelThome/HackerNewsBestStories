using System.Text.Json.Serialization;

namespace HackerNewsBestStoriesApi.Models
{
    // partial mapping of HackerNews item
    public class HackerNewsItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("by")]
        public string By { get; set; } = "";

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("time")]
        public long Time { get; set; } // unix timestamp

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("kids")]
        public int[]? Kids { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}
