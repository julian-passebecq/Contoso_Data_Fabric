param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $repoRoot
try {
    if (-not $NoRestore) {
        dotnet restore .\ContosoDGV2.sln
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    dotnet run --project .\ContosoFabric.Desktop\ContosoFabric.Desktop.csproj
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
