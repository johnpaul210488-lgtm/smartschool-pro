using FluentValidation;
using SmartSchool.Core.DTOs;

namespace SmartSchool.API.Validators;

public sealed class BusUpsertRequestValidator : AbstractValidator<BusUpsertRequest>
{
    public BusUpsertRequestValidator()
    {
        RuleFor(x => x.BusNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Route).NotEmpty().MaximumLength(200);
        RuleFor(x => x.VehicleNo).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Capacity).InclusiveBetween(1, 120);
    }
}

public sealed class BusEventRequestValidator : AbstractValidator<BusEventRequest>
{
    public BusEventRequestValidator()
    {
        RuleFor(x => x.StudentId).GreaterThan(0);
        RuleFor(x => x.BusId).GreaterThan(0).When(x => x.BusId is not null);
        RuleFor(x => x.DriverId).GreaterThan(0).When(x => x.DriverId is not null);
        RuleFor(x => x.TripId).GreaterThan(0).When(x => x.TripId is not null);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180)
            .NotEqual(0).When(x => x.Latitude == 0)
            .WithMessage("GPS coordinates (0,0) are not valid - wait for a location fix.");
    }
}

public sealed class TripStartRequestValidator : AbstractValidator<TripStartRequest>
{
    public TripStartRequestValidator()
    {
        RuleFor(x => x.BusId).GreaterThan(0);
        RuleFor(x => x.Type).IsInEnum().NotEqual(default(SmartSchool.Shared.Enums.TripType));
    }
}

public sealed class AssignDriverRequestValidator : AbstractValidator<AssignDriverRequest>
{
    public AssignDriverRequestValidator()
    {
        RuleFor(x => x.DriverId).GreaterThan(0).When(x => x.DriverId is not null);
    }
}
