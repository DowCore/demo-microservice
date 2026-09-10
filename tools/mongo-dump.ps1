#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DumpDir = Join-Path $RepoRoot "data\mongo"
$Databases = @(
    "MetaDowAdministrationDb",
    "MetaDowIdentityServiceDb",
    "MetaDowProjectsDb",
    "MetaDowSaaSDb"
)

function Get-MetaDowMongoContainerId {
    $rows = docker ps --format "{{.ID}}`t{{.Image}}`t{{.Names}}"
    foreach ($row in $rows) {
        $parts = $row -split "`t"
        if ($parts.Count -lt 3) { continue }
        $id, $image, $name = $parts[0], $parts[1], $parts[2]
        $imageLower = $image.ToLowerInvariant()
        $nameLower = $name.ToLowerInvariant()
        if ($imageLower -match "express" -or $nameLower -match "express") { continue }
        if ($imageLower -match "mongo" -or $nameLower -match "mongodb") {
            return $id
        }
    }
    return $null
}

$containerId = Get-MetaDowMongoContainerId
if (-not $containerId) {
    Write-Error "MongoDB container not found. Start AppHost and ensure Docker Desktop is running."
}

Write-Host "Dump from container $containerId to $DumpDir"
New-Item -ItemType Directory -Force -Path $DumpDir | Out-Null
Get-ChildItem $DumpDir -Force | Where-Object { $_.Name -notin @("README.md", ".gitkeep") } | Remove-Item -Recurse -Force

docker exec $containerId sh -c "rm -rf /tmp/metadow-dump && mkdir -p /tmp/metadow-dump" | Out-Null
foreach ($db in $Databases) {
    Write-Host "mongodump --db $db"
    docker exec $containerId mongodump --db $db --out /tmp/metadow-dump
}

if (Test-Path (Join-Path $DumpDir "tmp-copy")) {
    Remove-Item (Join-Path $DumpDir "tmp-copy") -Recurse -Force
}
docker cp "${containerId}:/tmp/metadow-dump/." $DumpDir
docker exec $containerId rm -rf /tmp/metadow-dump | Out-Null

Write-Host "Dump finished. Commit .bson / .json under data/mongo if you want to share seed data."
Get-ChildItem $DumpDir -Recurse -File | Select-Object -ExpandProperty FullName
