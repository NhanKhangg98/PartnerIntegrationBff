using FluentAssertions;
using FluentValidation.TestHelper;
using PartnerIntegration.Core.DTOs;
using PartnerIntegration.Core.Validators;
using Xunit;

namespace PartnerIntegration.UnitTests.Validators;

public class PartnerTransactionValidatorTests
{
    private readonly PartnerTransactionValidator _validator = new();

    // ── Happy Path ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_WhenAllFieldsAreValid_ShouldNotHaveAnyErrors()
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: 150.50m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        var result = _validator.TestValidate(model);

        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── PartnerId ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenPartnerIdIsEmpty_ShouldHaveValidationError(string? invalidPartnerId)
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: invalidPartnerId,
            TransactionReference: "TX_1001",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.PartnerId);
    }

    // ── TransactionReference ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenTransactionReferenceIsEmpty_ShouldHaveValidationError(string? invalidRef)
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: invalidRef,
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.TransactionReference);
    }

    // ── Amount ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void Validate_WhenAmountIsZeroOrNegativeOrNull_ShouldHaveValidationError(double? amountDouble)
    {
        decimal? invalidAmount = amountDouble.HasValue ? Convert.ToDecimal(amountDouble.Value) : null;

        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: invalidAmount,
            Currency: "USD",
            Timestamp: DateTime.UtcNow
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    // ── Currency ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("INVALID_CURR")]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WhenCurrencyIsInvalid_ShouldHaveValidationError(string? invalidCurrency)
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: 100m,
            Currency: invalidCurrency,
            Timestamp: DateTime.UtcNow
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Currency);
    }

    // ── Timestamp ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_WhenTimestampIsNull_ShouldHaveValidationError()
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: 100m,
            Currency: "USD",
            Timestamp: null
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Timestamp);
    }

    [Fact]
    public void Validate_WhenTimestampIsOlderThan24Hours_ShouldHaveValidationError()
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow.AddDays(-2) // 2 days ago — clearly stale
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Timestamp);
    }

    [Fact]
    public void Validate_WhenTimestampIsMoreThan5MinutesInFuture_ShouldHaveValidationError()
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow.AddMinutes(10) // 10 min in future — clock skew too large
        );

        var result = _validator.TestValidate(model);

        result.ShouldHaveValidationErrorFor(x => x.Timestamp);
    }

    [Fact]
    public void Validate_WhenTimestampIsRecentAndValid_ShouldNotHaveTimestampError()
    {
        var model = new PartnerTransactionRequestDto(
            PartnerId: "PARTNER_01",
            TransactionReference: "TX_1001",
            Amount: 100m,
            Currency: "USD",
            Timestamp: DateTime.UtcNow.AddHours(-1) // 1 hour ago — valid
        );

        var result = _validator.TestValidate(model);

        result.ShouldNotHaveValidationErrorFor(x => x.Timestamp);
    }
}