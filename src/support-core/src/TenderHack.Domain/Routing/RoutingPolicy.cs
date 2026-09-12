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
///
/// The risk-flag vocabulary below mirrors what `src/knowledge`'s answerability service actually
/// emits (`answerability/service.py`), not just the single `TECHNICAL_DIAGNOSIS_REQUIRED` placeholder
/// this policy originally shipped with — without this, any real flag (`HUMAN_SUPPORT_REQUIRED`,
/// `ROLE_AMBIGUITY`, ...) fell through to a generic `RISK_FLAGGED_ANSWER` reason code that told
/// nobody what was actually uncertain (architecture.md §10, tracked in
/// `docs/plans/active/2026-09-support-core-completion.md` item B2).
/// </summary>
public static class RoutingPolicy
{
    /// <summary>Reserved risk-flag convention for engineering diagnosis — not currently emitted by `knowledge`, kept for forward compatibility.</summary>
    public const string TechnicalDiagnosisFlag = "TECHNICAL_DIAGNOSIS_REQUIRED";

    public const string HumanSupportRequiredFlag = "HUMAN_SUPPORT_REQUIRED";
    public const string RoleAmbiguityFlag = "ROLE_AMBIGUITY";
    public const string ConflictingEvidenceFlag = "CONFLICTING_EVIDENCE";
    public const string WrongRoleFlag = "WRONG_ROLE";
    public const string WrongStatusFlag = "WRONG_STATUS";
    public const string WrongProviderFlag = "WRONG_PROVIDER";
    public const string ForbiddenGeneralizationFlag = "FORBIDDEN_GENERALIZATION";
    public const string HighRiskMissingConditionFlag = "HIGH_RISK_MISSING_CONDITION";
    public const string NegatedQueryFlag = "NEGATED_QUERY";
    public const string LowSpecificityFlag = "LOW_SPECIFICITY";
    public const string PostInstructionFailureFlag = "POST_INSTRUCTION_FAILURE";
    public const string MissingSourceFlag = "MISSING_SOURCE";
    public const string InvalidEvidenceReferenceFlag = "INVALID_EVIDENCE_REFERENCE";

    /// <summary>
    /// Handled entirely upstream in `TurnOrchestrator` (escalated to `TECHNICAL_ERROR` before this
    /// policy ever runs — product-spec.md §9 "infrastructure failure ≠ в базе нет ответа") — listed
    /// here only so it is never misclassified as an unknown flag if it somehow still reaches this policy.
    /// </summary>
    public const string KnowledgeUnavailableFlag = "KNOWLEDGE_UNAVAILABLE";

    private static readonly IReadOnlyDictionary<string, string> KnownRiskFlagReasonCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [HumanSupportRequiredFlag] = "HUMAN_SUPPORT_REQUIRED",
        [RoleAmbiguityFlag] = "ROLE_AMBIGUITY",
        [ConflictingEvidenceFlag] = "CONFLICTING_EVIDENCE",
        [WrongRoleFlag] = "WRONG_ROLE",
        [WrongStatusFlag] = "WRONG_STATUS",
        [WrongProviderFlag] = "WRONG_PROVIDER",
        [ForbiddenGeneralizationFlag] = "FORBIDDEN_GENERALIZATION",
        [HighRiskMissingConditionFlag] = "HIGH_RISK_MISSING_CONDITION",
        [NegatedQueryFlag] = "NEGATED_QUERY",
        [LowSpecificityFlag] = "LOW_SPECIFICITY",
        [PostInstructionFailureFlag] = "POST_INSTRUCTION_FAILURE",
        [MissingSourceFlag] = "MISSING_SOURCE",
        [InvalidEvidenceReferenceFlag] = "INVALID_EVIDENCE_REFERENCE",
    };

    public static RoutingDecision Evaluate(bool evidenceInsufficient, bool conditionDependent, IReadOnlyList<string> riskFlags, bool explicitHumanRequest = false)
    {
        if (riskFlags.Any(f => string.Equals(f, TechnicalDiagnosisFlag, StringComparison.OrdinalIgnoreCase)))
        {
            return new RoutingDecision(ServiceNeed.TechnicalDiagnosis, SupportLine.L2, "l2-technical", EngineeringReviewSuggested: true, ["TECHNICAL_DIAGNOSIS_FLAG"]);
        }

        if (explicitHumanRequest)
        {
            return new RoutingDecision(ServiceNeed.Consultation, SupportLine.L1, "l1-general", EngineeringReviewSuggested: false, ["EXPLICIT_HUMAN_REQUEST"]);
        }

        var flagReasonCodes = ClassifyRiskFlags(riskFlags);

        if (evidenceInsufficient)
        {
            // Low confidence must default to a conservative manual line with a reason code, never a
            // confident-sounding fabricated diagnosis (product-spec.md §15).
            var reasonCodes = flagReasonCodes.Count > 0 ? flagReasonCodes : ["INSUFFICIENT_EVIDENCE"];
            return new RoutingDecision(ServiceNeed.ComplexProcess, SupportLine.L2, "l2-general", EngineeringReviewSuggested: false, reasonCodes);
        }

        if (conditionDependent)
        {
            var reasonCodes = flagReasonCodes.Count > 0 ? flagReasonCodes : ["CONDITION_DEPENDENT"];
            return new RoutingDecision(ServiceNeed.AccountOrStateCheck, SupportLine.L1, "l1-general", EngineeringReviewSuggested: false, reasonCodes);
        }

        // Sufficient, verified evidence but one or more risk flags survived — the caller (product-spec.md
        // §10 ANSWER_AND_HANDOFF) still publishes the answer; this only supplies the handoff's reason codes.
        return new RoutingDecision(ServiceNeed.Consultation, SupportLine.L1, "l1-general", EngineeringReviewSuggested: false, flagReasonCodes);
    }

    /// <summary>
    /// An unrecognized flag becomes `UNKNOWN_RISK_FLAG:<name>` rather than being silently dropped — a
    /// new `knowledge`-side flag must never vanish into an over-confident plain `ANSWER`/generic
    /// reason; the fixture set in `evals/decisions` should catch a new flag appearing and give it a
    /// real classification here.
    /// </summary>
    private static List<string> ClassifyRiskFlags(IReadOnlyList<string> riskFlags)
    {
        var reasonCodes = new List<string>();
        foreach (var flag in riskFlags)
        {
            if (string.Equals(flag, KnowledgeUnavailableFlag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            reasonCodes.Add(KnownRiskFlagReasonCodes.TryGetValue(flag, out var reasonCode) ? reasonCode : $"UNKNOWN_RISK_FLAG:{flag}");
        }

        return reasonCodes;
    }
}
