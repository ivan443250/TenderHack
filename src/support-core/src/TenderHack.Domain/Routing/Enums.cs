namespace TenderHack.Domain.Routing;

/// <summary>product-spec.md §15 — do not confuse topic with support line.</summary>
public enum ServiceNeed
{
    Consultation,
    AccountOrStateCheck,
    ComplexProcess,
    TechnicalDiagnosis,
}

public enum SupportLine
{
    L1,
    L2,
}
