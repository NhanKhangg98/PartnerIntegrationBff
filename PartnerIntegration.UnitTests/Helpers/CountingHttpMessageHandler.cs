namespace PartnerIntegration.UnitTests.Helpers;

/// <summary>
/// A test-only HttpMessageHandler that delegates to a factory function and counts total invocations.
/// Use this to verify Polly retry behaviour without spinning up a real HTTP server.
/// </summary>
public sealed class CountingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;
    private int _callCount;

    /// <summary>Number of times SendAsync was invoked (i.e., initial attempt + retries).</summary>
    public int CallCount => _callCount;

    public CountingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
    {
        _handlerFunc = handlerFunc;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return await _handlerFunc(request, cancellationToken);
    }
}
