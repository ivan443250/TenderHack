# Support Core

The .NET runtime owns business state, orchestration, decisions, moderation/routing policy, handoff, completion/archive, notifications, feedback, idempotency and the public HTTP/SSE boundary.

`TenderHack.Domain` has no framework dependency. `TenderHack.Application` depends on Domain; Infrastructure implements application ports; Api and Worker compose the runtime. Knowledge/inference remains behind `IKnowledgeService`; business decisions stay in .NET.

Current code includes the case/turn state machine, `TurnOrchestrator`, moderation/routing paths, handoff/outbox/status handling, completion/feedback/notifications and API/worker infrastructure. Do not treat this directory as a health-only scaffold. Exact implemented-vs-target semantics are documented in `../../docs/architecture.md`, `../../docs/product-spec.md` and the contracts; documentation must not claim an unverified runtime capability merely because it is specified.

Run `dotnet build TenderHack.sln` and `dotnet test TenderHack.sln` from this directory. Start the API with `dotnet run --project src/TenderHack.Api`; start the worker with `dotnet run --project src/TenderHack.Worker`.
