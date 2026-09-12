"""Versioned, source-grounded condition cards.

Condition cards narrow when a source fragment is applicable.  They provide
evidence constraints only; support-core remains the owner of user-visible
business decisions and state transitions.
"""

from .cards import (
    CardMatchContext,
    CardMatchResult,
    CardMatchReason,
    ConditionCard,
    ConditionCardApplicability,
    ConditionCardRisk,
    match_card,
)
from .repository import InMemoryConditionCardStore
from .retrieval import ConditionCardCandidate, ConditionCardAugmentation, augment_with_cards, rank_condition_cards
from .seed import load_condition_card_seed, seed_path
from .verification import ConditionCardAudit, ConditionCardVerificationError, verify_condition_cards

__all__ = [
    "CardMatchContext",
    "CardMatchResult",
    "CardMatchReason",
    "ConditionCard",
    "ConditionCardApplicability",
    "ConditionCardRisk",
    "match_card",
    "InMemoryConditionCardStore",
    "load_condition_card_seed",
    "seed_path",
    "ConditionCardCandidate",
    "ConditionCardAugmentation",
    "augment_with_cards",
    "rank_condition_cards",
    "ConditionCardAudit",
    "ConditionCardVerificationError",
    "verify_condition_cards",
]
