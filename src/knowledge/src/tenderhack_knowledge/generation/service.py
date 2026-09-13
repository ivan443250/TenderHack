"""Evidence-gated draft orchestration with bounded generation."""

from __future__ import annotations

import inspect
import re
from dataclasses import dataclass
from typing import Any

from tenderhack_knowledge.answerability import assess_answerability
from tenderhack_knowledge.contracts.v0 import Claim, DraftRequest, DraftResponse, EvidenceSufficiency, TokenUsage
from tenderhack_knowledge.inference.errors import GeneratorClientError
from tenderhack_knowledge.inference.generator import GeneratorOutputTruncatedError
from tenderhack_knowledge.inference.safety import strip_reasoning
from tenderhack_knowledge.inference.prompts import DRAFT_PROMPT_VERSION, build_draft_prompt
from tenderhack_knowledge.ingestion.models import Corpus, KnowledgeFragment
from tenderhack_knowledge.ingestion.repository import CorpusBoundaryError, UnknownSnapshotError
from tenderhack_knowledge.verification import ClaimVerification, draft_text_issues, verify_claims

from .models import GroundedDraft, ModelOutputError, parse_model_output


class DraftGenerationError(RuntimeError):
    """Base error for a failed evidence-gated draft."""


class GeneratorUnavailableError(DraftGenerationError):
    """The configured local generator cannot be reached."""


class GeneratorModelError(DraftGenerationError):
    """The generator returned an unusable structured response."""


MAX_GENERATION_TOKENS = 800


def _validate_output_budget(value: object) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= MAX_GENERATION_TOKENS:
        raise GeneratorModelError(f"grounded draft max_output_tokens must be between 1 and {MAX_GENERATION_TOKENS}")
    return value


def _grounded_draft_response_format() -> dict[str, object]:
    """Return the minimal llama.cpp JSON schema for an internal draft.

    This is intentionally narrower than :class:`GroundedDraft`: optional
    post-check fields are populated by the parser/verifier, while the model
    only needs to emit the grounded markdown and its cited claims.  Keeping
    the schema explicit avoids Pydantic ``$defs``/union constructs that are
    not consistently understood by local OpenAI-compatible runtimes.
    """

    claim_schema: dict[str, object] = {
        "type": "object",
        "properties": {
            "claim_id": {"type": "string"},
            "text": {"type": "string"},
            "fragment_ids": {"type": "array", "items": {"type": "string"}, "minItems": 1},
            "applies_if": {"type": "array", "items": {"type": "string"}},
            "requires_human_check": {"type": "boolean"},
        },
        "required": ["claim_id", "text", "fragment_ids"],
        "additionalProperties": False,
    }
    return {
        "type": "json_object",
        "schema": {
            "type": "object",
            "properties": {
                "draft_markdown": {"type": "string"},
                "claims": {"type": "array", "items": claim_schema},
            },
            "required": ["draft_markdown", "claims"],
            "additionalProperties": False,
        },
    }


@dataclass(frozen=True)
class DraftGenerationResult:
    response: DraftResponse
    answerability: EvidenceSufficiency
    verification: tuple[ClaimVerification, ...] = ()


