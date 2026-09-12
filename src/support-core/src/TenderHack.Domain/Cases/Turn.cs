namespace TenderHack.Domain.Cases;

/// <summary>One revision of a case's in-flight question/answer cycle. Only <see cref="Case"/> mutates it.</summary>
public sealed class Turn
{
    public TurnId Id { get; }

    public int Revision { get; }

    public DateTimeOffset CreatedAt { get; }

    public TurnStatus Status { get; private set; } = TurnStatus.Queued;

    public Decision? Decision { get; private set; }

    internal Turn(TurnId id, int revision, DateTimeOffset createdAt)
    {
        Id = id;
        Revision = revision;
        CreatedAt = createdAt;
    }

    internal void MarkRunning()
    {
        if (Status == TurnStatus.Queued)
        {
            Status = TurnStatus.Running;
        }
    }

    /// <summary>A newer user revision supersedes this one; a superseded turn can no longer publish.</summary>
    internal void Supersede()
    {
        if (Status is TurnStatus.Queued or TurnStatus.Running)
        {
            Status = TurnStatus.Superseded;
        }
    }

    internal void Complete(Decision decision)
    {
        Status = TurnStatus.Completed;
        Decision = decision;
    }

    internal void Fail()
    {
        Status = TurnStatus.Failed;
        Decision = Cases.Decision.TechnicalError;
    }
}
