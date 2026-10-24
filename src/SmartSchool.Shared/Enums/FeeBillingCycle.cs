namespace SmartSchool.Shared.Enums;

/// <summary>Recurrence for configurable fee-plan lines; this is a plan setting, not an auto-charge command.</summary>
public enum FeeBillingCycle
{
    Monthly = 0,
    PerTerm = 1,
    Annually = 2,
    OneTime = 3
}
