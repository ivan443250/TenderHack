"""Durable PostgreSQL repository for the knowledge-owned provenance model.

The repository uses SQLAlchemy Core only.  It never reads or writes the
support-core/API tables and keeps snapshot membership separate from canonical
fragment rows so an older snapshot cannot be rewritten by a later publish.
"""

from __future__ import annotations

from collections.abc import Iterable, Mapping, Sequence
from datetime import datetime

import sqlalchemy as sa
from sqlalchemy import Select, select
from sqlalchemy.dialects.postgresql import REGCONFIG, insert as pg_insert
from sqlalchemy.ext.asyncio import AsyncConnection, AsyncEngine

from tenderhack_knowledge.ingestion.ids import make_snapshot_id
from tenderhack_knowledge.ingestion.models import (
    Corpus,
    Document,
    DocumentVersion,
    IngestionRun,
    KnowledgeFragment,
    KnowledgeSnapshot,
    SourceAnchor,
    SourceResolution,
)
from tenderhack_knowledge.ingestion.repository import (
    CorpusBoundaryError,
    UnknownFragmentError,
    UnknownSnapshotError,
)
from tenderhack_knowledge.inference.errors import (
    EmbeddingRevisionMismatchError,
    EmbeddingUnavailableError,
)
from tenderhack_knowledge.inference.model_refs import EMBEDDING_DIMENSION, EMBEDDING_MODEL_ID, EMBEDDING_REVISION
from tenderhack_knowledge.conditions.cards import ConditionCard


metadata = sa.MetaData()

# Keep weak typo recovery useful without presenting an unrelated short query
# as evidence.  The threshold is deliberately above the observed unrelated
# Russian probe (0.35) and below the measured misspelled gold query (0.615).
TRIGRAM_MIN_SCORE = 0.40

kb_documents = sa.Table(
    "kb_documents",
    metadata,
    sa.Column("document_id", sa.String(128), primary_key=True),
    sa.Column("original_filename", sa.Text(), nullable=False),
    sa.Column("corpus", sa.String(16), nullable=False),
)
kb_document_versions = sa.Table(
    "kb_document_versions",
    metadata,
    sa.Column("document_version_id", sa.String(128), primary_key=True),
    sa.Column("document_id", sa.String(128), nullable=False),
    sa.Column("content_sha256", sa.String(64), nullable=False),
    sa.Column("declared_version", sa.String(128)),
    sa.Column("declared_date", sa.Date()),
    sa.Column("page_count", sa.Integer(), nullable=False),
    sa.Column("source_reference", sa.Text(), nullable=False),
    sa.Column("ingested_at", sa.DateTime(timezone=True), nullable=False),
    sa.Column("review_status", sa.String(32), nullable=False),
)
kb_snapshots = sa.Table(
    "kb_snapshots",
    metadata,
    sa.Column("snapshot_id", sa.String(128), primary_key=True),
    sa.Column("corpus", sa.String(16), nullable=False),
    sa.Column("manifest_hash", sa.String(64), nullable=False),
    sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    sa.Column("review_status", sa.String(32), nullable=False),
)
kb_snapshot_document_versions = sa.Table(
    "kb_snapshot_document_versions",
    metadata,
    sa.Column("snapshot_id", sa.String(128), nullable=False),
    sa.Column("document_version_id", sa.String(128), nullable=False),
)
kb_fragments = sa.Table(
    "kb_fragments",
    metadata,
    sa.Column("fragment_id", sa.String(128), primary_key=True),
    sa.Column("document_version_id", sa.String(128), nullable=False),
    sa.Column("page_start", sa.Integer(), nullable=False),
    sa.Column("page_end", sa.Integer(), nullable=False),
    sa.Column("section", sa.Text()),
    sa.Column("kind", sa.String(32), nullable=False),
    sa.Column("heading_path", sa.JSON(), nullable=False),
    sa.Column("text", sa.Text(), nullable=False),
    sa.Column("source_anchor", sa.JSON(), nullable=False),
    sa.Column("actor_roles", sa.JSON()),
    sa.Column("process", sa.Text()),
    sa.Column("provider", sa.Text()),
    sa.Column("document_status", sa.String(64)),
    sa.Column("error_codes", sa.JSON()),
    sa.Column("review_status", sa.String(32), nullable=False),
    sa.Column("text_hash", sa.String(64), nullable=False),
)
kb_snapshot_fragments = sa.Table(
    "kb_snapshot_fragments",
    metadata,
    sa.Column("snapshot_id", sa.String(128), nullable=False),
    sa.Column("fragment_id", sa.String(128), nullable=False),
)
kb_fragment_embeddings = sa.Table(
    "kb_fragment_embeddings",
    metadata,
    sa.Column("fragment_id", sa.String(128), primary_key=True),
    sa.Column("model_id", sa.Text(), nullable=False),
    sa.Column("model_revision", sa.Text(), nullable=False),
    sa.Column("dimension", sa.Integer(), nullable=False),
    # The deployed column is pgvector vector(2048).  Text here keeps the
    # lightweight package importable without the optional pgvector SQLAlchemy
    # extension; writes/searches cast explicitly in SQL below.
    sa.Column("embedding", sa.Text(), nullable=False),
    sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
)
kb_condition_cards = sa.Table(
    "kb_condition_cards",
    metadata,
    sa.Column("card_id", sa.String(128), nullable=False),
    sa.Column("version", sa.String(64), nullable=False),
    sa.Column("snapshot_id", sa.String(128), nullable=False),
    sa.Column("title", sa.Text(), nullable=False),
    sa.Column("applicability", sa.JSON(), nullable=False),
    sa.Column("required_slots", sa.JSON(), nullable=False),
    sa.Column("allowed_action", sa.Text(), nullable=False),
    sa.Column("forbidden_generalization", sa.Text(), nullable=False),
    sa.Column("handoff_condition", sa.Text(), nullable=False),
    sa.Column("risk_level", sa.String(8), nullable=False),
    sa.Column("source_fragment_ids", sa.JSON(), nullable=False),
    sa.Column("source_quotes", sa.JSON(), nullable=False),
    sa.Column("source_anchors", sa.JSON(), nullable=False),
    sa.Column("review_status", sa.String(32), nullable=False),
    sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    sa.PrimaryKeyConstraint("card_id", "version"),
    sa.CheckConstraint("risk_level IN ('LOW', 'MEDIUM', 'HIGH')", name="ck_kb_condition_cards_risk"),
)
kb_ingestion_runs = sa.Table(
    "kb_ingestion_runs",
    metadata,
    sa.Column("run_id", sa.String(128), primary_key=True),
    sa.Column("status", sa.String(32), nullable=False),
    sa.Column("source_count", sa.Integer(), nullable=False),
    sa.Column("created_at", sa.DateTime(timezone=True), nullable=False),
    sa.Column("corpus", sa.String(16)),
    sa.Column("document_version_ids", sa.JSON()),
    sa.Column("completed_at", sa.DateTime(timezone=True)),
    sa.Column("warnings", sa.JSON()),
    sa.Column("error", sa.Text()),
)


