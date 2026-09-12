"""Pinned Hugging Face revisions selected during the K0C review.

Revision pins keep an explicit inference run reproducible.  Operators may
override them through the inference settings when deliberately upgrading a
model.
"""

EMBEDDING_MODEL_ID = "Qwen/Qwen3-Embedding-0.6B"
EMBEDDING_REVISION = "97b0c614be4d77ee51c0cef4e5f07c00f9eb65b3"

RERANKER_MODEL_ID = "BAAI/bge-reranker-v2-m3"
RERANKER_REVISION = "953dc6f6f85a1b2dbfca4c34a2796e7dde08d41e"

GENERATOR_MODEL_ID = "Qwen/Qwen3-4B-Instruct-2507"
GENERATOR_REVISION = "cdbee75f17c01a7cc42f958dc650907174af0554"
