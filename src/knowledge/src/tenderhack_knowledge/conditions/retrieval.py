"""Non-destructive condition-card augmentation for retrieval results."""

from __future__ import annotations

from collections.abc import Iterable, Sequence
from dataclasses import dataclass

from .cards import CardMatchContext, CardMatchResult, ConditionCard, match_card


@dataclass(frozen=True)
class ConditionCardCandidate:
    card: ConditionCard
    match: CardMatchResult


@dataclass(frozen=True)
class ConditionCardAugmentation:
    """Raw evidence is retained; applicable cards are an adjacent layer."""

    raw_candidates: tuple[object, ...]
    condition_cards: tuple[ConditionCardCandidate, ...]


def rank_condition_cards(cards: Iterable[ConditionCard], context: CardMatchContext) -> tuple[ConditionCardCandidate, ...]:
    candidates = [
        ConditionCardCandidate(card=card, match=match_card(card, context))
        for card in cards
    ]
    # Inapplicable cards remain observable for diagnostics, but are always
    # after applicable cards and can never be selected as evidence.
    return tuple(
        sorted(
            candidates,
            key=lambda candidate: (
                not candidate.match.applicable,
                -candidate.match.score,
                candidate.card.card_id,
                candidate.card.version,
            ),
        )
    )


def augment_with_cards(
    raw_candidates: Sequence[object],
    cards: Iterable[ConditionCard],
    context: CardMatchContext,
) -> ConditionCardAugmentation:
    """Attach cards beside raw retrieval; never hide or reorder raw evidence."""

    ranked = rank_condition_cards(cards, context)
    return ConditionCardAugmentation(
        raw_candidates=tuple(raw_candidates),
        # The augmentation boundary exposes only applicable cards.  Callers
        # that need missing-slot diagnostics can use rank_condition_cards()
        # directly; an unmet condition cannot become a hidden universal rule.
        condition_cards=tuple(candidate for candidate in ranked if candidate.match.applicable),
    )
