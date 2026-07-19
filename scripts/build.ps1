$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DistRoot = Join-Path $ProjectRoot "dist"
$CliRoot = Join-Path $DistRoot "satl"
$PackageRoot = Join-Path $DistRoot "package-win-x64"
$PackageRuntimeRoot = Join-Path $PackageRoot "_runtime"
$GuiPublishRoot = Join-Path $DistRoot "gui-win-x64"
$ReleaseRoot = Join-Path $DistRoot "release"
$GuiBuildRoot = Join-Path $ProjectRoot "build\gui-publish-intermediate"
$CliPayloadRoot = Join-Path $ProjectRoot "build\cli-payload"
$DownloadRoot = Join-Path $ProjectRoot "build\downloads"
$VenvPython = Join-Path $ProjectRoot ".venv\Scripts\python.exe"
$Python = if (Test-Path $VenvPython) { $VenvPython } else { "python" }
$GuiProject = Join-Path $ProjectRoot "src\Satl.Gui\Satl.Gui.csproj"
$InstallerScript = Join-Path $ProjectRoot "installer\SATLInstaller.iss"
$IconPath = Join-Path $ProjectRoot "src\Satl.Gui\Assets\AppIcon.ico"
$EmbeddedPythonVersion = "3.13.13"
$EmbeddedPythonArchiveName = "python-$EmbeddedPythonVersion-embed-amd64.zip"
$EmbeddedPythonArchive = Join-Path $DownloadRoot $EmbeddedPythonArchiveName
$EmbeddedPythonPartial = "$EmbeddedPythonArchive.part"
$EmbeddedPythonUrl = "https://www.python.org/ftp/python/$EmbeddedPythonVersion/$EmbeddedPythonArchiveName"
$EmbeddedPythonSha256 = "8766a8775746235e23cf5aee5027ab1060bb981d93110577adcf3508aa0cbd55"
$MaximumPackageSizeBytes = 140MB

[xml] $GuiProjectXml = Get-Content -LiteralPath $GuiProject -Raw -Encoding UTF8
$Version = @($GuiProjectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "The WinUI project does not define a release version"
}
$SetupName = "SATLInstaller-Setup-v$Version.exe"
$SetupExecutable = Join-Path $ReleaseRoot $SetupName
$Checksums = Join-Path $ReleaseRoot "SHA256SUMS.txt"

