using FluentValidation;
using PartnerIntegration.Core.DTOs;

namespace PartnerIntegration.Core.Validators;

public class PartnerTransactionValidator : AbstractValidator<PartnerTransactionRequestDto>
{
    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "VND", "USD", "EUR", "JPY", "GBP", "SGD", "AUD", "CAD"
    };

    public PartnerTransactionValidator()
    {
        RuleFor(x => x.PartnerId)
            .NotEmpty().WithMessage("PartnerId is required.");

        RuleFor(x => x.TransactionReference)
            .NotEmpty().WithMessage("transactionReference is required.");

        RuleFor(x => x.Amount)
            .NotNull().WithMessage("amount is required.")
            .GreaterThan(0).WithMessage("amount must be greater than 0.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("currency is required.")
            .Must(c => !string.IsNullOrWhiteSpace(c) && AllowedCurrencies.Contains(c))
            .WithMessage("currency must be a valid ISO currency (e.g., VND, USD, EUR).");

        RuleFor(x => x.Timestamp)
            .NotNull().WithMessage("timestamp is required.")
            .Must(t => t.HasValue && t.Value >= DateTime.UtcNow.AddDays(-1))
            .WithMessage("timestamp must not be older than 24 hours.")
            .Must(t => t.HasValue && t.Value <= DateTime.UtcNow.AddMinutes(5))
            .WithMessage("timestamp must not be more than 5 minutes in the future.");
    }
}
