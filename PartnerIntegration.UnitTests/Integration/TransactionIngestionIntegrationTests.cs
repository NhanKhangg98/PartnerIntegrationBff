using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Interfaces;
using PartnerIntegration.UnitTests.Helpers;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace PartnerIntegration.UnitTests.Integration;

/// <summary>
/// End-to-end integration tests using <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// The real ASP.NET Core pipeline runs (middleware, routing, FluentValidation, etc.) but
/// external dependencies (RabbitMQ, partner verification HTTP client) are replaced
/// with lightweight test doubles so no infrastructure is required.
/// </summary>
public class TransactionIngestionIntegrationTests : IClassFixture<TransactionIngestionIntegrationTests.AppFactory>
{
    private readonly AppFactory _factory;

    public TransactionIngestionIntegrationTests(AppFactory factory)
    {
        _factory = factory;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    public sealed class AppFactory : WebApplicationFactory<Program>
    {
        /// <summary>Captured messages for assertion in tests.</summary>
        public InMemoryMessagePublisher Publisher { get; } = new();

        /// <summary>Controls what VerifyPartnerAsync returns per test.</summary>
        public Mock<IPartnerVerificationClient> MockVerifier { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                // Inject test-specific config overrides
                var testConfig = new Dictionary<string, string?>
                {
                    // Provide a known API key so integration tests can authenticate
                    ["Security:ApiKeys"] = "integration-test-key"
                };
                config.AddInMemoryCollection(testConfig);
            });

            builder.ConfigureServices(services =>
            {
                // Replace IMessagePublisher — avoids RabbitMQ connection in tests
                // (last registration wins when the same interface is resolved)
                services.AddSingleton<IMessagePublisher>(Publisher);

                // Replace IPartnerVerificationClient — gives tests full control
                services.AddTransient<IPartnerVerificationClient>(_ => MockVerifier.Object);
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient CreateAuthenticatedClient() =>
        _factory.CreateClient();

    private static HttpRequestMessage BuildRequest(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/partner/transactions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Api-Key", "integration-test-key");
        return request;
    }

    private static object ValidPayload(string partnerId = "PARTNER_VALID") => new
    {
        partnerId,
        transactionReference = "TX_E2E_001",
        amount = 500.00m,
        currency = "USD",
        timestamp = DateTime.UtcNow
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostTransaction_WithValidPayloadAndVerifiedPartner_ShouldReturn202AndEnqueueMessage()
    {
        // Arrange
        _factory.MockVerifier
            .Setup(x => x.VerifyPartnerAsync("PARTNER_VALID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.SendAsync(BuildRequest(ValidPayload()));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Publisher.Published.Should().HaveCount(1, "one transaction should have been enqueued");
    }

    [Fact]
    public async Task PostTransaction_WithInvalidPartner_ShouldReturn422AndNotEnqueue()
    {
        // Arrange
        _factory.Publisher.Clear();
        _factory.MockVerifier
            .Setup(x => x.VerifyPartnerAsync("PARTNER_INACTIVE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.SendAsync(BuildRequest(ValidPayload("PARTNER_INACTIVE")));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _factory.Publisher.Published.Should().BeEmpty("an unverified partner must not result in a queued message");
    }

    [Fact]
    public async Task PostTransaction_WithMissingPartnerId_ShouldReturn400()
    {
        // Arrange
        var client = CreateAuthenticatedClient();
        var request = BuildRequest(new
        {
            transactionReference = "TX_NO_PARTNER",
            amount = 100m,
            currency = "USD",
            timestamp = DateTime.UtcNow
        });

        // Act
        using var response = await client.SendAsync(request);

        // Assert — FluentValidation auto-validation returns 400 Bad Request
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTransaction_WithAmountZero_ShouldReturn400()
    {
        // Arrange
        var client = CreateAuthenticatedClient();
        var request = BuildRequest(new
        {
            partnerId = "PARTNER_VALID",
            transactionReference = "TX_ZERO_AMT",
            amount = 0m,
            currency = "USD",
            timestamp = DateTime.UtcNow
        });

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTransaction_WithInvalidCurrency_ShouldReturn400()
    {
        // Arrange
        var client = CreateAuthenticatedClient();
        var request = BuildRequest(new
        {
            partnerId = "PARTNER_VALID",
            transactionReference = "TX_BAD_CURR",
            amount = 100m,
            currency = "XYZ",
            timestamp = DateTime.UtcNow
        });

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTransaction_WithoutApiKey_ShouldReturn401()
    {
        // Arrange — deliberately omit X-Api-Key header
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/partner/transactions")
        {
            Content = JsonContent.Create(ValidPayload())
        };

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTransaction_WhenVerificationServiceThrows_ShouldReturn503AndNotEnqueue()
    {
        // Arrange
        _factory.Publisher.Clear();
        _factory.MockVerifier
            .Setup(x => x.VerifyPartnerAsync("PARTNER_TIMEOUT_E2E", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Simulated downstream timeout"));

        var client = CreateAuthenticatedClient();

        // Act
        using var response = await client.SendAsync(BuildRequest(ValidPayload("PARTNER_TIMEOUT_E2E")));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _factory.Publisher.Published.Should().BeEmpty("a timed-out verification must not result in a queued message");
    }
}
