namespace TenderHack.Domain.Handoffs;

public readonly record struct HandoffId(Guid Value)
{
    public static HandoffId New() => new(Guid.NewGuid());

    public static bool TryParse(string? text, out HandoffId id)
    {
        if (Guid.TryParse(text, out var value))
        {
            id = new HandoffId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}
