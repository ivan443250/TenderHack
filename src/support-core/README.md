# Support Core scaffold

The .NET runtime owns business state, orchestration, decisions, moderation/routing policy, handoff, feedback, idempotency and the public HTTP/SSE boundary.

`TenderHack.Domain` has no framework dependency. `TenderHack.Application` depends on Domain; Infrastructure implements application ports; Api and Worker compose the runtime. The generated Knowledge client belongs in Infrastructure when contract generation is wired.

This foundation contains process health endpoints and a long-running worker host only. `/health/ready` is explicitly process-only (`dependencies=not_checked`) until API-owned persistence and the generated Knowledge client are wired in. Case behavior is intentionally not implemented yet.

Run `dotnet build TenderHack.sln` and `dotnet test TenderHack.sln` from this directory. Start the API with `dotnet run --project src/TenderHack.Api`; start the worker with `dotnet run --project src/TenderHack.Worker`.
