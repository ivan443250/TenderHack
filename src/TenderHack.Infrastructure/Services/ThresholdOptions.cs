namespace TenderHack.Infrastructure.Services;

public sealed class ThresholdOptions
{
    public const string SectionName = "Thresholds";

    public double MinConfidence { get; set; } = 0.55;
}
