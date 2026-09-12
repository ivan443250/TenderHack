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
    "KnowledgeFragment",
    "KnowledgeSnapshot",
    "PageExtraction",
    "PdfPlumberExtractor",
    "SemanticFragmentBuilder",
    "SourceAnchor",
    "SourceRepository",
    "SourceResolution",
    "SourceResolutionError",
    "SourceResolver",
    "TextBlock",
    "UnknownFragmentError",
    "UnknownSnapshotError",
]