function Assert-WithinProject([string] $Path) {
    $ResolvedProject = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    $ResolvedTarget = [System.IO.Path]::GetFullPath($Path)
    if (-not $ResolvedTarget.StartsWith($ResolvedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside project: $ResolvedTarget"
    }
}

function Find-InnoCompiler {
    $Candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    return $Candidates | Select-Object -First 1
}

@($DistRoot, $CliRoot, $PackageRoot, $PackageRuntimeRoot, $GuiPublishRoot, $ReleaseRoot, $GuiBuildRoot,
    $CliPayloadRoot, $DownloadRoot) |
    ForEach-Object { Assert-WithinProject $_ }

Push-Location $ProjectRoot
try {
    foreach ($Path in @($CliRoot, $CliPayloadRoot)) {
        if (Test-Path $Path) {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
    if (-not (Test-Path $DownloadRoot)) {
        New-Item -ItemType Directory -Path $DownloadRoot | Out-Null
    }
    if (Test-Path $EmbeddedPythonArchive) {
        $CachedPythonHash = (Get-FileHash -LiteralPath $EmbeddedPythonArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($CachedPythonHash -ne $EmbeddedPythonSha256) {
            Remove-Item -LiteralPath $EmbeddedPythonArchive -Force
        }
    }
    if (-not (Test-Path $EmbeddedPythonArchive)) {
        if (Test-Path $EmbeddedPythonPartial) {
            Remove-Item -LiteralPath $EmbeddedPythonPartial -Force
        }
        Invoke-WebRequest -Uri $EmbeddedPythonUrl -OutFile $EmbeddedPythonPartial
        $DownloadedPythonHash = (Get-FileHash -LiteralPath $EmbeddedPythonPartial -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($DownloadedPythonHash -ne $EmbeddedPythonSha256) {
            Remove-Item -LiteralPath $EmbeddedPythonPartial -Force
            throw "Downloaded embedded Python archive checksum mismatch: $DownloadedPythonHash"
        }
        Move-Item -LiteralPath $EmbeddedPythonPartial -Destination $EmbeddedPythonArchive
    }
    $EmbeddedPythonActualHash = (Get-FileHash -LiteralPath $EmbeddedPythonArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($EmbeddedPythonActualHash -ne $EmbeddedPythonSha256) {
        throw "Embedded Python archive checksum mismatch: $EmbeddedPythonActualHash"
    }

    $EmbeddedRuntimeRoot = $CliRoot
    Expand-Archive -LiteralPath $EmbeddedPythonArchive -DestinationPath $EmbeddedRuntimeRoot
    foreach ($UnusedRuntimeFile in @("pythonw.exe", "python.cat")) {
        $UnusedRuntimePath = Join-Path $EmbeddedRuntimeRoot $UnusedRuntimeFile
        if (Test-Path $UnusedRuntimePath) {
            Remove-Item -LiteralPath $UnusedRuntimePath -Force
        }
    }

    Copy-Item -LiteralPath (Join-Path $ProjectRoot "src\satl") -Destination $CliPayloadRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "src\satl\__main__.py") -Destination $CliPayloadRoot
    Get-ChildItem -LiteralPath $CliPayloadRoot -Directory -Filter "__pycache__" -Recurse |
        Remove-Item -Recurse -Force
    & $Python -m zipapp $CliPayloadRoot -o (Join-Path $EmbeddedRuntimeRoot "satl.pyz")
    if ($LASTEXITCODE -ne 0) {
        throw "SATL Python archive build failed"
    }

    $CliVersion = & (Join-Path $CliRoot "python.exe") (Join-Path $CliRoot "satl.pyz") --version
    if ($LASTEXITCODE -ne 0 -or $CliVersion -ne "satl $Version") {
        throw "Built SATL Python payload has unexpected version: $CliVersion"
    }

    foreach ($Path in @($PackageRoot, $ReleaseRoot)) {
        if (Test-Path $Path) {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
    foreach ($Path in @($GuiPublishRoot, $GuiBuildRoot)) {
        if (-not (Test-Path -LiteralPath $Path)) {
            New-Item -ItemType Directory -Path $Path | Out-Null
        }
    }
    Get-ChildItem -LiteralPath $GuiPublishRoot -Force |
        Where-Object { $_.Name -ne "SATLInstaller.exe" } |
        Remove-Item -Recurse -Force

    & dotnet publish $GuiProject `
        -c Release `
        -r win-x64 `
        -p:Platform=x64 `
        "-p:OutDir=$GuiBuildRoot" `
        --self-contained true `
        -o $GuiPublishRoot
    if ($LASTEXITCODE -ne 0) {
        throw "WinUI publish failed"
    }
    $GuiExecutable = Join-Path $GuiPublishRoot "SATLInstaller.exe"
    if (-not (Test-Path $GuiExecutable)) {
        throw "WinUI publish did not produce SATLInstaller.exe"
    }
    $GuiPublishFiles = @(Get-ChildItem -LiteralPath $GuiPublishRoot -Recurse -File)
    if ($GuiPublishFiles.Count -ne 1 -or $GuiPublishFiles[0].FullName -ne $GuiExecutable) {
        throw "WinUI single-file publish produced unexpected loose files"
    }

    Add-Type -AssemblyName System.Drawing
    $EmbeddedIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($GuiExecutable)
    if ($null -eq $EmbeddedIcon -or $EmbeddedIcon.Width -lt 16 -or $EmbeddedIcon.Height -lt 16) {
        throw "WinUI executable does not contain a usable embedded application icon"
    }
    $GuiVersion = (Get-Item -LiteralPath $GuiExecutable).VersionInfo
    if ($GuiVersion.ProductVersion -notlike "$Version*") {
        throw "WinUI executable has unexpected product version: $($GuiVersion.ProductVersion)"
    }

    Copy-Item -Path (Join-Path $GuiPublishRoot "*") -Destination $PackageRoot -Recurse
    New-Item -ItemType Directory -Path $PackageRuntimeRoot | Out-Null
    Copy-Item -Path (Join-Path $CliRoot "*") -Destination $PackageRuntimeRoot -Recurse
    foreach ($Document in @("README.md", "LICENSE", "THIRD_PARTY_NOTICES.md")) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot $Document) -Destination $PackageRoot
    }
    Get-ChildItem -LiteralPath $PackageRoot -Filter "*.pdb" -File -Recurse |
        Remove-Item -Force
    $PreviewPath = Join-Path $PackageRoot "Assets\AppIcon.preview.png"
    if (Test-Path $PreviewPath) {
        Remove-Item -LiteralPath $PreviewPath -Force
    }
    Get-ChildItem -LiteralPath $PackageRoot -Directory |
        Where-Object {
            $_.Name -match '^[a-z]{2,3}(?:-[A-Za-z0-9]+)+$' -and
            $_.Name -notin @("en-us", "zh-CN")
        } |
        Remove-Item -Recurse -Force

    $RootExecutables = @(Get-ChildItem -LiteralPath $PackageRoot -File -Filter "*.exe")
    if ($RootExecutables.Count -ne 1 -or $RootExecutables[0].Name -ne "SATLInstaller.exe") {
        throw (
            "Installer payload root must contain only SATLInstaller.exe; found: " +
            (($RootExecutables | ForEach-Object Name) -join ", ")
        )
    }
    $ScatteredRuntimeFiles = @(
        Get-ChildItem -LiteralPath $PackageRoot -File |
            Where-Object { $_.Extension -in @(".dll", ".json", ".pri", ".winmd", ".xbf") }
    )
    if ($ScatteredRuntimeFiles.Count -gt 0) {
        throw (
            "Installer payload root contains scattered runtime files: " +
            (($ScatteredRuntimeFiles | ForEach-Object Name) -join ", ")
        )
    }

    $PackageFiles = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse -File)
    $PackageSizeBytes = ($PackageFiles | Measure-Object -Property Length -Sum).Sum
    if ($PackageSizeBytes -gt $MaximumPackageSizeBytes) {
        throw (
            "Uncompressed release payload is unexpectedly large: " +
            "$([math]::Round($PackageSizeBytes / 1MB, 2)) MiB " +
            "(limit: $([math]::Round($MaximumPackageSizeBytes / 1MB, 2)) MiB)"
        )
    }
    $InnoCompiler = Find-InnoCompiler
    if (-not $InnoCompiler) {
        throw "Inno Setup 6 is required to build the installable release"
    }
    & $InnoCompiler `
        "/DSourceRoot=$PackageRoot" `
        "/DOutputRoot=$ReleaseRoot" `
        "/DMyAppVersion=$Version" `
        "/DMyAppIcon=$IconPath" `
        $InstallerScript
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $SetupExecutable)) {
        throw "Inno Setup did not produce $SetupName"
    }

    $SetupHash = (Get-FileHash -LiteralPath $SetupExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content `
        -LiteralPath $Checksums `
        -Value "$SetupHash  $([System.IO.Path]::GetFileName($SetupExecutable))" `
        -Encoding ascii
    foreach ($Path in @(
        $CliRoot,
        $PackageRoot,
        $GuiPublishRoot,
        $GuiBuildRoot,
        $CliPayloadRoot
    )) {
        if (Test-Path -LiteralPath $Path) {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
    }
    Write-Host "Uncompressed release payload: $([math]::Round($PackageSizeBytes / 1MB, 2)) MiB"
    Write-Host "Built release assets in $ReleaseRoot"
}
finally {
    Pop-Location
}
