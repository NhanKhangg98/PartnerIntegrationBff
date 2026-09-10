using FluentAssertions;
using FluentValidation.TestHelper;
using PartnerIntegration.Core.DTOs;
using Xunit;

namespace PartnerIntegration.UnitTests.Validators;

public class PartnerTransactionValidatorTests
{
    private readonly PartnerTransactionValidator _validator = new();

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

        // Kiểm tra đúng field có lỗi mà không cần hardcode so khớp từng chữ hoa/thường
        result.ShouldHaveValidationErrorFor(x => x.PartnerId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public void Validate_WhenAmountIsZeroOrNegativeOrNull_ShouldHaveValidationError(double? amountDouble)
    {
        // Chuyển đổi an toàn từ double sang decimal?
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
}