class PostgresKnowledgeRepository:
    """Async durable implementation of the ingestion/source repository."""

    def __init__(self, engine: AsyncEngine) -> None:
        self.engine = engine

    async def register_document(self, document: Document) -> Document:
        async with self.engine.begin() as connection:
            row = await connection.execute(
                select(kb_documents).where(kb_documents.c.document_id == document.document_id)
            )
            existing = row.mappings().first()
            if existing is None:
                await connection.execute(
                    pg_insert(kb_documents)
                    .values(
                        document_id=document.document_id,
                        original_filename=document.original_filename,
                        corpus=document.corpus.value,
                    )
                    .on_conflict_do_nothing(index_elements=[kb_documents.c.document_id])
                )
                stored_row = await connection.execute(
                    select(kb_documents).where(kb_documents.c.document_id == document.document_id)
                )
                stored = stored_row.mappings().first()
                if stored is None:  # pragma: no cover - the insert/select share a transaction
                    raise RuntimeError(f"document was not stored: {document.document_id}")
                self._validate_document_identity(stored, document)
                return _document_from_row(stored)
            self._validate_document_identity(existing, document)
            return _document_from_row(existing)

    async def store_version(
        self,
        version: DocumentVersion,
        fragments: Iterable[KnowledgeFragment],
        run: IngestionRun,
    ) -> tuple[DocumentVersion, tuple[KnowledgeFragment, ...], IngestionRun, bool]:
        fragment_values = tuple(fragments)
        async with self.engine.begin() as connection:
            existing_row = await self._one(connection, select(kb_document_versions).where(kb_document_versions.c.document_version_id == version.document_version_id))
            if existing_row is not None:
                existing = _version_from_row(existing_row)
                if not _same_version_facts(existing, version):
                    raise ValueError(f"document version collision for {version.document_version_id}")
                stored_fragments = await self._fragments_for_version(connection, version.document_version_id)
                stored_run_row = await self._one(connection, select(kb_ingestion_runs).where(kb_ingestion_runs.c.run_id == run.run_id))
                if stored_run_row is None:
                    await connection.execute(pg_insert(kb_ingestion_runs).values(_run_values(run)).on_conflict_do_nothing(index_elements=[kb_ingestion_runs.c.run_id]))
                    stored_run = run
                else:
                    stored_run = _run_from_row(stored_run_row)
                return existing, stored_fragments, stored_run, True

            if any(fragment.document_version_id != version.document_version_id for fragment in fragment_values):
                raise ValueError("fragment belongs to a different document version")
            await connection.execute(pg_insert(kb_document_versions).values(_version_values(version)))
            for fragment in fragment_values:
                existing_fragment_row = await self._one(connection, select(kb_fragments).where(kb_fragments.c.fragment_id == fragment.fragment_id))
                if existing_fragment_row is not None:
                    if _fragment_from_row(existing_fragment_row) != fragment:
                        raise ValueError(f"fragment identity collision for {fragment.fragment_id}")
                    continue
                await connection.execute(pg_insert(kb_fragments).values(_fragment_values(fragment)))
            await connection.execute(pg_insert(kb_ingestion_runs).values(_run_values(run)))
            return version, fragment_values, run, False

    async def publish_snapshot(
        self,
        document_version_ids: Iterable[str],
        corpus: Corpus | str,
    ) -> KnowledgeSnapshot:
        corpus_value = corpus.value if isinstance(corpus, Corpus) else str(corpus)
        version_ids = tuple(sorted(set(document_version_ids)))
        if not version_ids:
            raise ValueError("cannot publish an empty knowledge snapshot")
        snapshot_id, manifest_hash = make_snapshot_id(corpus_value, version_ids)
        async with self.engine.begin() as connection:
            rows = await connection.execute(
                select(kb_document_versions.c.document_version_id, kb_documents.c.corpus)
                .join(kb_documents, kb_documents.c.document_id == kb_document_versions.c.document_id)
                .where(kb_document_versions.c.document_version_id.in_(version_ids))
            )
            versions = {row.document_version_id: row.corpus for row in rows}
            missing = [version_id for version_id in version_ids if version_id not in versions]
            if missing:
                raise UnknownSnapshotError(f"unknown document version(s): {', '.join(missing)}")
            wrong_corpus = [version_id for version_id in version_ids if versions[version_id] != corpus_value]
            if wrong_corpus:
                raise CorpusBoundaryError(
                    f"cannot publish {versions[wrong_corpus[0]]} document version in {corpus_value} snapshot"
                )

            existing_row = await self._one(connection, select(kb_snapshots).where(kb_snapshots.c.snapshot_id == snapshot_id))
            if existing_row is not None:
                existing_versions = await self._version_ids_for_snapshot(connection, snapshot_id)
                existing = _snapshot_from_row(existing_row, existing_versions)
                if existing.document_version_ids != version_ids or existing.corpus.value != corpus_value:
                    raise ValueError(f"snapshot identity collision for {snapshot_id}")
                return existing

            created_at = datetime.now().astimezone()
            await connection.execute(
                pg_insert(kb_snapshots).values(
                    snapshot_id=snapshot_id,
                    corpus=corpus_value,
                    manifest_hash=manifest_hash,
                    created_at=created_at,
                    review_status="PENDING_REVIEW",
                )
            )
            await connection.execute(
                pg_insert(kb_snapshot_document_versions),
                [
                    {"snapshot_id": snapshot_id, "document_version_id": version_id}
                    for version_id in version_ids
                ],
            )
            fragment_rows = await connection.execute(
                select(kb_fragments.c.fragment_id)
                .where(kb_fragments.c.document_version_id.in_(version_ids))
            )
            fragment_ids = [row.fragment_id for row in fragment_rows]
            if fragment_ids:
                await connection.execute(
                    pg_insert(kb_snapshot_fragments),
                    [{"snapshot_id": snapshot_id, "fragment_id": fragment_id} for fragment_id in fragment_ids],
                )
            return KnowledgeSnapshot(
                snapshot_id=snapshot_id,
                corpus=Corpus(corpus_value),
                document_version_ids=version_ids,
                manifest_hash=manifest_hash,
                created_at=created_at,
                review_status="PENDING_REVIEW",
            )

    async def get_current_normative_snapshot(self) -> KnowledgeSnapshot | None:
        async with self.engine.connect() as connection:
            row = await self._one(
                connection,
                select(kb_snapshots)
                .where(kb_snapshots.c.corpus == Corpus.NORMATIVE.value)
                .order_by(kb_snapshots.c.created_at.desc(), kb_snapshots.c.snapshot_id.desc())
                .limit(1),
            )
            if row is None:
                return None
            versions = await self._version_ids_for_snapshot(connection, row["snapshot_id"])
            return _snapshot_from_row(row, versions)

    async def get_snapshot(self, snapshot_id: str) -> KnowledgeSnapshot | None:
        async with self.engine.connect() as connection:
            row = await self._one(connection, select(kb_snapshots).where(kb_snapshots.c.snapshot_id == snapshot_id))
            if row is None:
                return None
            versions = await self._version_ids_for_snapshot(connection, snapshot_id)
            return _snapshot_from_row(row, versions)

    async def snapshot_fragments(self, snapshot_id: str) -> tuple[KnowledgeFragment, ...]:
        async with self.engine.connect() as connection:
            if await self._one(connection, select(kb_snapshots.c.snapshot_id).where(kb_snapshots.c.snapshot_id == snapshot_id)) is None:
                raise UnknownSnapshotError(snapshot_id)
            result = await connection.execute(
                select(kb_fragments)
                .join(kb_snapshot_fragments, kb_snapshot_fragments.c.fragment_id == kb_fragments.c.fragment_id)
                .where(kb_snapshot_fragments.c.snapshot_id == snapshot_id)
                .order_by(kb_fragments.c.page_start, kb_fragments.c.page_end, kb_fragments.c.fragment_id)
            )
            return tuple(
                _fragment_from_row(row).model_copy(update={"snapshot_id": snapshot_id})
                for row in result.mappings()
            )

    async def store_condition_cards(self, cards: Iterable[ConditionCard]) -> int:
        """Persist immutable card versions after a normative provenance check."""

        values = tuple(cards)
        if not values:
            return 0
        by_snapshot: dict[str, list[ConditionCard]] = {}
        for card in values:
            by_snapshot.setdefault(card.snapshot_id, []).append(card)

        # Validate every source reference before writing.  This deliberately
        # does not use the moving "current" snapshot: cards are pinned to the
        # snapshot recorded in their seed artifact.
        for card_snapshot_id, snapshot_cards in by_snapshot.items():
            snapshot = await self.get_snapshot(card_snapshot_id)
            if snapshot is None:
                raise UnknownSnapshotError(card_snapshot_id)
            if snapshot.corpus != Corpus.NORMATIVE:
                raise CorpusBoundaryError(
                    f"condition cards may only attach to normative snapshots, got {snapshot.corpus.value}"
                )
            fragments = {fragment.fragment_id: fragment for fragment in await self.snapshot_fragments(card_snapshot_id)}
            for card in snapshot_cards:
                missing = sorted(set(card.source_fragment_ids) - set(fragments))
                if missing:
                    raise ValueError(
                        f"condition card {card.card_id}@{card.version} references fragments outside snapshot: "
                        + ", ".join(missing[:3])
                    )
                if any(not fragments[fragment_id].text.strip() for fragment_id in card.source_fragment_ids):
                    raise ValueError(f"condition card {card.card_id}@{card.version} references empty source text")

        async with self.engine.begin() as connection:
            for card in values:
                payload = _condition_card_values(card)
                await connection.execute(
                    pg_insert(kb_condition_cards).values(**payload).on_conflict_do_nothing(
                        index_elements=[kb_condition_cards.c.card_id, kb_condition_cards.c.version]
                    )
                )
                row = await self._one(
                    connection,
                    select(kb_condition_cards).where(
                        kb_condition_cards.c.card_id == card.card_id,
                        kb_condition_cards.c.version == card.version,
                    ),
                )
                if row is None:  # pragma: no cover - insert/select share a transaction
                    raise RuntimeError(f"condition card was not stored: {card.card_id}@{card.version}")
                stored = _condition_card_from_row(row)
                if stored != card:
                    raise ValueError(f"condition card identity collision for {card.card_id}@{card.version}")
        return len(values)

    async def condition_cards(
        self,
        snapshot_id: str,
        *,
        review_status: str | None = "VERIFIED",
    ) -> tuple[ConditionCard, ...]:
        async with self.engine.connect() as connection:
            statement = select(kb_condition_cards).where(kb_condition_cards.c.snapshot_id == snapshot_id)
            if review_status is not None:
                statement = statement.where(kb_condition_cards.c.review_status == review_status)
            result = await connection.execute(
                statement.order_by(kb_condition_cards.c.card_id, kb_condition_cards.c.version)
            )
            return tuple(_condition_card_from_row(row) for row in result.mappings())

    async def get_condition_card(
        self,
        card_id: str,
        version: str,
        *,
        snapshot_id: str | None = None,
    ) -> ConditionCard | None:
        async with self.engine.connect() as connection:
            statement = select(kb_condition_cards).where(
                kb_condition_cards.c.card_id == card_id,
                kb_condition_cards.c.version == version,
            )
            if snapshot_id is not None:
                statement = statement.where(kb_condition_cards.c.snapshot_id == snapshot_id)
            row = await self._one(connection, statement)
            return _condition_card_from_row(row) if row is not None else None

    async def snapshot_counts(self, snapshot_id: str) -> tuple[int, int]:
        async with self.engine.connect() as connection:
            if await self._one(connection, select(kb_snapshots.c.snapshot_id).where(kb_snapshots.c.snapshot_id == snapshot_id)) is None:
                raise UnknownSnapshotError(snapshot_id)
            document_count_row = await self._one(
                connection,
                select(sa.func.count(sa.distinct(kb_document_versions.c.document_id)).label("count"))
                .join(kb_snapshot_document_versions, kb_snapshot_document_versions.c.document_version_id == kb_document_versions.c.document_version_id)
                .where(kb_snapshot_document_versions.c.snapshot_id == snapshot_id),
            )
            fragment_count_row = await self._one(
                connection,
                select(sa.func.count().label("count"))
                .select_from(kb_snapshot_fragments)
                .where(kb_snapshot_fragments.c.snapshot_id == snapshot_id),
            )
            return int(document_count_row.count), int(fragment_count_row.count)

    async def lexical_search(
        self,
        snapshot_id: str,
        query: str,
        exact_codes: Iterable[str] = (),
        *,
        limit: int = 30,
        allow_trigram: bool = True,
        use_fts: bool = True,
    ) -> tuple[dict[str, object], ...]:
        """Search only snapshot members using exact, FTS and weak trigram signals."""

        normalized_query = query.strip()
        safe_limit = max(1, min(int(limit), 100))
        codes = tuple(dict.fromkeys(code for code in exact_codes if code))
        exact_predicates = tuple(
            kb_fragments.c.text.ilike(f"%{_escape_like(code)}%", escape="\\")
            for code in codes
        )
        exact_score = sa.literal(0.0)
        if exact_predicates:
            exact_score = sum(
                (sa.case((predicate, 1.0), else_=0.0) for predicate in exact_predicates),
                sa.literal(0.0),
            )

        russian_config = sa.cast(sa.literal("russian"), REGCONFIG)
        simple_config = sa.cast(sa.literal("simple"), REGCONFIG)
        russian_vector = sa.func.to_tsvector(russian_config, kb_fragments.c.text)
        simple_vector = sa.func.to_tsvector(simple_config, kb_fragments.c.text)
        russian_query = sa.func.websearch_to_tsquery(russian_config, normalized_query)
        simple_query = sa.func.websearch_to_tsquery(simple_config, normalized_query)
        russian_match = russian_vector.op("@@")(russian_query)
        simple_match = simple_vector.op("@@")(simple_query)
        fts_score = sa.func.greatest(
            sa.func.ts_rank_cd(russian_vector, russian_query),
            sa.func.ts_rank_cd(simple_vector, simple_query),
        )
        trigram_score = sa.func.word_similarity(normalized_query, kb_fragments.c.text)
        channels = [*exact_predicates]
        if use_fts:
            channels.extend((russian_match, simple_match))
        if allow_trigram and normalized_query:
            channels.append(trigram_score >= TRIGRAM_MIN_SCORE)
        if not channels:
            return ()

        membership = kb_snapshot_fragments
        statement = (
            select(
                kb_fragments.c.fragment_id,
                kb_document_versions.c.document_id,
                kb_fragments.c.page_start,
                kb_fragments.c.source_anchor,
                kb_fragments.c.text,
                exact_score.label("exact_score"),
                fts_score.label("fts_score"),
                trigram_score.label("trigram_score"),
            )
            .join(membership, membership.c.fragment_id == kb_fragments.c.fragment_id)
            .join(kb_document_versions, kb_document_versions.c.document_version_id == kb_fragments.c.document_version_id)
            .where(membership.c.snapshot_id == snapshot_id)
            .where(sa.or_(*channels))
            .order_by(exact_score.desc(), fts_score.desc(), trigram_score.desc(), kb_fragments.c.fragment_id)
            .limit(safe_limit)
        )
        async with self.engine.connect() as connection:
            result = await connection.execute(statement)
            return tuple(result.mappings())

    async def store_embeddings(
        self,
        snapshot_id: str,
        embeddings: Mapping[str, Sequence[float]],
        *,
        model_id: str,
        model_revision: str,
        dimension: int = EMBEDDING_DIMENSION,
    ) -> int:
        """Insert or deterministically re-run embeddings for snapshot members.

        A fragment has one active embedding row.  Re-running with the same
        model metadata is idempotent; attempting to overwrite it with another
        revision is rejected so a mixed vector space cannot be searched by
        accident.
        """

        _validate_embedding_metadata(model_id, model_revision, dimension)
        values = {str(fragment_id): tuple(vector) for fragment_id, vector in embeddings.items()}
        for fragment_id, vector in values.items():
            _validate_vector(vector, dimension)
        async with self.engine.begin() as connection:
            snapshot_exists = await self._one(
                connection,
                select(kb_snapshots.c.snapshot_id).where(kb_snapshots.c.snapshot_id == snapshot_id),
            )
            if snapshot_exists is None:
                raise UnknownSnapshotError(snapshot_id)
            if values:
                member_rows = await connection.execute(
                    select(kb_snapshot_fragments.c.fragment_id).where(
                        kb_snapshot_fragments.c.snapshot_id == snapshot_id,
                        kb_snapshot_fragments.c.fragment_id.in_(tuple(values)),
                    )
                )
                members = {row.fragment_id for row in member_rows}
                missing = sorted(set(values) - members)
                if missing:
                    raise ValueError(f"embedding fragments are outside snapshot: {', '.join(missing[:3])}")
            existing_rows = await connection.execute(
                select(kb_fragment_embeddings).where(
                    kb_fragment_embeddings.c.fragment_id.in_(tuple(values))
                )
                if values
                else select(kb_fragment_embeddings).where(sa.false())
            )
            existing = {row.fragment_id: row for row in existing_rows.mappings()}
            for fragment_id, row in existing.items():
                metadata = (row.model_id, row.model_revision, int(row.dimension))
                requested = (model_id, model_revision, dimension)
                if metadata != requested:
                    raise EmbeddingRevisionMismatchError(
                        f"fragment {fragment_id} stores {metadata}, requested {requested}"
                    )
            now = datetime.now().astimezone()
            for fragment_id, vector in values.items():
                statement = sa.text(
                    """
                    INSERT INTO kb_fragment_embeddings
                        (fragment_id, model_id, model_revision, dimension, embedding, created_at)
                    VALUES
                        (:fragment_id, :model_id, :model_revision, :dimension,
                         CAST(:embedding AS vector), :created_at)
                    ON CONFLICT (fragment_id) DO UPDATE SET
                        embedding = EXCLUDED.embedding,
                        created_at = EXCLUDED.created_at
                    """
                )
                await connection.execute(
                    statement,
                    {
                        "fragment_id": fragment_id,
                        "model_id": model_id,
                        "model_revision": model_revision,
                        "dimension": dimension,
                        "embedding": _vector_literal(vector),
                        "created_at": now,
                    },
                )
        return len(values)

    async def embedding_stats(
        self,
        snapshot_id: str,
        *,
        model_id: str,
        model_revision: str,
        dimension: int = EMBEDDING_DIMENSION,
    ) -> dict[str, int | bool]:
        """Return count/dimension/identity checks for a snapshot embedding set."""

        _validate_embedding_metadata(model_id, model_revision, dimension)
        async with self.engine.connect() as connection:
            if await self._one(
                connection, select(kb_snapshots.c.snapshot_id).where(kb_snapshots.c.snapshot_id == snapshot_id)
            ) is None:
                raise UnknownSnapshotError(snapshot_id)
            row = await self._one(
                connection,
                sa.text(
                    """
                    SELECT
                        count(*) AS snapshot_fragments,
                        count(e.fragment_id) AS embeddings,
                        count(*) FILTER (WHERE e.dimension = :dimension) AS matching_dimension,
                        count(*) FILTER (
                            WHERE e.model_id = :model_id AND e.model_revision = :model_revision
                              AND e.dimension = :dimension
                        ) AS matching_metadata,
                        count(*) FILTER (WHERE e.embedding::text ILIKE '%NaN%') AS non_finite
                    FROM kb_snapshot_fragments m
                    LEFT JOIN kb_fragment_embeddings e ON e.fragment_id = m.fragment_id
                    WHERE m.snapshot_id = :snapshot_id
                    """
                ).bindparams(
                    snapshot_id=snapshot_id,
                    model_id=model_id,
                    model_revision=model_revision,
                    dimension=dimension,
                ),
            )
            if row is None:
                raise UnknownSnapshotError(snapshot_id)
            return {
                "snapshot_fragments": int(row.snapshot_fragments),
                "embeddings": int(row.embeddings),
                "matching_dimension": int(row.matching_dimension),
                "matching_metadata": int(row.matching_metadata),
                "non_finite": int(row.non_finite),
                "identity_preserved": int(row.embeddings) == int(row.snapshot_fragments),
            }

    async def dense_search(
        self,
        snapshot_id: str,
        query_embedding: Sequence[float],
        *,
        model_id: str,
        model_revision: str,
        dimension: int = EMBEDDING_DIMENSION,
        limit: int = 30,
    ) -> tuple[dict[str, object], ...]:
        """Exact cosine-distance scan constrained to one immutable snapshot."""

        _validate_embedding_metadata(model_id, model_revision, dimension)
        _validate_vector(query_embedding, dimension)
        safe_limit = max(1, min(int(limit), 100))
        async with self.engine.connect() as connection:
            if await self._one(
                connection, select(kb_snapshots.c.snapshot_id).where(kb_snapshots.c.snapshot_id == snapshot_id)
            ) is None:
                raise UnknownSnapshotError(snapshot_id)
            metadata_row = await self._one(
                connection,
                sa.text(
                    """
                    SELECT count(*) AS total,
                           count(*) FILTER (
                               WHERE e.model_id = :model_id
                                 AND e.model_revision = :model_revision
                                 AND e.dimension = :dimension
                           ) AS matching
                    FROM kb_snapshot_fragments m
                    JOIN kb_fragment_embeddings e ON e.fragment_id = m.fragment_id
                    WHERE m.snapshot_id = :snapshot_id
                    """
                ).bindparams(
                    snapshot_id=snapshot_id,
                    model_id=model_id,
                    model_revision=model_revision,
                    dimension=dimension,
                ),
            )
            if metadata_row is None:
                raise UnknownSnapshotError(snapshot_id)
            if int(metadata_row.total) == 0:
                raise EmbeddingUnavailableError(f"no embeddings for snapshot {snapshot_id}")
            if int(metadata_row.matching) != int(metadata_row.total):
                raise EmbeddingRevisionMismatchError(
                    f"snapshot {snapshot_id} contains embeddings from another model revision"
                )
            result = await connection.execute(
                sa.text(
                    """
                    SELECT f.fragment_id, v.document_id, f.page_start,
                           f.source_anchor, f.text,
                           1 - (e.embedding <=> CAST(:query_embedding AS vector)) AS dense_score
                    FROM kb_snapshot_fragments m
                    JOIN kb_fragments f ON f.fragment_id = m.fragment_id
                    JOIN kb_document_versions v ON v.document_version_id = f.document_version_id
                    JOIN kb_fragment_embeddings e ON e.fragment_id = f.fragment_id
                    WHERE m.snapshot_id = :snapshot_id
                      AND e.model_id = :model_id
                      AND e.model_revision = :model_revision
                      AND e.dimension = :dimension
                    ORDER BY dense_score DESC, f.fragment_id ASC
                    LIMIT :limit
                    """
                ).bindparams(
                    snapshot_id=snapshot_id,
                    model_id=model_id,
                    model_revision=model_revision,
                    dimension=dimension,
                    query_embedding=_vector_literal(query_embedding),
                    limit=safe_limit,
                )
            )
            return tuple(result.mappings())

    async def resolve_source(self, fragment_id: str, snapshot_id: str | None = None) -> SourceResolution:
        async with self.engine.connect() as connection:
            effective_snapshot_id = snapshot_id
            if effective_snapshot_id is None:
                current = await self._one(
                    connection,
                    select(kb_snapshots.c.snapshot_id)
                    .where(kb_snapshots.c.corpus == Corpus.NORMATIVE.value)
                    .order_by(kb_snapshots.c.created_at.desc(), kb_snapshots.c.snapshot_id.desc())
                    .limit(1),
                )
                if current is None:
                    raise UnknownSnapshotError("no current normative snapshot")
                effective_snapshot_id = current.snapshot_id
            elif await self._one(connection, select(kb_snapshots.c.snapshot_id).where(kb_snapshots.c.snapshot_id == effective_snapshot_id)) is None:
                raise UnknownSnapshotError(effective_snapshot_id)

            row = await self._one(
                connection,
                select(
                    kb_fragments,
                    kb_document_versions.c.document_id.label("version_document_id"),
                    kb_document_versions.c.content_sha256.label("version_content_sha256"),
                    kb_document_versions.c.declared_version,
                    kb_document_versions.c.declared_date,
                    kb_document_versions.c.page_count.label("version_page_count"),
                    kb_document_versions.c.source_reference,
                    kb_document_versions.c.ingested_at.label("version_ingested_at"),
                    kb_document_versions.c.review_status.label("version_review_status"),
                    kb_documents.c.original_filename,
                )
                .join(kb_snapshot_fragments, kb_snapshot_fragments.c.fragment_id == kb_fragments.c.fragment_id)
                .join(kb_document_versions, kb_document_versions.c.document_version_id == kb_fragments.c.document_version_id)
                .join(kb_documents, kb_documents.c.document_id == kb_document_versions.c.document_id)
                .where(
                    kb_snapshot_fragments.c.snapshot_id == effective_snapshot_id,
                    kb_fragments.c.fragment_id == fragment_id,
                ),
            )
            if row is None:
                raise UnknownFragmentError(fragment_id)
            fragment = _fragment_from_row(row)
            return SourceResolution(
                fragment_id=fragment.fragment_id,
                document_id=row.version_document_id,
                document_version_id=fragment.document_version_id,
                original_filename=row.original_filename,
                source_reference=row.source_reference,
                snapshot_id=effective_snapshot_id,
                page_start=fragment.page_start,
                page_end=fragment.page_end,
                section=fragment.section,
                kind=fragment.kind,
                heading_path=fragment.heading_path,
                text=fragment.text,
                source_anchor=fragment.source_anchor,
                review_status=fragment.review_status,
                declared_version=row.declared_version,
                declared_date=row.declared_date,
                page_count=row.version_page_count,
            )

    async def _fragments_for_version(self, connection: AsyncConnection, version_id: str) -> tuple[KnowledgeFragment, ...]:
        result = await connection.execute(
            select(kb_fragments)
            .where(kb_fragments.c.document_version_id == version_id)
            .order_by(kb_fragments.c.page_start, kb_fragments.c.page_end, kb_fragments.c.fragment_id)
        )
        return tuple(_fragment_from_row(row) for row in result.mappings())

    @staticmethod
    async def _version_ids_for_snapshot(connection: AsyncConnection, snapshot_id: str) -> tuple[str, ...]:
        result = await connection.execute(
            select(kb_snapshot_document_versions.c.document_version_id)
            .where(kb_snapshot_document_versions.c.snapshot_id == snapshot_id)
            .order_by(kb_snapshot_document_versions.c.document_version_id)
        )
        return tuple(row.document_version_id for row in result)

    @staticmethod
    async def _one(connection: AsyncConnection, statement: Select) -> object | None:
        result = await connection.execute(statement)
        return result.mappings().first()

    @staticmethod
    def _validate_document_identity(existing: object, document: Document) -> None:
        if existing["corpus"] != document.corpus.value or existing["original_filename"] != document.original_filename:
            raise ValueError(f"document identity collision for {document.document_id}")


