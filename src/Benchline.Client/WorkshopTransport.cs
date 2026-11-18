using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Benchline.Client;

internal sealed class WorkshopTransport : DelegatingHandler
{
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly ResiliencePipeline<HttpResponseMessage> _reads = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddTimeout(TotalTimeout)
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 2,
            Delay = RetryDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args => IsTransient(args.Outcome),
            OnRetry = args =>
            {
                args.Outcome.Result?.Dispose();
                return default;
            }
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage> { ShouldHandle = args => IsTransient(args.Outcome) })
        .AddTimeout(AttemptTimeout)
        .Build();

    private readonly ResiliencePipeline<HttpResponseMessage> _writes = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddTimeout(AttemptTimeout)
        .Build();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pipeline = request.Method == HttpMethod.Get ? _reads : _writes;
        return pipeline.ExecuteAsync(
            token => new ValueTask<HttpResponseMessage>(base.SendAsync(request, token)), cancellationToken).AsTask();
    }

    private static ValueTask<bool> IsTransient(Outcome<HttpResponseMessage> outcome) =>
        new(outcome.Exception is HttpRequestException or TimeoutRejectedException
            || outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int?)outcome.Result?.StatusCode >= 500);
}
