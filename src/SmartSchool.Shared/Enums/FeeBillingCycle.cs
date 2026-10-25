namespace SmartSchool.Shared.Enums;

/// <summary>Recurrence for configurable fee-plan lines; this is a plan setting, not an auto-charge command.</summary>
public enum FeeBillingCycle
{
    Monthly = 0,
    PerTerm = 1, // Legacy stored value; use Quarterly in the current UI.
    Annually = 2,
    OneTime = 3,
    Quarterly = 4
}
