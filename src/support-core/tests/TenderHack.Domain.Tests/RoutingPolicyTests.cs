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
}
