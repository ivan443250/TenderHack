"""Pinned model references for the Model Stack V2 runtime.

The legacy constants remain available to the historical K0/K2 adapters and
reports.  Active retrieval/generation code uses the Giga/Querit/Qwen3.8
references below; changing them is an explicit model migration.
"""

EMBEDDING_MODEL_ID = "ai-sage/Giga-Embeddings-instruct-3B-0826"
EMBEDDING_REVISION = "ed7db5c91b900b39381b27b6e9c0a3d31137cd29"
EMBEDDING_DIMENSION = 2048
EMBEDDING_RUNTIME_REPO = "ai-babai/giga-embeddings-0826-3b-gguf"
EMBEDDING_RUNTIME_FILE = "giga-embeddings-0826-3b-q8_0.gguf"
EMBEDDING_RUNTIME_REVISION = "04c5a2d751ce20908200de2324444a14a81b1d80"
EMBEDDING_RUNTIME_SHA256 = "429f2d04a968ffe73137fe65c2e458a08236056168b905d208b4d81ecab08c22"

RERANKER_MODEL_ID = "Querit/Querit-4B"
RERANKER_REVISION = "e557bb5ce6555f16b33dbb7d2ca62e8d27c14c13"

GENERATOR_MODEL_ID = "empero-ai/Qwen3.8-4B-Distill"
GENERATOR_REVISION = "c83cb7aa2999d2f35c43e9ae0634a30eb8985a1e"
GENERATOR_RUNTIME_REPO = "empero-ai/Qwen3.8-4B-Distill-GGUF"
GENERATOR_RUNTIME_FILE = "Qwen3.8-4B-Q6_K.gguf"
GENERATOR_RUNTIME_REVISION = "391fc7d103e3942a408def3e4f51c2f85d464417"

# Kept for historical adapters and benchmark interpretation.  They are not
# accepted by the active 2048-dimensional persistence path.
LEGACY_EMBEDDING_MODEL_ID = "Qwen/Qwen3-Embedding-0.6B"
LEGACY_EMBEDDING_REVISION = "97b0c614be4d77ee51c0cef4e5f07c00f9eb65b3"
LEGACY_RERANKER_MODEL_ID = "BAAI/bge-reranker-v2-m3"
LEGACY_RERANKER_REVISION = "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e"
LEGACY_GENERATOR_MODEL_ID = "Qwen/Qwen3-4B-Instruct-2507"
LEGACY_GENERATOR_REVISION = "cdbee75f17c01a7cc42f958dc650907174af0554"
