using FluentValidation;
using SmartSchool.Core.DTOs;
using SmartSchool.Shared.Helpers;

namespace SmartSchool.API.Validators;

public class StudentCreateRequestValidator : AbstractValidator<StudentCreateRequest>
{
    public StudentCreateRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.DateOfBirth).NotEmpty()
            .Must(dob => dob <= PkTime.TodayPk && dob > PkTime.TodayPk.AddYears(-25))
            .WithMessage("Date of birth must be in the past and within the last 25 years.");
        RuleFor(x => x.ClassId).GreaterThan(0);
        RuleFor(x => x.RollNo).NotEmpty().MaximumLength(20);
        RuleFor(x => x.PhotoUrl).MaximumLength(500);
        RuleFor(x => x.FatherName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FatherPhone).NotEmpty().MaximumLength(20)
            .Must(PhoneNormalizer.IsValid).WithMessage("Father's phone is not a valid phone number.");
        RuleFor(x => x.MotherPhone).MaximumLength(20)
            .Must(p => p == null || PhoneNormalizer.IsValid(p))
            .WithMessage("Mother's phone is not a valid phone number.");
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.BusId).GreaterThan(0).When(x => x.BusId is not null);
    }
}

public sealed class StudentUpdateRequestValidator : AbstractValidator<StudentUpdateRequest>
{
    public StudentUpdateRequestValidator()
    {
        Include(new StudentCreateRequestValidator());
    }
}

public sealed class ClassUpsertRequestValidator : AbstractValidator<ClassUpsertRequest>
{
    public ClassUpsertRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Section).NotEmpty().MaximumLength(10);
        RuleFor(x => x.TeacherId).GreaterThan(0).When(x => x.TeacherId is not null);
    }
}