def _document_from_row(row: object) -> Document:
    return Document(document_id=row["document_id"], original_filename=row["original_filename"], corpus=Corpus(row["corpus"]))


def _version_values(version: DocumentVersion) -> dict[str, object]:
    return {
        "document_version_id": version.document_version_id,
        "document_id": version.document_id,
        "content_sha256": version.content_sha256,
        "declared_version": version.declared_version,
        "declared_date": version.declared_date,
        "page_count": version.page_count,
        "source_reference": version.source_reference,
        "ingested_at": version.ingested_at,
        "review_status": version.review_status,
    }


def _version_from_row(row: object) -> DocumentVersion:
    return DocumentVersion(
        document_version_id=row["document_version_id"],
        document_id=row["document_id"],
        content_sha256=row["content_sha256"],
        declared_version=row["declared_version"],
        declared_date=row["declared_date"],
        page_count=row["page_count"],
        source_reference=row["source_reference"],
        ingested_at=row["ingested_at"],
        review_status=row["review_status"],
    )


def _same_version_facts(left: DocumentVersion, right: DocumentVersion) -> bool:
    """Compare rerunnable version identity without treating ingestion time as identity."""

    return (
        left.document_version_id == right.document_version_id
        and left.document_id == right.document_id
        and left.content_sha256 == right.content_sha256
        and left.declared_version == right.declared_version
        and left.declared_date == right.declared_date
        and left.page_count == right.page_count
        and left.source_reference == right.source_reference
        and left.review_status == right.review_status
    )


