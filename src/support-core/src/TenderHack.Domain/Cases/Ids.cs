namespace TenderHack.Domain.Cases;

public readonly record struct CaseId(Guid Value)
{
    public static CaseId New() => new(Guid.NewGuid());

    public static bool TryParse(string? text, out CaseId id)
    {
        if (Guid.TryParse(text, out var value))
        {
            id = new CaseId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString();
}

public readonly record struct TurnId(Guid Value)
{
    public static TurnId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
