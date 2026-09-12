# Controlled inference runtime

Model artifacts are mounted locally and are not committed. Compose keeps the `inference` service behind the `inference` profile; vLLM is the target runtime and llama.cpp is the documented fallback, not a second mandatory service. No external LLM API is used.
