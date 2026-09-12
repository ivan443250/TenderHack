"""Small in-memory condition-card store for deterministic tests and tooling."""

from __future__ import annotations

from collections.abc import Iterable

from .cards import ConditionCard


class InMemoryConditionCardStore:
    """Immutable versioned store keyed by ``(card_id, version)``."""

    def __init__(self) -> None:
        self._cards: dict[tuple[str, str], ConditionCard] = {}

    def put(self, card: ConditionCard) -> ConditionCard:
        key = (card.card_id, card.version)
        existing = self._cards.get(key)
        if existing is not None and existing != card:
            raise ValueError(f"condition card identity collision for {card.card_id}@{card.version}")
        self._cards[key] = card
        return existing or card

    def put_many(self, cards: Iterable[ConditionCard]) -> int:
        values = tuple(cards)
        for card in values:
            self.put(card)
        return len(values)

    def get(self, card_id: str, version: str) -> ConditionCard | None:
        return self._cards.get((card_id, version))

    def for_snapshot(self, snapshot_id: str, *, review_status: str | None = "VERIFIED") -> tuple[ConditionCard, ...]:
        values = [card for card in self._cards.values() if card.snapshot_id == snapshot_id]
        if review_status is not None:
            values = [card for card in values if card.review_status == review_status]
        return tuple(sorted(values, key=lambda card: (card.card_id, card.version)))

    def __len__(self) -> int:
        return len(self._cards)
