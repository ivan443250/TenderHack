from functools import lru_cache
import os

from pydantic import BaseModel, Field


class Settings(BaseModel):
    service_name: str = "knowledge"
    database_url: str = Field(default="", description="Knowledge-owned PostgreSQL URL")
    check_database_on_ready: bool = True
    inference_base_url: str = "http://inference:8000"
    environment: str = "Development"

    @classmethod
    def from_env(cls) -> "Settings":
        raw_check = os.getenv("KNOWLEDGE_CHECK_DB", "true").lower()
        return cls(
            database_url=os.getenv("DATABASE_URL", os.getenv("KNOWLEDGE_DATABASE_URL", "")),
            check_database_on_ready=raw_check not in {"0", "false", "no"},
            inference_base_url=os.getenv("INFERENCE_BASE_URL", "http://inference:8000"),
            environment=os.getenv("ASPNETCORE_ENVIRONMENT", os.getenv("ENVIRONMENT", "Development")),
        )


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings.from_env()
