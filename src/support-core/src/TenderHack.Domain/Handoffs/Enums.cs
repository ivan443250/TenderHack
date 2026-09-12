namespace TenderHack.Domain.Handoffs;

public enum HandoffStatus
{
    NotRequested,
    Pending,
    Accepted,
    SimulatedAccepted,
    Failed,
}

public enum IntegrationMode
{
    Real,
    Simulated,
}

public enum HandoffTerminalOutcome
{
    Resolved,
    ClosedUnresolved,
    Cancelled,
}
