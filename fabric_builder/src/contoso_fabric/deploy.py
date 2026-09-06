from __future__ import annotations

from pathlib import Path

from .scenarios import SCENARIOS
from .spec import ProjectSpec


class DeploymentError(RuntimeError):
    pass


def _require_target(spec: ProjectSpec) -> tuple[str | None, str | None]:
    name = spec.fabric.workspace.name
    workspace_id = spec.fabric.workspace.id
    if not (name or workspace_id):
        raise DeploymentError("Live Fabric deployment needs fabric.workspace.name or fabric.workspace.id in project.json")
    return name, workspace_id


def upload_bronze(spec: ProjectSpec, data_dir: str | Path) -> list[str]:
    """Upload generated raw files to the Bronze Lakehouse Files/raw path using Azure CLI authentication."""
    try:
        from azure.core.exceptions import ResourceExistsError
        from azure.identity import AzureCliCredential
        from azure.storage.filedatalake import DataLakeServiceClient
    except ImportError as exc:
        raise DeploymentError("Install Fabric extras first: pip install -e './fabric_builder[fabric]'") from exc

    workspace_name, _ = _require_target(spec)
    if workspace_name is None:
        raise DeploymentError("Bronze upload V1 currently requires fabric.workspace.name; workspace ID support is next.")

    data_path = Path(data_dir).resolve()
    if not data_path.exists():
        raise DeploymentError(f"Generated data directory does not exist: {data_path}")

    service = DataLakeServiceClient(account_url="https://onelake.dfs.fabric.microsoft.com", credential=AzureCliCredential())
    fs = service.get_file_system_client(workspace_name)
    directory = fs.get_directory_client(f"{spec.fabric.bronze_lakehouse}.Lakehouse/Files/raw")
    try:
        directory.create_directory()
    except ResourceExistsError:
        pass

    uploaded: list[str] = []
    expected = set(SCENARIOS[spec.business.scenario].tables)
    for local in sorted(data_path.iterdir()):
        if not local.is_file() or local.stem.lower() not in expected:
            continue
        file_client = directory.get_file_client(local.name)
        with local.open("rb") as handle:
            file_client.upload_data(handle, overwrite=True)
        uploaded.append(local.name)
    if not uploaded:
        raise DeploymentError(f"No expected generated files found in {data_path}; expected table stems include {sorted(expected)}")
    return uploaded


def publish_fabric_items(spec: ProjectSpec, fabric_dir: str | Path, item_types: list[str] | None = None) -> None:
    """Publish Git-compatible Fabric item folders with Microsoft's fabric-cicd library."""
    try:
        from azure.identity import AzureCliCredential
        from fabric_cicd import FabricWorkspace, publish_all_items
    except ImportError as exc:
        raise DeploymentError("Install Fabric extras first: pip install -e './fabric_builder[fabric]'") from exc

    workspace_name, workspace_id = _require_target(spec)
    kwargs = {"environment": spec.fabric.environment, "repository_directory": str(Path(fabric_dir).resolve()), "item_type_in_scope": item_types or ["Lakehouse", "Notebook"], "token_credential": AzureCliCredential()}
    if workspace_id:
        kwargs["workspace_id"] = workspace_id
    else:
        kwargs["workspace_name"] = workspace_name
    workspace = FabricWorkspace(**kwargs)
    publish_all_items(workspace)
