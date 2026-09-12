"""Loader for the reproducible K3 card seed artifact."""

from __future__ import annotations

import json
from pathlib import Path

from .cards import ConditionCard


def seed_path() -> Path:
    """Return the repository seed path without consulting a moving snapshot."""

    return Path(__file__).resolve().parents[3] / "conditions" / "seed.json"


def load_condition_card_seed(path: str | Path | None = None) -> tuple[ConditionCard, ...]:
    source = Path(path) if path is not None else seed_path()
    payload = json.loads(source.read_text(encoding="utf-8"))
    return tuple(ConditionCard.model_validate(value) for value in payload["cards"])
