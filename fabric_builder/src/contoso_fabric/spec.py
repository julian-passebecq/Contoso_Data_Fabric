from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

STAGES = ("generate", "bronze", "silver", "gold", "semantic", "report", "deploy", "run")
IMPLEMENTED_SCENARIOS = {"sales-bi"}
KNOWN_SCENARIOS = {"sales-bi", "customer-experience", "data-quality", "ml-dissatisfaction"}
KNOWN_FORMATS = {"parquet", "csv", "delta"}
KNOWN_SCALES = {"tiny", "small", "medium", "large"}


class SpecError(ValueError):
    pass


def _mapping(value: Any, name: str) -> dict[str, Any]:
    if value is None:
        return {}
    if not isinstance(value, dict):
        raise SpecError(f"{name} must be an object")
    return value


@dataclass(frozen=True)
class Project:
    name: str = "contoso-fabric-sales"
    seed: int = 0


@dataclass(frozen=True)
class Business:
    scenario: str = "sales-bi"
    scale: str = "small"
    years: int = 3
    features: dict[str, bool] = field(default_factory=dict)


@dataclass(frozen=True)
class Landing:
    format: str = "parquet"
    compression: str = "snappy"
    bronze_mode: str = "files"


@dataclass(frozen=True)
class Architecture:
    profile: str = "medallion"
    stop_after: str = "gold"


@dataclass(frozen=True)
class FabricWorkspace:
    mode: str = "existing"
    name: str | None = None
    id: str | None = None


@dataclass(frozen=True)
class Fabric:
    workspace: FabricWorkspace = field(default_factory=FabricWorkspace)
    environment: str = "DEV"
    bronze_lakehouse: str = "Contoso_Bronze"
    silver_lakehouse: str = "Contoso_Silver"
    gold_lakehouse: str = "Contoso_Gold"


@dataclass(frozen=True)
class ProjectSpec:
    version: str = "1.0"
    project: Project = field(default_factory=Project)
    business: Business = field(default_factory=Business)
    landing: Landing = field(default_factory=Landing)
    architecture: Architecture = field(default_factory=Architecture)
    fabric: Fabric = field(default_factory=Fabric)

    @staticmethod
    def from_dict(raw: dict[str, Any]) -> "ProjectSpec":
        project_raw = _mapping(raw.get("project"), "project")
        business_raw = _mapping(raw.get("business"), "business")
        data_raw = _mapping(raw.get("data"), "data")
        landing_raw = _mapping(data_raw.get("landing"), "data.landing")
        arch_raw = _mapping(raw.get("architecture"), "architecture")
        fabric_raw = _mapping(raw.get("fabric"), "fabric")
        ws_raw = _mapping(fabric_raw.get("workspace"), "fabric.workspace")

        spec = ProjectSpec(
            version=str(raw.get("version", "1.0")),
            project=Project(
                name=str(project_raw.get("name", "contoso-fabric-sales")),
                seed=int(project_raw.get("seed", 0)),
            ),
            business=Business(
                scenario=str(business_raw.get("scenario", "sales-bi")),
                scale=str(business_raw.get("scale", "small")),
                years=int(business_raw.get("years", 3)),
                features={str(k): bool(v) for k, v in _mapping(business_raw.get("features"), "business.features").items()},
            ),
            landing=Landing(
                format=str(landing_raw.get("format", "parquet")),
                compression=str(landing_raw.get("compression", "snappy")),
                bronze_mode=str(landing_raw.get("bronzeMode", "files")),
            ),
            architecture=Architecture(
                profile=str(arch_raw.get("profile", "medallion")),
                stop_after=str(arch_raw.get("stopAfter", "gold")),
            ),
            fabric=Fabric(
                workspace=FabricWorkspace(
                    mode=str(ws_raw.get("mode", "existing")),
                    name=ws_raw.get("name"),
                    id=ws_raw.get("id"),
                ),
                environment=str(fabric_raw.get("environment", "DEV")),
                bronze_lakehouse=str(fabric_raw.get("bronzeLakehouse", "Contoso_Bronze")),
                silver_lakehouse=str(fabric_raw.get("silverLakehouse", "Contoso_Silver")),
                gold_lakehouse=str(fabric_raw.get("goldLakehouse", "Contoso_Gold")),
            ),
        )
        spec.validate()
        return spec

    def validate(self) -> None:
        if self.business.scenario not in KNOWN_SCENARIOS:
            raise SpecError(f"Unknown business scenario: {self.business.scenario}")
        if self.business.scenario not in IMPLEMENTED_SCENARIOS:
            raise SpecError(
                f"Scenario '{self.business.scenario}' is catalogued but not implemented in V1; use 'sales-bi'."
            )
        if self.business.scale not in KNOWN_SCALES:
            raise SpecError(f"scale must be one of {sorted(KNOWN_SCALES)}")
        if not 1 <= self.business.years <= 20:
            raise SpecError("business.years must be between 1 and 20")
        if self.landing.format not in KNOWN_FORMATS:
            raise SpecError(f"landing format must be one of {sorted(KNOWN_FORMATS)}")
        if self.architecture.stop_after not in STAGES:
            raise SpecError(f"stopAfter must be one of {list(STAGES)}")
        if self.fabric.workspace.mode not in {"existing", "create"}:
            raise SpecError("fabric.workspace.mode must be 'existing' or 'create'")


def load_project_spec(path: str | Path) -> ProjectSpec:
    p = Path(path)
    return ProjectSpec.from_dict(json.loads(p.read_text(encoding="utf-8")))
