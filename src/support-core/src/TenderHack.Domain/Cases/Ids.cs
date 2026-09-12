namespace TenderHack.Domain.Cases;

public readonly record struct CaseId(Guid Value)
{
    public static CaseId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct TurnId(Guid Value)
{
    public static TurnId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
