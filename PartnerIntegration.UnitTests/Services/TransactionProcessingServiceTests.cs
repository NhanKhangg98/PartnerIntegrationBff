using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Interfaces;
using PartnerIntegration.Core.Services;
using Xunit;

namespace PartnerIntegration.UnitTests.Services;

public class TransactionProcessingServiceTests
{
    private readonly Mock<IPartnerVerificationClient> _mockVerifierClient;
    private readonly Mock<IMessagePublisher> _mockPublisher;
    private readonly Mock<ILogger<TransactionProcessingService>> _mockLogger;
    private readonly TransactionProcessingService _service;

    public TransactionProcessingServiceTests()
    {
        _mockVerifierClient = new Mock<IPartnerVerificationClient>();
        _mockPublisher = new Mock<IMessagePublisher>();
        _mockLogger = new Mock<ILogger<TransactionProcessingService>>();

        _service = new TransactionProcessingService(
            _mockVerifierClient.Object,
            _mockPublisher.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task ProcessAsync_WhenPartnerIsValid_ShouldPublishMessageAndReturn202()
    {
        var request = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_VALID",
            TransactionReference: "TX_123",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        _mockVerifierClient
            .Setup(x => x.VerifyPartnerAsync("PARTNER_VALID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.ProcessAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);

        // Đảm bảo message thực sự được đẩy vào queue
        _mockPublisher.Verify(
            x => x.PublishAsync(It.IsAny<string>(), request, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task ProcessAsync_WhenPartnerIsInactiveOrNotFound_ShouldReturn422AndNotPublish()
    {
        var request = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_INVALID",
            TransactionReference: "TX_123",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        _mockVerifierClient
            .Setup(x => x.VerifyPartnerAsync("PARTNER_INVALID", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.ProcessAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422);

        // Tuyệt đối không publish vào queue nếu partner không hợp lệ
        _mockPublisher.Verify(
            x => x.PublishAsync(It.IsAny<string>(), It.IsAny<PartnerTransactionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ProcessAsync_WhenDownstreamVerificationTimesOut_ShouldReturn503AndNotCrash()
    {
        var request = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_TIMEOUT",
            TransactionReference: "TX_123",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        _mockVerifierClient
            .Setup(x => x.VerifyPartnerAsync("PARTNER_TIMEOUT", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Simulated downstream timeout."));

        var result = await _service.ProcessAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(503);

        _mockPublisher.Verify(
            x => x.PublishAsync(It.IsAny<string>(), It.IsAny<PartnerTransactionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }
}