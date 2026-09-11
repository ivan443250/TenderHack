using TenderHack.Domain.Enums;

namespace TenderHack.Domain.Entities;

public sealed class Specialist
{
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = null!;
    public SupportLine Line { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Specialist() { }

    public Specialist(Guid id, string fullName, SupportLine line)
    {
        Id = id;
        FullName = fullName;
        Line = line;
    }

    public void Deactivate() => IsActive = false;
}
