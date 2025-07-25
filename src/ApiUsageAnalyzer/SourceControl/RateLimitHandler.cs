using System.Globalization;
using System.Net;

namespace ApiUsageAnalyzer.SourceControl;

public sealed class RateLimitHandler : HttpClientHandler
{
    private readonly Lock @lock = new();

    private DateTimeOffset? RateLimitReset
    {
        // Prevent torn reads
        get { lock (@lock) return field; }
        set { lock (@lock) field = value; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
    retry:
        if (RateLimitReset is { } resetTime)
        {
            var remainingTime = resetTime - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);
            if (remainingTime > TimeSpan.Zero)
            {
                Console.WriteLine($"GitHub non-authenticated API rate limit exceeded, waiting until the reset time at {resetTime:t}");
                await Task.Delay(remainingTime, cancellationToken);
                Console.WriteLine("GitHub non-authenticated API rate limit reset time reached, retrying request.");
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var resetHeaderValue = response.Headers.GetValues("X-RateLimit-Reset").Single();
            RateLimitReset = DateTimeOffset.FromUnixTimeSeconds(long.Parse(resetHeaderValue, NumberStyles.None, CultureInfo.InvariantCulture));

            response.Dispose();
            goto retry;
        }

        return response;
    }
}
