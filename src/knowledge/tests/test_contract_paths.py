from pathlib import Path
from typing import Any

import yaml

from tenderhack_knowledge.main import app


CONTRACT_PATH = Path(__file__).resolve().parents[3] / "docs" / "contracts" / "knowledge-v0.openapi.yaml"


def _resolve(schema: dict[str, Any], spec: dict[str, Any]) -> dict[str, Any]:
    while "$ref" in schema:
        reference = schema["$ref"]
        assert reference.startswith("#/components/")
        _, _, path = reference.partition("#/components/")
        value: Any = spec["components"]
        for part in path.split("/"):
            value = value[part]
        schema = value
    return schema


def _shape(schema: dict[str, Any], spec: dict[str, Any]) -> tuple[dict[str, Any], bool]:
    schema = _resolve(schema, spec)
    nullable = bool(schema.get("nullable", False))

    if "allOf" in schema and len(schema["allOf"]) == 1:
        inner, inner_nullable = _shape(schema["allOf"][0], spec)
        return inner, nullable or inner_nullable

    alternatives = schema.get("anyOf") or schema.get("oneOf")
    if alternatives:
        non_null = [item for item in alternatives if item.get("type") != "null"]
        has_null = len(non_null) != len(alternatives)
        if len(non_null) == 1:
            inner, inner_nullable = _shape(non_null[0], spec)
            return inner, nullable or has_null or inner_nullable

    return schema, nullable


def _assert_schema(
    expected: dict[str, Any], actual: dict[str, Any], expected_spec: dict[str, Any], actual_spec: dict[str, Any], path: str
) -> None:
    expected, expected_nullable = _shape(expected, expected_spec)
    actual, actual_nullable = _shape(actual, actual_spec)

    assert expected_nullable == actual_nullable, f"nullable drift at {path}"

    expected_type = expected.get("type")
    actual_type = actual.get("type")
    assert expected_type == actual_type, f"type drift at {path}: {expected_type!r} != {actual_type!r}"

    if "enum" in expected:
        actual_enum = actual.get("enum")
        if actual_enum is None and "const" in actual:
            actual_enum = [actual["const"]]
        assert actual_enum == expected["enum"], f"enum drift at {path}"
    if "format" in expected:
        assert actual.get("format") == expected["format"], f"format drift at {path}"
    if "default" in expected:
        assert actual.get("default") == expected["default"], f"default drift at {path}"

    if expected_type == "object":
        assert set(actual.get("required", [])) == set(expected.get("required", [])), f"required drift at {path}"
        expected_properties = expected.get("properties", {})
        actual_properties = actual.get("properties", {})
        assert set(actual_properties) == set(expected_properties), f"property drift at {path}"
        for name, expected_property in expected_properties.items():
            _assert_schema(expected_property, actual_properties[name], expected_spec, actual_spec, f"{path}.{name}")
        if "additionalProperties" in expected:
            assert actual.get("additionalProperties") == expected["additionalProperties"], f"additionalProperties drift at {path}"
    elif expected_type == "array":
        _assert_schema(expected["items"], actual["items"], expected_spec, actual_spec, f"{path}[]")


def _response_schema(response: dict[str, Any], spec: dict[str, Any]) -> dict[str, Any]:
    response = _resolve(response, spec)
    return response["content"]["application/json"]["schema"]


def test_frozen_knowledge_contract_semantic_parity() -> None:
    frozen = yaml.safe_load(CONTRACT_PATH.read_text(encoding="utf-8"))
    generated = app.openapi()
    generated_paths = generated["paths"]

    for path, frozen_operations in frozen["paths"].items():
        assert path in generated_paths, f"missing frozen path: {path}"
        for method, frozen_operation in frozen_operations.items():
            assert method in generated_paths[path], f"missing frozen route: {method.upper()} {path}"
            operation = generated_paths[path][method]

            frozen_parameters = frozen_operation.get("parameters", [])
            generated_parameters = {(item["in"], item["name"]): item for item in operation.get("parameters", [])}
            for parameter in frozen_parameters:
                parameter = _resolve(parameter, frozen)
                key = (parameter["in"], parameter["name"])
                assert key in generated_parameters, f"missing parameter {parameter['name']} at {method.upper()} {path}"
                generated_parameter = generated_parameters[key]
                assert generated_parameter["required"] == parameter["required"], f"required parameter drift at {path}"
                _assert_schema(parameter["schema"], generated_parameter["schema"], frozen, generated, f"{path}:{parameter['name']}")

            if "requestBody" in frozen_operation:
                expected_body = frozen_operation["requestBody"]
                actual_body = operation.get("requestBody")
                assert actual_body is not None and actual_body["required"] == expected_body["required"]
                _assert_schema(
                    expected_body["content"]["application/json"]["schema"],
                    actual_body["content"]["application/json"]["schema"],
                    frozen,
                    generated,
                    f"{method.upper()} {path} request",
                )

            generated_responses = operation.get("responses", {})
            for status, expected_response in frozen_operation["responses"].items():
                assert status in generated_responses, f"missing response {status} at {method.upper()} {path}"
                _assert_schema(
                    _response_schema(expected_response, frozen),
                    _response_schema(generated_responses[status], generated),
                    frozen,
                    generated,
                    f"{method.upper()} {path} response {status}",
                )
