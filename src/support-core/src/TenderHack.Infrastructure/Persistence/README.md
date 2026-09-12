# API-owned persistence boundary

EF Core/Npgsql migrations added here may create only Support Core tables (`cases`, `turns`, events, handoffs, outbox, feedback and API jobs). Knowledge-owned `kb_*` and quality tables are never queried or migrated by this runtime.