class GroundedDraftService:
    """Build a draft only after the deterministic answerability gate passes.

    The service intentionally performs one verification pass. A recoverable
    structured-output failure may trigger one compact-output retry; this
    remains bounded and never turns into an unbounded retry loop.
    """

    def __init__(self, repository: Any, generator: Any | None = None, *, generator_factory: Any | None = None) -> None:
        self.repository = repository
        self.generator = generator
        self.generator_factory = generator_factory

    async def generate(self, payload: DraftRequest) -> DraftGenerationResult:
        max_tokens = _validate_output_budget(payload.constraints.max_output_tokens)
        assessment = await assess_answerability(
            payload.query,
            payload.snapshot_id,
            payload.evidence_fragment_ids,
            self.repository,
        )
        if assessment.evidence_sufficiency is not EvidenceSufficiency.SUFFICIENT:
            return DraftGenerationResult(
                response=DraftResponse(
                    draft_markdown="",
                    claims=[],
                    model_version=f"{DRAFT_PROMPT_VERSION}:gated:{assessment.evidence_sufficiency.value}",
                    token_usage=TokenUsage(input=0, output=0),
                ),
                answerability=assessment.evidence_sufficiency,
            )

        generator = self.generator
        if generator is None and self.generator_factory is not None:
            generator = self.generator_factory()
        if generator is None:
            raise GeneratorUnavailableError("local generator is unavailable")
        fragments = await _normative_fragments(self.repository, payload.snapshot_id, payload.evidence_fragment_ids)
        prompt = build_draft_prompt(payload.query, fragments, payload.constraints)
        # Request the smallest structured-output mode supported by the
        # OpenAI-compatible local runtime.  The parser below remains strict:
        # this hint never turns prose into a grounded draft.
        response_format = _grounded_draft_response_format()
        grounded: GroundedDraft | None = None
        for attempt in range(2):
            retry_attempt = attempt == 1
            attempt_prompt = _compact_retry_prompt(prompt) if retry_attempt else prompt
            try:
                raw = await _request_generation(
                    generator,
                    attempt_prompt,
                    response_format=response_format,
                    max_tokens=max_tokens,
                )
            except GeneratorOutputTruncatedError as exc:
                if not retry_attempt:
                    # A provider-confirmed length stop is recoverable. The
                    # next request keeps exactly the same evidence and budget.
                    continue
                raise GeneratorModelError("local generator output was truncated after bounded retry") from exc

            try:
                # Reasoning is private runtime material and is removed before
                # the structured parser, regardless of which local generator
                # adapter supplied the text.
                grounded = parse_model_output(strip_reasoning(raw))
            except ModelOutputError as exc:
                if not retry_attempt and _is_retryable_model_output_error(exc):
                    continue
                raise GeneratorModelError(str(exc)) from exc
            except GeneratorClientError as exc:
                # Safety-boundary, transport-shape, and contract errors fail
                # closed; they are not evidence that a shorter prompt helps.
                raise GeneratorModelError(str(exc)) from exc
            break

        if grounded is None:  # pragma: no cover - loop either succeeds or raises
            raise GeneratorModelError("local generator returned no usable structured draft")

        # Verify the internal claims before projecting them. This preserves
        # ``applies_if`` and human-check markers for the condition-sensitive
        # postcheck; those fields are intentionally absent from the frozen
        # public Claim DTO.
        verification = await verify_claims(payload.snapshot_id, grounded.claims, self.repository)
        unsupported = tuple(item for item in verification if not item.result.supported)
        if unsupported:
            # A substantive draft with an unsupported claim is never emitted.
            # The bounded policy reports MODEL_ERROR; it does not silently trim
            # claims or retry forever.
            raise GeneratorModelError("grounded draft failed deterministic claim verification")
        rendered_markdown = grounded.draft_markdown.strip() or "\n\n".join(block.strip() for block in grounded.answer_blocks if block.strip())
        if grounded.claims and not rendered_markdown:
            raise GeneratorModelError("grounded claims require non-empty draft_markdown")
        if not grounded.claims and rendered_markdown:
            raise GeneratorModelError("substantive draft requires at least one grounded claim")
        if draft_text_issues(rendered_markdown, tuple(fragment.text for fragment in fragments)):
            raise GeneratorModelError("grounded draft contains an unsupported source URL")
        public_claims = [Claim(claim_id=claim.claim_id, text=claim.text, fragment_ids=list(claim.fragment_ids)) for claim in grounded.claims]
        model_version = _model_version(generator)
        return DraftGenerationResult(
            response=DraftResponse(
                draft_markdown=rendered_markdown,
                claims=public_claims,
                model_version=f"{DRAFT_PROMPT_VERSION}:{model_version}",
                token_usage=TokenUsage(input=_estimate_tokens(prompt), output=_estimate_tokens(rendered_markdown)),
            ),
            answerability=assessment.evidence_sufficiency,
            verification=verification,
        )


