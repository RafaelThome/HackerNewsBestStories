using HackerNewsBestStoriesApi.Models;

namespace HackerNewsBestStoriesApi.Services
{
    public interface IHackerNewsClient
    {
        Task<List<BestStoriesResponseItem>> GetFirstNBestStoriesAsync(int n);
    }
}
