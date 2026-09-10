using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PartnerIntegration.Core.Interfaces;
using PartnerIntegration.Infrastructure.Clients;
using PartnerIntegration.UnitTests.Helpers;
using Polly;
using System.Net;
using Xunit;

namespace PartnerIntegration.UnitTests.Integration;

/// <summary>
/// Tests verifying that the Polly resilience pipeline (retry + timeout) on
/// <see cref="PartnerVerificationClient"/> works correctly.
///
/// These tests build a real <see cref="IServiceCollection"/> with the ACTUAL
/// Polly pipeline, but swap out the primary <see cref="HttpMessageHandler"/>
/// with a <see cref="CountingHttpMessageHandler"/> so we can control responses
/// and count how many times the handler is invoked (initial attempt + retries).
/// </summary>
public class RetryResilienceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ServiceProvider that wires up a real Polly retry pipeline
    /// on top of the supplied primary handler.
    /// Retry delay is set to zero so tests run fast.
    /// </summary>
    private static ServiceProvider BuildProviderWithHandler(CountingHttpMessageHandler primaryHandler)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>(client =>
        {
            client.BaseAddress = new Uri("http://test-host");
        })
        .ConfigurePrimaryHttpMessageHandler(() => primaryHandler)
        .AddResilienceHandler("test-retry-pipeline", pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                // Zero delay keeps tests fast while still exercising retry logic
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
                UseJitter = false,
                ShouldHandle = args =>
                {
                    var isServerError = args.Outcome.Result is not null &&
                                       (int)args.Outcome.Result.StatusCode >= 500;
                    var isTransient = args.Outcome.Exception is
                        HttpRequestException or
                        Polly.Timeout.TimeoutRejectedException;
                    return ValueTask.FromResult(isServerError || isTransient);
                }
            });

            // Per-attempt timeout kept short to keep tests fast
            pipeline.AddTimeout(TimeSpan.FromSeconds(2));
        });

        return services.BuildServiceProvider();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyPartner_WhenFirst2AttemptsReturn503_ShouldRetryAndSucceedOn3rdAttempt()
    {
        // Arrange: fail twice with 503, then succeed
        var callCount = 0;
        var handler = new CountingHttpMessageHandler((_, _) =>
        {
            callCount++;
            var statusCode = callCount <= 2
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        });

        await using var provider = BuildProviderWithHandler(handler);
        var client = provider.GetRequiredService<IPartnerVerificationClient>();

        // Act
        var result = await client.VerifyPartnerAsync("PARTNER_TEST");

        // Assert
        result.Should().BeTrue("the 3rd attempt should have succeeded");
        handler.CallCount.Should().Be(3, "1 initial attempt + 2 retries");
    }

    [Fact]
    public async Task VerifyPartner_WhenAllAttemptsReturn503_ShouldExhaustRetriesAndThrow()
    {
        // Arrange: always fail with 503
        var handler = new CountingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        await using var provider = BuildProviderWithHandler(handler);
        var client = provider.GetRequiredService<IPartnerVerificationClient>();

        // Act
        Func<Task> act = () => client.VerifyPartnerAsync("PARTNER_ALWAYS_DOWN");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>(
            "after exhausting all retries, EnsureSuccessStatusCode should throw");

        // 1 initial + 3 retries = 4 total handler invocations
        handler.CallCount.Should().Be(4, "MaxRetryAttempts = 3 means 1 initial + 3 retries");
    }

    [Fact]
    public async Task VerifyPartner_WhenPartnerNotFound_ShouldReturnFalseWithoutRetrying()
    {
        // Arrange: 404 is NOT in the retry ShouldHandle — should not retry
        var handler = new CountingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        await using var provider = BuildProviderWithHandler(handler);
        var client = provider.GetRequiredService<IPartnerVerificationClient>();

        // Act
        var result = await client.VerifyPartnerAsync("UNKNOWN_PARTNER");

        // Assert
        result.Should().BeFalse("404 means partner not found");
        handler.CallCount.Should().Be(1, "404 is not a transient error — no retry should occur");
    }

    [Fact]
    public async Task VerifyPartner_WhenFirstAttemptSucceeds_ShouldCallHandlerExactlyOnce()
    {
        // Arrange: immediate success
        var handler = new CountingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await using var provider = BuildProviderWithHandler(handler);
        var client = provider.GetRequiredService<IPartnerVerificationClient>();

        // Act
        var result = await client.VerifyPartnerAsync("PARTNER_HEALTHY");

        // Assert
        result.Should().BeTrue();
        handler.CallCount.Should().Be(1, "no failures means no retries");
    }
}
