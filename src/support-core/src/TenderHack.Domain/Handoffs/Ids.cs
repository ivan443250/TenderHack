namespace TenderHack.Domain.Handoffs;

public readonly record struct HandoffId(Guid Value)
{
    public static HandoffId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
