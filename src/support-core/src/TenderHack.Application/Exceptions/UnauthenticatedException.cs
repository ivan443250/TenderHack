namespace TenderHack.Application.Exceptions;

/// <summary>The caller has no owner session cookie yet (web-api-v0.md §13) — call `POST /api/v0/session` first.</summary>
public sealed class UnauthenticatedException() : Exception("No owner session.");
