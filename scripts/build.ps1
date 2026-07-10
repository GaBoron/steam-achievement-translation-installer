$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DistRoot = Join-Path $ProjectRoot "dist"
$PackageRoot = Join-Path $DistRoot "satl-win-x64"
$Archive = Join-Path $DistRoot "satl-win-x64.zip"
$VenvPython = Join-Path $ProjectRoot ".venv\Scripts\python.exe"
$Python = if (Test-Path $VenvPython) { $VenvPython } else { "python" }

function Assert-WithinProject([string] $Path) {
    $ResolvedProject = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    $ResolvedTarget = [System.IO.Path]::GetFullPath($Path)
    if (-not $ResolvedTarget.StartsWith($ResolvedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside project: $ResolvedTarget"
    }
}

Assert-WithinProject $DistRoot
Assert-WithinProject $PackageRoot
Assert-WithinProject $Archive

Push-Location $ProjectRoot
try {
    & $Python -m PyInstaller --noconfirm --clean --onefile --name satl --paths src src/satl/__main__.py
    & (Join-Path $DistRoot "satl.exe") --version
    if ($LASTEXITCODE -ne 0) {
        throw "Built satl.exe failed its version smoke test"
    }

    if (Test-Path $PackageRoot) {
        Remove-Item -LiteralPath $PackageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PackageRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $DistRoot "satl.exe") -Destination $PackageRoot
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "README.md") -Destination $PackageRoot
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "LICENSE") -Destination $PackageRoot
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "THIRD_PARTY_NOTICES.md") -Destination $PackageRoot

    if (Test-Path $Archive) {
        Remove-Item -LiteralPath $Archive -Force
    }
    Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $Archive
    $Hash = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$Archive.sha256" -Value "$Hash  satl-win-x64.zip" -Encoding ascii
    Write-Host "Built $Archive"
}
finally {
    Pop-Location
}
