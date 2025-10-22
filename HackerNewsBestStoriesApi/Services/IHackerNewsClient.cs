using HackerNewsBestStoriesApi.Models;

namespace HackerNewsBestStoriesApi.Services
{
    public interface IHackerNewsClient
    {
        Task<List<BestStoryResponse>> GetFirstNBestStoriesAsync(int n);
    }
}
