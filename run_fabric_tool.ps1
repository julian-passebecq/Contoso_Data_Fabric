param(
  [ValidateSet("plan", "compile", "generate", "upload-bronze", "deploy-items")]
  [string]$Command = "plan",
  [string]$Project = ".\project.json",
  [string]$StopAfter = ""
)

$venvPython = Join-Path $PSScriptRoot ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
  py -m venv (Join-Path $PSScriptRoot ".venv")
  & $venvPython -m pip install -e (Join-Path $PSScriptRoot "fabric_builder")
}

$argsList = @("-m", "contoso_fabric", "--project", $Project, $Command)
if ($StopAfter -and ($Command -eq "plan" -or $Command -eq "compile")) {
  $argsList += @("--stop-after", $StopAfter)
}
& $venvPython @argsList
exit $LASTEXITCODE
