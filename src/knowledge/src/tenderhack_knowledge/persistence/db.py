from collections.abc import AsyncIterator

from sqlalchemy.ext.asyncio import AsyncEngine, AsyncSession, async_sessionmaker, create_async_engine

from tenderhack_knowledge.settings.config import get_settings


def create_engine() -> AsyncEngine | None:
    url = get_settings().database_url
    return create_async_engine(url, pool_pre_ping=True) if url else None


async def session_scope() -> AsyncIterator[AsyncSession]:
    engine = create_engine()
    if engine is None:
        raise RuntimeError("Knowledge database is not configured")
    factory = async_sessionmaker(engine, expire_on_commit=False)
    async with factory() as session:
        yield session
    await engine.dispose()