async def create_grounded_draft(
    payload: DraftRequest,
    repository: Any,
    generator: Any | None = None,
    *,
    generator_factory: Any | None = None,
) -> DraftGenerationResult:
    return await GroundedDraftService(repository, generator, generator_factory=generator_factory).generate(payload)


async def _normative_fragments(repository: Any, snapshot_id: str, requested_ids: list[str]) -> tuple[KnowledgeFragment, ...]:
    snapshot = repository.get_snapshot(snapshot_id)
    if inspect.isawaitable(snapshot):
        snapshot = await snapshot
    if snapshot is None:
        raise UnknownSnapshotError(snapshot_id)
    if snapshot.corpus != Corpus.NORMATIVE:
        raise CorpusBoundaryError(f"draft requires a normative snapshot, got {snapshot.corpus.value}")
    values = repository.snapshot_fragments(snapshot_id)
    if inspect.isawaitable(values):
        values = await values
    by_id = {fragment.fragment_id: fragment for fragment in values}
    # The answerability gate already rejects a partial invalid candidate set;
    # retain caller ordering for reproducible prompts and provenance.
    return tuple(by_id[fragment_id] for fragment_id in dict.fromkeys(requested_ids))


def _is_unavailable(error: GeneratorClientError) -> bool:
    message = str(error).casefold()
    return any(marker in message for marker in ("request failed", "returned http 502", "returned http 503", "returned http 504", "timed out", "connection"))


async def _request_generation(
    generator: Any,
    prompt: str,
    *,
    response_format: dict[str, object],
    max_tokens: int,
) -> str:
    """Call one generation attempt while preserving unavailable semantics."""

    try:
        return await generator.draft(prompt, response_format=response_format, max_tokens=max_tokens)
    except GeneratorOutputTruncatedError:
        # Keep the internal provider signal distinct so the caller can spend
        # the one bounded recovery attempt without changing the public DTO.
        raise
    except GeneratorClientError as exc:
        if _is_unavailable(exc):
            raise GeneratorUnavailableError("local generator is unavailable") from exc
        raise GeneratorModelError("local generator failed") from exc
    except (TimeoutError, ConnectionError, OSError) as exc:
        raise GeneratorUnavailableError("local generator is unavailable") from exc


def _is_retryable_model_output_error(error: ModelOutputError) -> bool:
    """Identify parse-level shape failures safe for one compact retry.

    Business-field rejection and duplicate-claim failures stay fail-closed;
    they are not repaired by asking the model to restate the same payload.
    """

    message = str(error).casefold()
    return any(
        marker in message
        for marker in (
            "empty structured draft",
            "invalid json",
            "structured draft must be a json object",
            "structured draft failed schema validation",
        )
    )


def _compact_retry_prompt(prompt: str) -> str:
    """Ask for a shorter JSON response without changing supplied evidence."""

    return (
        prompt
        + "\n\nBOUNDED RECOVERY RETRY (TRUNCATION RETRY): Return only one shortest valid JSON object in the same schema. "
        "Keep draft_markdown concise, use at most 3 grounded claims, do not repeat long source excerpts, "
        "do not add markdown outside JSON, do not include reasoning, and use only the supplied fragment_ids."
    )


def _model_version(generator: Any) -> str:
    metadata = getattr(generator, "metadata", {})
    if isinstance(metadata, dict):
        model_id = metadata.get("model_id") or getattr(generator, "model_id", "local-generator")
        revision = metadata.get("revision")
        return f"{model_id}@{revision}" if revision else str(model_id)
    return str(getattr(generator, "model_id", "local-generator"))


_TOKEN_ESTIMATE_RE = re.compile(r"\S+")


def _estimate_tokens(text: str) -> int:
    # The adapter does not expose provider token usage. This deliberately
    # labelled estimate is deterministic and never presented as measured
    # runtime telemetry.
    return len(_TOKEN_ESTIMATE_RE.findall(text))
