from __future__ import annotations

import shutil
import subprocess
from pathlib import Path
from typing import Sequence

from .planner import build_plan
from .spec import ProjectSpec


class GeneratorError(RuntimeError):
    pass


def generator_command(spec: ProjectSpec, repo_root: Path, output_dir: Path, cache_dir: Path) -> list[str]:
    plan = build_plan(spec, "generate")
    config = repo_root / "_test_data" / "IN" / "config_test.json"
    data = repo_root / "_test_data" / "IN" / "data_test.xlsx"
    project = repo_root / "DatabaseGenerator" / "DatabaseGenerator.csproj"

    for required in (config, data, project):
        if not required.exists():
            raise GeneratorError(f"Required legacy generator input is missing: {required}")

    return [
        "dotnet", "run", "--project", str(project), "--",
        str(config), str(data), str(output_dir), str(cache_dir),
        f"param:OrdersCount={plan['generation']['ordersCount']}",
        f"param:OutputFormat={plan['generation']['outputFormat']}",
        "param:StartDT=2014-01-01",
        f"param:YearsCount={spec.business.years}",
    ]


def run_generator(spec: ProjectSpec, repo_root: str | Path, output_dir: str | Path, cache_dir: str | Path) -> Sequence[str]:
    if not shutil.which("dotnet"):
        raise GeneratorError("dotnet was not found on PATH. Install .NET SDK 8+ before running the legacy generator.")
    repo = Path(repo_root).resolve()
    out = Path(output_dir).resolve()
    cache = Path(cache_dir).resolve()
    out.mkdir(parents=True, exist_ok=True)
    cache.mkdir(parents=True, exist_ok=True)
    cmd = generator_command(spec, repo, out, cache)
    subprocess.run(cmd, check=True)
    return cmd
