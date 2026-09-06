import json

from contoso_fabric.bundle import compile_bundle
from contoso_fabric.spec import ProjectSpec


def get_spec(stop_after="gold"):
    return ProjectSpec.from_dict({"project": {"name": "bundle-test"}, "business": {"scenario": "sales-bi", "scale": "tiny", "years": 2}, "data": {"landing": {"format": "parquet"}}, "architecture": {"stopAfter": stop_after}, "fabric": {"workspace": {"mode": "existing", "name": "Demo"}}})


def test_compile_bronze_only(tmp_path):
    compile_bundle(get_spec("bronze"), tmp_path)
    assert (tmp_path / "fabric" / "Contoso_Bronze.Lakehouse" / ".platform").exists()
    assert (tmp_path / "fabric" / "Bronze_Load.Notebook" / "notebook-content.ipynb").exists()
    assert not (tmp_path / "fabric" / "Contoso_Silver.Lakehouse").exists()


def test_compile_gold_writes_valid_notebooks(tmp_path):
    compile_bundle(get_spec("gold"), tmp_path)
    for name in ("Bronze_Load", "Silver_Transform", "Gold_Build"):
        nb = json.loads((tmp_path / "fabric" / f"{name}.Notebook" / "notebook-content.ipynb").read_text())
        assert nb["nbformat"] == 4
        assert nb["cells"]
