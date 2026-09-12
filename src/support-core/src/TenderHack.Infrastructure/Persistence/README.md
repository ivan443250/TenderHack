# API-owned persistence boundary

EF Core/Npgsql model for `TenderHack.Domain`'s `Case` aggregate (owned `Turn`s and `Handoff`), plus the `case_events` and `outbox` tables (architecture.md §8). Migrations here may only create Support Core tables — `kb_*` and `quality_*` belong to `knowledge` and are never queried or migrated by this runtime.

## Adding a migration

```bash
dotnet ef migrations add <Name> -o Persistence/Migrations --project src/TenderHack.Infrastructure
```

`DesignTimeDbContextFactory` lets this run without a live database or a running `Api`/`Worker` host — the connection string it uses is never actually opened for migration generation.
