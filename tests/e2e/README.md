# Cross-runtime smoke tests

This directory is the home for Browser/API/Knowledge/worker smoke scenarios against the Compose environment. It is no longer described as waiting for a future scaffold: the runtime boundaries and persistence now exist, while the executable E2E suite is still expected to grow toward the critical scenarios listed in `../../docs/quality.md §11`.

Do not duplicate decision logic in test code. E2E assertions should observe public state/events and real boundary behavior; deterministic Knowledge fixtures are acceptable only when the test is explicitly a decision/contract fixture rather than a full real-model run.