def _fragment_values(fragment: KnowledgeFragment) -> dict[str, object]:
    payload = fragment.model_dump(mode="json")
    payload.pop("snapshot_id", None)
    return payload


def _condition_card_values(card: ConditionCard) -> dict[str, object]:
    return {
        "card_id": card.card_id,
        "version": card.version,
        "snapshot_id": card.snapshot_id,
        "title": card.title,
        "applicability": card.applicability.model_dump(mode="json"),
        "required_slots": list(card.required_slots),
        "allowed_action": card.allowed_action,
        "forbidden_generalization": card.forbidden_generalization,
        "handoff_condition": card.handoff_condition,
        "risk_level": card.risk_level.value,
        "source_fragment_ids": list(card.source_fragment_ids),
        "source_quotes": list(card.source_quotes),
        "source_anchors": list(card.source_anchors),
        "review_status": card.review_status,
        "created_at": datetime.now().astimezone(),
    }


def _condition_card_from_row(row: object) -> ConditionCard:
    return ConditionCard(
        card_id=row["card_id"],
        version=row["version"],
        snapshot_id=row["snapshot_id"],
        title=row["title"],
        applicability=row["applicability"],
        required_slots=tuple(row["required_slots"] or ()),
        allowed_action=row["allowed_action"],
        forbidden_generalization=row["forbidden_generalization"],
        handoff_condition=row["handoff_condition"],
        risk_level=row["risk_level"],
        source_fragment_ids=tuple(row["source_fragment_ids"] or ()),
        source_quotes=tuple(row["source_quotes"] or ()),
        source_anchors=tuple(row["source_anchors"] or ()),
        review_status=row["review_status"],
    )


