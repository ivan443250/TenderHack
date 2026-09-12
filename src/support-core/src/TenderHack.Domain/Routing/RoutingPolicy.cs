namespace TenderHack.Domain.Routing;

public sealed record RoutingDecision(
    ServiceNeed ServiceNeed,
    SupportLine RecommendedLine,
    string DispatchQueue,
    bool EngineeringReviewSuggested,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Decides where a case that needs support goes (product-spec.md §15). Takes already-translated
/// booleans/flags, not raw `knowledge` response types — `Application` does that translation so this
/// stays a pure Domain policy with no notion of the knowledge-v0 contract shapes.
/// </summary>
public static class RoutingPolicy
{
    /// <summary>Risk-flag convention `knowledge` is expected to emit when a case needs engineering diagnosis.</summary>
    public const string TechnicalDiagnosisFlag = "TECHNICAL_DIAGNOSIS_REQUIRED";

    public static RoutingDecision Evaluate(bool evidenceInsufficient, bool conditionDependent, IReadOnlyList<string> riskFlags, bool explicitHumanRequest = false)
    {
        var reasonCodes = new List<string>();
        var engineeringReview = riskFlags.Any(f => string.Equals(f, TechnicalDiagnosisFlag, StringComparison.OrdinalIgnoreCase));

        if (engineeringReview)
        {
            reasonCodes.Add("TECHNICAL_DIAGNOSIS_FLAG");
            return new RoutingDecision(ServiceNeed.TechnicalDiagnosis, SupportLine.L2, "l2-technical", EngineeringReviewSuggested: true, reasonCodes);
        }

        if (explicitHumanRequest)
        {
            reasonCodes.Add("EXPLICIT_HUMAN_REQUEST");
            return new RoutingDecision(ServiceNeed.Consultation, SupportLine.L1, "l1-general", EngineeringReviewSuggested: false, reasonCodes);
        }

        if (evidenceInsufficient)
        {
            // Low confidence must default to a conservative manual line with a reason code, never a
            // confident-sounding fabricated diagnosis (product-spec.md §15).
            reasonCodes.Add("INSUFFICIENT_EVIDENCE");
            return new RoutingDecision(ServiceNeed.ComplexProcess, SupportLine.L2, "l2-general", EngineeringReviewSuggested: false, reasonCodes);
        }

        if (conditionDependent)
        {
            reasonCodes.Add("CONDITION_DEPENDENT");
            return new RoutingDecision(ServiceNeed.AccountOrStateCheck, SupportLine.L1, "l1-general", EngineeringReviewSuggested: false, reasonCodes);
        }

        if (riskFlags.Count > 0)
        {
            reasonCodes.Add("RISK_FLAGGED_ANSWER");
        }

        return new RoutingDecision(ServiceNeed.Consultation, SupportLine.L1, "l1-general", EngineeringReviewSuggested: false, reasonCodes);
    }
}
