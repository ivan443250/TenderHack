namespace TenderHack.Application.Abstractions;

// Backed by IOptions in Infrastructure; changeable via admin endpoint without redeploy — see section 9.4.
public interface IThresholdProvider
{
    double MinConfidence { get; }
}