def _escape_like(value: str) -> str:
    """Escape user-controlled LIKE wildcards before building an exact predicate."""

    return value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


def _validate_embedding_metadata(model_id: str, model_revision: str, dimension: int) -> None:
    if not model_id or not model_revision:
        raise ValueError("embedding model_id and model_revision are required")
    if model_id != EMBEDDING_MODEL_ID or model_revision != EMBEDDING_REVISION:
        raise EmbeddingRevisionMismatchError(
            f"only active Giga model metadata is accepted: {(EMBEDDING_MODEL_ID, EMBEDDING_REVISION)}"
        )
    if int(dimension) != EMBEDDING_DIMENSION:
        raise ValueError(f"only {EMBEDDING_DIMENSION}-dimensional embeddings are supported")


def _validate_vector(vector: Sequence[float], dimension: int = EMBEDDING_DIMENSION) -> None:
    import math

    if len(vector) != int(dimension):
        raise ValueError(f"embedding has dimension {len(vector)}; expected {dimension}")
    if any(not math.isfinite(float(value)) for value in vector):
        raise ValueError("embedding contains a non-finite value")


def _vector_literal(vector: Sequence[float]) -> str:
    _validate_vector(vector, len(vector))
    return "[" + ",".join(format(float(value), ".9g") for value in vector) + "]"


