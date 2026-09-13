#!/bin/sh
set -eu

# The .NET side applies EF migrations on startup; this service didn't, so `knowledge` tables never
# got created unless someone ran `alembic upgrade head` by hand in the container. Both `knowledge`
# (uvicorn) and `knowledge-worker` share this image/entrypoint, so run it once before either command.
if [ "${KNOWLEDGE_SKIP_MIGRATIONS:-false}" != "true" ]; then
    alembic upgrade head
fi

exec "$@"
