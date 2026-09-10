#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DumpDir = Join-Path $RepoRoot "data\mongo"

function Test-MetaDowMongoDump {
    if (-not (Test-Path $DumpDir)) { return $false }
    $files = Get-ChildItem $DumpDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in ".bson", ".archive" -or $_.Name -like "*.metadata.json" }
    return $null -ne $files -and $files.Count -gt 0
}

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

if (-not (Test-MetaDowMongoDump)) {
    Write-Host "No mongodump files in data/mongo; skip restore. Run tools/mongo-dump.ps1 first if you need seed data."
    exit 0
}

$containerId = Get-MetaDowMongoContainerId
if (-not $containerId) {
    Write-Error "MongoDB container not found. Start AppHost and ensure Docker Desktop is running."
}

Write-Host "Restore $DumpDir -> container $containerId"
docker exec $containerId sh -c "rm -rf /tmp/metadow-restore && mkdir -p /tmp/metadow-restore" | Out-Null
docker cp "$DumpDir/." "${containerId}:/tmp/metadow-restore"
$drop = if ($env:MONGO_RESTORE_DROP -eq "1") { "--drop" } else { "" }
$cmd = "mongorestore $drop /tmp/metadow-restore"
Write-Host $cmd
docker exec $containerId sh -c $cmd
docker exec $containerId rm -rf /tmp/metadow-restore | Out-Null
Write-Host "Restore finished."
