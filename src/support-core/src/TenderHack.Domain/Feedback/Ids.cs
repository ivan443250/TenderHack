namespace TenderHack.Domain.Feedback;

public readonly record struct FeedbackId(Guid Value)
{
    public static FeedbackId New() => new(Guid.NewGuid());

    public static bool TryParse(string? text, out FeedbackId id)
    {
        if (Guid.TryParse(text, out var value))
        {
            id = new FeedbackId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
