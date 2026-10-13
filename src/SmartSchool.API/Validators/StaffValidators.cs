using FluentValidation;
using SmartSchool.Core.DTOs;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Validators;

public class TeacherCreateRequestValidator : AbstractValidator<TeacherCreateRequest>
{
    public TeacherCreateRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20)
            .Must(PhoneNormalizer.IsValid).WithMessage("Phone is not a valid phone number.");
        RuleFor(x => x.Subject).MaximumLength(100);
        RuleFor(x => x.Username).Length(3, 50).When(x => !string.IsNullOrWhiteSpace(x.Username));
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6)
            .When(x => !string.IsNullOrWhiteSpace(x.Username))
            .WithMessage("Password is required when a Username is supplied.");
    }
}

public sealed class TeacherUpdateRequestValidator : AbstractValidator<TeacherUpdateRequest>
{
    public TeacherUpdateRequestValidator()
    {
        Include(new TeacherCreateRequestValidator());
    }
}

public sealed class DriverCreateRequestValidator : AbstractValidator<DriverCreateRequest>
{
    public DriverCreateRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20)
            .Must(PhoneNormalizer.IsValid).WithMessage("Phone is not a valid phone number.");
        RuleFor(x => x.LicenseNo).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Username).NotEmpty().Length(3, 50);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
    }
}

public sealed class DriverUpdateRequestValidator : AbstractValidator<DriverUpdateRequest>
{
    public DriverUpdateRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20)
            .Must(PhoneNormalizer.IsValid).WithMessage("Phone is not a valid phone number.");
        RuleFor(x => x.LicenseNo).NotEmpty().MaximumLength(50);
    }
}
