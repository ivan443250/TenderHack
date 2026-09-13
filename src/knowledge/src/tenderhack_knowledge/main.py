from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from tenderhack_knowledge.api.routes import get_knowledge_repository, router
from tenderhack_knowledge.contracts.v0 import ErrorCode, ErrorResponse
from tenderhack_knowledge.observability.logging import configure_logging
from tenderhack_knowledge.settings.config import get_settings

configure_logging()


@asynccontextmanager
async def lifespan(app: FastAPI):
    del app
    yield
    # `get_knowledge_repository`/`get_quality_repository` are process-wide `lru_cache`
    # singletons (one engine/pool for the process) — dispose the pool here rather than
    # leaving connections open past the last request.
    repository = get_knowledge_repository()
    if repository is not None:
        await repository.engine.dispose()


app = FastAPI(title="TenderHack Knowledge Service", version="0.1.0", lifespan=lifespan)
app.include_router(router)


def _validation_details(exc: RequestValidationError) -> list[dict[str, object]]:
    return [
        {
            "loc": [str(part) for part in error.get("loc", ())],
            "type": error.get("type", "validation_error"),
            "message": error.get("msg", "Request validation failed"),
        }
        for error in exc.errors()
    ]


def _header_name(value: object) -> str:
    names = {
        "x-trace-id": "X-Trace-Id",
        "x-case-id": "X-Case-Id",
        "x-turn-id": "X-Turn-Id",
    }
    return names.get(str(value).lower(), str(value))


@app.exception_handler(RequestValidationError)
async def request_validation_handler(request: Request, exc: RequestValidationError) -> JSONResponse:
    del request
    missing_headers = [
        _header_name(error["loc"][-1])
        for error in exc.errors()
        if error.get("type") == "missing" and error.get("loc") and error["loc"][0] == "header"
    ]
    if missing_headers:
        body = ErrorResponse(
            code=ErrorCode.MISSING_TRACE_HEADER,
            message=f"Required headers missing: {', '.join(dict.fromkeys(missing_headers))}",
        )
        return JSONResponse(status_code=400, content=body.model_dump(mode="json"))

    body = ErrorResponse(
        code=ErrorCode.VALIDATION_ERROR,
        message="Request validation failed",
        detail={"errors": _validation_details(exc)},
    )
    return JSONResponse(status_code=422, content=body.model_dump(mode="json"))


@app.exception_handler(HTTPException)
async def http_exception_handler(request: Request, exc: HTTPException) -> JSONResponse:
    del request
    if isinstance(exc.detail, dict) and {"code", "message"}.issubset(exc.detail):
        body = ErrorResponse.model_validate(exc.detail)
    else:
        code = ErrorCode.INTERNAL_ERROR if exc.status_code >= 500 else ErrorCode.VALIDATION_ERROR
        body = ErrorResponse(code=code, message=str(exc.detail))
    return JSONResponse(status_code=exc.status_code, content=body.model_dump(mode="json"))


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
