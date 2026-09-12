from tenderhack_knowledge.understanding import understand_query, normalize_query


def test_normalization_is_deterministic_and_conservative() -> None:
    original = "  \u041d\u0435 \u043e\u0442\u043f\u0440\u0430\u0432\u043b\u044f\u0442\u044c \u0423\u041f\u0414-0007\u00a0 \u0431\u0435\u0437 \u041c\u0427\u0414!  "
    result = understand_query(original)

    assert result.original_query == original
    assert result.normalized_query == "\u043d\u0435 \u043e\u0442\u043f\u0440\u0430\u0432\u043b\u044f\u0442\u044c \u0443\u043f\u0434-0007 \u0431\u0435\u0437 \u043c\u0447\u0434!"
    assert result.exact_codes == ("\u0423\u041f\u0414-0007", "\u041c\u0427\u0414")
    assert result.entities[0].provenance == "ORIGINAL"


def test_exact_extraction_preserves_leading_zero_and_negation() -> None:
    result = understand_query("\u043a\u043e\u0434 00017 \u043d\u0435 \u0443\u0434\u0430\u043b\u044f\u0442\u044c")

    assert "00017" in result.exact_codes
    assert "\u043d\u0435 \u0443\u0434\u0430\u043b\u044f\u0442\u044c" in result.normalized_query


def test_normalization_maps_yo_only_for_search() -> None:
    assert normalize_query("\u0415\u0441\u0442\u044c \u0451\u043b\u043a\u0430") == "\u0435\u0441\u0442\u044c \u0435\u043b\u043a\u0430"


def test_duration_and_status_entities_are_deterministic() -> None:
    result = understand_query("\u043e\u0431\u0440\u0430\u0431\u043e\u0442\u043a\u0430 \u0432 \u043e\u0447\u0435\u0440\u0435\u0434\u0438 15 \u043c\u0438\u043d\u0443\u0442")

    assert any(entity.type == "status" for entity in result.entities)
    assert any(entity.type == "duration" for entity in result.entities)