def _fragment_from_row(row: object) -> KnowledgeFragment:
    return KnowledgeFragment(
        fragment_id=row["fragment_id"],
        document_version_id=row["document_version_id"],
        snapshot_id=None,
        page_start=row["page_start"],
        page_end=row["page_end"],
        section=row["section"],
        kind=row["kind"],
        heading_path=tuple(row["heading_path"] or ()),
        text=row["text"],
        source_anchor=SourceAnchor.model_validate(row["source_anchor"]),
        actor_roles=tuple(row["actor_roles"]) if row["actor_roles"] is not None else None,
        process=row["process"],
        provider=row["provider"],
        document_status=row["document_status"],
        error_codes=tuple(row["error_codes"]) if row["error_codes"] is not None else None,
        review_status=row["review_status"],
        text_hash=row["text_hash"],
    )


def _run_values(run: IngestionRun) -> dict[str, object]:
    return {
        "run_id": run.run_id,
        "status": run.status,
        "source_count": run.source_count,
        "created_at": run.started_at,
        "corpus": run.corpus.value,
        "document_version_ids": list(run.document_version_ids),
        "completed_at": run.completed_at,
        "warnings": list(run.warnings),
        "error": run.error,
    }


def _run_from_row(row: object) -> IngestionRun:
    return IngestionRun(
        run_id=row["run_id"],
        corpus=Corpus(row["corpus"]),
        status=row["status"],
        source_count=row["source_count"],
        document_version_ids=tuple(row["document_version_ids"] or ()),
        started_at=row["created_at"],
        completed_at=row["completed_at"],
        warnings=tuple(row["warnings"] or ()),
        error=row["error"],
    )


def _snapshot_from_row(row: object, document_version_ids: Iterable[str]) -> KnowledgeSnapshot:
    return KnowledgeSnapshot(
        snapshot_id=row["snapshot_id"],
        corpus=Corpus(row["corpus"]),
        document_version_ids=tuple(document_version_ids),
        manifest_hash=row["manifest_hash"],
        created_at=row["created_at"],
        review_status=row["review_status"],
    )
