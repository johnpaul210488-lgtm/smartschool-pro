using FluentValidation;
using SmartSchool.Core.DTOs;
using SmartSchool.Shared.Enums;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Validators;

public class FeeCreateRequestValidator : AbstractValidator<FeeCreateRequest>
{
    protected const string MonthPattern = @"^\d{4}-(0[1-9]|1[0-2])$";

    public FeeCreateRequestValidator()
    {
        RuleFor(x => x.StudentId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).LessThanOrEqualTo(10_000_000);
        RuleFor(x => x.Month).NotEmpty().Matches(MonthPattern)
            .WithMessage("Month must be in yyyy-MM format.");
        RuleFor(x => x.DueDate).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public sealed class FeeUpdateRequestValidator : AbstractValidator<FeeUpdateRequest>
{
    public FeeUpdateRequestValidator()
    {
        Include(new FeeCreateRequestValidator());
        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<FeeStatus>(s, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("Status must be one of: Pending, Paid, Overdue, Partial.");
    }
}

public sealed class PayFeeRequestValidator : AbstractValidator<PayFeeRequest>
{
    public PayFeeRequestValidator()
    {
        RuleFor(x => x.PaidDate)
            .Must(d => d is null || d <= PkTime.TodayPk)
            .WithMessage("Paid date cannot be in the future.");
        RuleFor(x => x.Amount).Must(amount => amount is null || amount > 0m)
            .WithMessage("A partial payment amount must be greater than zero.");
        RuleFor(x => x.Method).MaximumLength(30);
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}
