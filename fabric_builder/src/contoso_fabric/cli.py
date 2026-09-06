from __future__ import annotations

import argparse
import json
from pathlib import Path

from .bundle import compile_bundle
from .deploy import publish_fabric_items, upload_bronze
from .generator import generator_command, run_generator
from .planner import build_plan
from .spec import STAGES, load_project_spec


def _print(payload) -> None:
    print(json.dumps(payload, indent=2, default=str))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="contoso-fabric", description="Contoso data + Microsoft Fabric pipeline builder")
    parser.add_argument("--project", default="project.json", help="Path to project.json")
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("plan", help="Validate the project and print the derived pipeline plan")
    p.add_argument("--stop-after", choices=STAGES)
    c = sub.add_parser("compile", help="Generate Fabric item folders and deployment manifests")
    c.add_argument("--output", default="generated")
    c.add_argument("--stop-after", choices=STAGES)
    g = sub.add_parser("generate", help="Run the existing C# Contoso generator")
    g.add_argument("--repo-root", default=".")
    g.add_argument("--output", default="generated/data")
    g.add_argument("--cache", default="generated/cache")
    g.add_argument("--dry-run", action="store_true")
    u = sub.add_parser("upload-bronze", help="Upload generated raw files to OneLake Bronze Files/raw")
    u.add_argument("--data", default="generated/data")
    d = sub.add_parser("deploy-items", help="Publish generated Lakehouse/Notebook items with fabric-cicd")
    d.add_argument("--fabric-dir", default="generated/fabric")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    spec = load_project_spec(args.project)
    if args.command == "plan":
        _print(build_plan(spec, args.stop_after))
    elif args.command == "compile":
        _print(compile_bundle(spec, args.output, args.stop_after))
    elif args.command == "generate":
        repo = Path(args.repo_root).resolve()
        if args.dry_run:
            _print({"command": generator_command(spec, repo, Path(args.output).resolve(), Path(args.cache).resolve())})
        else:
            _print({"status": "ok", "command": list(run_generator(spec, repo, args.output, args.cache))})
    elif args.command == "upload-bronze":
        _print({"uploaded": upload_bronze(spec, args.data)})
    elif args.command == "deploy-items":
        publish_fabric_items(spec, args.fabric_dir)
        _print({"status": "published", "types": ["Lakehouse", "Notebook"]})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
