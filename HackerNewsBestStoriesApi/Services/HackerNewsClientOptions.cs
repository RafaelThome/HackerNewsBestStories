namespace HackerNewsBestStoriesApi.Services
{
    public class HackerNewsClientOptions
    {
        public int BestIdsCacheSeconds { get; set; } = 1;
        public int ItemCacheSeconds { get; set; } = 600;
        public int MaxThreadserCore { get; set; } = 10;
        public int MaxN { get; set; } = 1000;
        public int RequestSemaphoreCount { get; set; } = 1000;
    }
}
