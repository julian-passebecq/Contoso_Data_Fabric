from __future__ import annotations

from dataclasses import asdict
from typing import Any

from .scenarios import SCENARIOS
from .spec import ProjectSpec, STAGES

SCALE_TO_ORDERS = {
    "tiny": 10_000,
    "small": 100_000,
    "medium": 500_000,
    "large": 2_000_000,
}
FORMAT_TO_LEGACY = {"parquet": "PARQUET", "csv": "CSV", "delta": "DELTATABLE"}


def stage_slice(stop_after: str) -> list[str]:
    return list(STAGES[: STAGES.index(stop_after) + 1])


def build_plan(spec: ProjectSpec, stop_after: str | None = None) -> dict[str, Any]:
    target = stop_after or spec.architecture.stop_after
    if target not in STAGES:
        raise ValueError(f"Unknown stage: {target}")

    scenario = SCENARIOS[spec.business.scenario]
    stages = stage_slice(target)
    unsupported = [s for s in stages if s in {"semantic", "report", "run"}]

    artifacts: list[str] = ["data/", "truth_manifest.json", "plan.json"]
    if "bronze" in stages:
        artifacts += [f"fabric/{spec.fabric.bronze_lakehouse}.Lakehouse/", "fabric/Bronze_Load.Notebook/"]
    if "silver" in stages:
        artifacts += [f"fabric/{spec.fabric.silver_lakehouse}.Lakehouse/", "fabric/Silver_Transform.Notebook/"]
    if "gold" in stages:
        artifacts += [f"fabric/{spec.fabric.gold_lakehouse}.Lakehouse/", "fabric/Gold_Build.Notebook/"]

    warnings: list[str] = []
    if spec.project.seed != 0:
        warnings.append("The legacy C# generator currently fixes Random(0); requested seed is recorded but effective generation seed is 0.")
    if unsupported:
        warnings.append("V1 can plan semantic/report/run stages, but only generate/bronze/silver/gold item compilation and deployment are implemented.")

    return {
        "version": "1.0",
        "project": spec.project.name,
        "scenario": asdict(scenario),
        "requestedStopAfter": target,
        "stages": stages,
        "implementedThrough": "deploy",
        "unsupportedRequestedStages": unsupported,
        "generation": {
            "ordersCount": SCALE_TO_ORDERS[spec.business.scale],
            "years": spec.business.years,
            "requestedSeed": spec.project.seed,
            "effectiveSeed": 0,
            "outputFormat": FORMAT_TO_LEGACY[spec.landing.format],
            "landingFormat": spec.landing.format,
        },
        "fabric": {
            "workspace": asdict(spec.fabric.workspace),
            "environment": spec.fabric.environment,
            "lakehouses": {
                "bronze": spec.fabric.bronze_lakehouse,
                "silver": spec.fabric.silver_lakehouse,
                "gold": spec.fabric.gold_lakehouse,
            },
        },
        "artifacts": artifacts,
        "warnings": warnings,
    }
