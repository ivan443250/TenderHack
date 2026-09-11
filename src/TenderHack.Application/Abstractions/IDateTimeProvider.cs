namespace TenderHack.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTimeOffset Now { get; }
}
