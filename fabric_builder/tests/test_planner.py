from contoso_fabric.planner import build_plan
from contoso_fabric.spec import ProjectSpec, SpecError


def spec(stop_after="gold", scale="small", seed=0):
    return ProjectSpec.from_dict({"project": {"name": "x", "seed": seed}, "business": {"scenario": "sales-bi", "scale": scale, "years": 3}, "data": {"landing": {"format": "parquet"}}, "architecture": {"stopAfter": stop_after}, "fabric": {"workspace": {"mode": "existing", "name": "ws"}}})


def test_partial_pipeline_stops_exactly_at_bronze():
    plan = build_plan(spec("bronze"))
    assert plan["stages"] == ["generate", "bronze"]
    assert not any("Silver" in x for x in plan["artifacts"])


def test_medium_maps_to_legacy_order_count():
    assert build_plan(spec(scale="medium"))["generation"]["ordersCount"] == 500_000


def test_nonzero_seed_is_explicitly_warned():
    plan = build_plan(spec(seed=42))
    assert plan["generation"]["effectiveSeed"] == 0
    assert plan["warnings"]


def test_future_scenario_is_rejected_in_v1():
    try:
        ProjectSpec.from_dict({"business": {"scenario": "customer-experience"}})
    except SpecError as exc:
        assert "not implemented" in str(exc)
    else:
        raise AssertionError("expected SpecError")
