using HackerNewsBestStoriesApi.Controllers;

namespace HackerNewsBestStoriesApi.Extensions
{
    public static class WebApplicationExtensions
    {
        public static WebApplication Setup(this WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v0/swagger.json", "Hacker News Best Stories API v0");
                    c.RoutePrefix = "";
                });
            }
            else
                app.UseHttpsRedirection();

            app.UseRateLimiter();

            BestStories.AddRoutes(app);
            
            return app;
        }
    }
}
