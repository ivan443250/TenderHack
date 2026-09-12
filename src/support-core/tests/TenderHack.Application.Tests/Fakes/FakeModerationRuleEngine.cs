using TenderHack.Application.Ports;

namespace TenderHack.Application.Tests.Fakes;

public sealed class FakeModerationRuleEngine : IModerationRuleEngine
{
    public ModerationRuleMatch? NextResult { get; set; }

    public ModerationRuleMatch? Evaluate(string text) => NextResult;
}
