from fastapi import FastAPI, HTTPException

from tenderhack_knowledge.api.routes import router
from tenderhack_knowledge.observability.logging import configure_logging
from tenderhack_knowledge.settings.config import get_settings

configure_logging()
app = FastAPI(title="TenderHack Knowledge Service", version="0.1.0")
app.include_router(router)


@app.get("/health/live")
def health_live() -> dict[str, str]:
    return {"status": "ok", "service": get_settings().service_name}


@app.get("/health/ready")
async def health_ready() -> dict[str, str]:
    settings = get_settings()
    if settings.check_database_on_ready and settings.database_url:
        from tenderhack_knowledge.persistence.db import create_engine

        engine = create_engine()
        if engine is not None:
            try:
                async with engine.connect():
                    pass
            except Exception as exc:  # pragma: no cover - depends on external DB
                raise HTTPException(status_code=503, detail="knowledge database is unavailable") from exc
            finally:
                await engine.dispose()
    return {"status": "ready", "service": settings.service_name}


def run() -> None:
    import uvicorn

    uvicorn.run("tenderhack_knowledge.main:app", host="0.0.0.0", port=8000)
