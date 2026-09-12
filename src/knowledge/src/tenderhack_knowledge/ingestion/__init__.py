"""Source ingestion and document provenance boundary."""

from .chunker import SemanticFragmentBuilder
from .extractor import ExtractionError, PdfPlumberExtractor
from .models import (
    Corpus,
    Document,
    DocumentVersion,
    ExtractedTable,
    ExtractionResult,
    IngestionResult,
    IngestionRun,
    KnowledgeFragment,
    KnowledgeSnapshot,
    PageExtraction,
    PreparedIngestion,
    SourceAnchor,
    SourceResolution,
    TextBlock,
)
from .pipeline import IngestionPipeline
from .repository import (
    CorpusBoundaryError,
    InMemoryKnowledgeRepository,
    SourceRepository,
    SourceResolutionError,
    SourceResolver,
    UnknownFragmentError,
    UnknownSnapshotError,
)

__all__ = [
    "Corpus",
    "BootstrapError",
    "BootstrapResult",
    "CorpusExpectation",
    "CorpusIdentityMismatch",
    "EXPECTED_NORMATIVE_CORPUS",
    "CorpusBoundaryError",
    "Document",
    "DocumentVersion",
    "ExtractionError",
    "ExtractedTable",
    "ExtractionResult",
    "InMemoryKnowledgeRepository",
    "IngestionPipeline",
    "IngestionResult",
    "IngestionRun",
    "InvalidManifest",
    "KnowledgeFragment",
    "KnowledgeSnapshot",
    "MissingNormativeArtifact",
    "PageExtraction",
    "PreparedIngestion",
    "PdfPlumberExtractor",
    "SemanticFragmentBuilder",
    "SourceAnchor",
    "SourceRepository",
    "SourceResolution",
    "SourceResolutionError",
    "SourceResolver",
    "TextBlock",
    "bootstrap_in_memory",
    "bootstrap_postgres",
    "load_manifest",
    "UnknownFragmentError",
    "UnknownSnapshotError",
]


_BOOTSTRAP_EXPORTS = {
    "BootstrapError",
    "BootstrapResult",
    "CorpusExpectation",
    "CorpusIdentityMismatch",
    "EXPECTED_NORMATIVE_CORPUS",
    "InvalidManifest",
    "MissingNormativeArtifact",
    "bootstrap_in_memory",
    "bootstrap_postgres",
    "load_manifest",
}


def __getattr__(name: str):
    """Load bootstrap helpers lazily to keep conditions imports acyclic."""

    if name in _BOOTSTRAP_EXPORTS:
        from importlib import import_module

        value = getattr(import_module(".bootstrap", __name__), name)
        globals()[name] = value
        return value
    raise AttributeError(name)
