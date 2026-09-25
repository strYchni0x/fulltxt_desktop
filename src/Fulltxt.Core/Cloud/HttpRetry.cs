using System.Net;

namespace Fulltxt.Core.Cloud;

/// <summary>Sendet Anfragen erneut, wenn ein Anbieter mit 429/503 drosselt (wie die RateLimit-Middleware der Android-App).</summary>
public static class HttpRetry
{
    /// <summary>Für Tests überschreibbar, damit Wiederholungen nicht echte Sekunden warten.</summary>
    public static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead,
        int maxAttempts = 4)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            var response = await client.SendAsync(request, completion, ct).ConfigureAwait(false);

            var throttled = response.StatusCode == HttpStatusCode.TooManyRequests
                            || response.StatusCode == HttpStatusCode.ServiceUnavailable;
            if (!throttled || attempt >= maxAttempts) return response;

            var wait = response.Headers.RetryAfter switch
            {
                { Delta: { } delta } => delta,
                { Date: { } date } => date - DateTimeOffset.UtcNow,
                _ => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            };
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
            if (wait > TimeSpan.FromSeconds(60)) wait = TimeSpan.FromSeconds(60);

            response.Dispose();
            await Delay(wait, ct).ConfigureAwait(false);
        }
    }
}
