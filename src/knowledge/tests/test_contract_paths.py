from pathlib import Path

import yaml

from tenderhack_knowledge.main import app


def test_frozen_knowledge_contract_paths_and_methods_are_exposed() -> None:
    contract_path = Path(__file__).resolve().parents[3] / "docs" / "contracts" / "knowledge-v0.openapi.yaml"
    frozen_paths = yaml.safe_load(contract_path.read_text(encoding="utf-8"))["paths"]
    exposed = {
        (path, method.lower())
        for path, operations in app.openapi()["paths"].items()
        for method in operations
    }

    for path, operations in frozen_paths.items():
        for method in operations:
            assert (path, method.lower()) in exposed, f"missing frozen route: {method.upper()} {path}"
