using TenderHack.Domain.Routing;
using Xunit;

namespace TenderHack.Domain.Tests;

public sealed class RoutingPolicyTests
{
    [Fact]
    public void TechnicalDiagnosisFlagTakesPriorityOverEverythingElse()
    {
        var decision = RoutingPolicy.Evaluate(
            evidenceInsufficient: true, conditionDependent: true,
            riskFlags: [RoutingPolicy.TechnicalDiagnosisFlag], explicitHumanRequest: true);

        Assert.Equal(ServiceNeed.TechnicalDiagnosis, decision.ServiceNeed);
        Assert.Equal(SupportLine.L2, decision.RecommendedLine);
        Assert.True(decision.EngineeringReviewSuggested);
        Assert.Contains("TECHNICAL_DIAGNOSIS_FLAG", decision.ReasonCodes);
    }

    [Fact]
    public void ExplicitHumanRequestRoutesToL1WithReasonCode()
    {
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: false, riskFlags: [], explicitHumanRequest: true);

        Assert.Equal(SupportLine.L1, decision.RecommendedLine);
        Assert.Contains("EXPLICIT_HUMAN_REQUEST", decision.ReasonCodes);
    }

    [Fact]
    public void InsufficientEvidenceIsConservativeL2NeverAConfidentDiagnosis()
    {
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: true, conditionDependent: false, riskFlags: []);

        Assert.Equal(ServiceNeed.ComplexProcess, decision.ServiceNeed);
        Assert.Equal(SupportLine.L2, decision.RecommendedLine);
        Assert.Contains("INSUFFICIENT_EVIDENCE", decision.ReasonCodes);
    }

    [Fact]
    public void ConditionDependentRoutesToL1AccountCheck()
    {
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: true, riskFlags: []);

        Assert.Equal(ServiceNeed.AccountOrStateCheck, decision.ServiceNeed);
        Assert.Equal(SupportLine.L1, decision.RecommendedLine);
    }

    [Fact]
    public void SufficientEvidenceNoFlagsIsPlainConsultation()
    {
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: false, riskFlags: []);

        Assert.Equal(ServiceNeed.Consultation, decision.ServiceNeed);
        Assert.Empty(decision.ReasonCodes);
    }

    [Theory]
    [InlineData(RoutingPolicy.HumanSupportRequiredFlag, "HUMAN_SUPPORT_REQUIRED")]
    [InlineData(RoutingPolicy.RoleAmbiguityFlag, "ROLE_AMBIGUITY")]
    [InlineData(RoutingPolicy.ConflictingEvidenceFlag, "CONFLICTING_EVIDENCE")]
    [InlineData(RoutingPolicy.WrongRoleFlag, "WRONG_ROLE")]
    [InlineData(RoutingPolicy.WrongStatusFlag, "WRONG_STATUS")]
    [InlineData(RoutingPolicy.WrongProviderFlag, "WRONG_PROVIDER")]
    [InlineData(RoutingPolicy.ForbiddenGeneralizationFlag, "FORBIDDEN_GENERALIZATION")]
    [InlineData(RoutingPolicy.HighRiskMissingConditionFlag, "HIGH_RISK_MISSING_CONDITION")]
    [InlineData(RoutingPolicy.NegatedQueryFlag, "NEGATED_QUERY")]
    [InlineData(RoutingPolicy.LowSpecificityFlag, "LOW_SPECIFICITY")]
    [InlineData(RoutingPolicy.PostInstructionFailureFlag, "POST_INSTRUCTION_FAILURE")]
    [InlineData(RoutingPolicy.MissingSourceFlag, "MISSING_SOURCE")]
    [InlineData(RoutingPolicy.InvalidEvidenceReferenceFlag, "INVALID_EVIDENCE_REFERENCE")]
    public void EachKnownRealRiskFlagGetsItsOwnReasonCodeOnASufficientAnswer(string flag, string expectedReasonCode)
    {
        // Regression: these are the actual flags src/knowledge's answerability service emits
        // (architecture.md §10) — before B2 they all collapsed into one generic RISK_FLAGGED_ANSWER.
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: false, riskFlags: [flag]);

        Assert.Equal([expectedReasonCode], decision.ReasonCodes);
    }

    [Fact]
    public void AnUnrecognizedFlagBecomesAnExplicitUnknownReasonCodeRatherThanBeingDropped()
    {
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: false, conditionDependent: false, riskFlags: ["SOME_NEW_FLAG"]);

        Assert.Contains("UNKNOWN_RISK_FLAG:SOME_NEW_FLAG", decision.ReasonCodes);
    }

    [Fact]
    public void KnowledgeUnavailableFlagIsNeverClassifiedHereItIsHandledUpstreamAsTechnicalError()
    {
        var decision = RoutingPolicy.Evaluate(evidenceInsufficient: true, conditionDependent: false, riskFlags: [RoutingPolicy.KnowledgeUnavailableFlag]);

        // Falls back to the plain INSUFFICIENT_EVIDENCE reason since the flag itself contributes nothing here.
        Assert.Equal(["INSUFFICIENT_EVIDENCE"], decision.ReasonCodes);
    }

    [Fact]
    public void MultipleRiskFlagsOnAConditionDependentCaseAllSurfaceTheirOwnReasonCodes()
    {
        var decision = RoutingPolicy.Evaluate(
            evidenceInsufficient: false, conditionDependent: true,
            riskFlags: [RoutingPolicy.NegatedQueryFlag, RoutingPolicy.MissingSourceFlag]);

        Assert.Equal(["NEGATED_QUERY", "MISSING_SOURCE"], decision.ReasonCodes);
    }
}
