$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DistRoot = Join-Path $ProjectRoot "dist"
$CliRoot = Join-Path $DistRoot "satl"
$PackageRoot = Join-Path $DistRoot "package-win-x64"
$GuiPublishRoot = Join-Path $DistRoot "gui-win-x64"
$ReleaseRoot = Join-Path $DistRoot "release"
$GuiBuildRoot = Join-Path $ProjectRoot "build\gui-publish-intermediate"
$CliBuildRoot = Join-Path $ProjectRoot "build\cli-launcher-publish"
$CliPayloadRoot = Join-Path $ProjectRoot "build\cli-payload"
$DownloadRoot = Join-Path $ProjectRoot "build\downloads"
$VenvPython = Join-Path $ProjectRoot ".venv\Scripts\python.exe"
$Python = if (Test-Path $VenvPython) { $VenvPython } else { "python" }
$GuiProject = Join-Path $ProjectRoot "src\Satl.Gui\Satl.Gui.csproj"
$CliLauncherProject = Join-Path $ProjectRoot "src\Satl.CliLauncher\Satl.CliLauncher.csproj"
$InstallerScript = Join-Path $ProjectRoot "installer\SATLInstaller.iss"
$IconPath = Join-Path $ProjectRoot "src\Satl.Gui\Assets\AppIcon.ico"
$EmbeddedPythonVersion = "3.13.13"
$EmbeddedPythonArchiveName = "python-$EmbeddedPythonVersion-embed-amd64.zip"
$EmbeddedPythonArchive = Join-Path $DownloadRoot $EmbeddedPythonArchiveName
$EmbeddedPythonPartial = "$EmbeddedPythonArchive.part"
$EmbeddedPythonUrl = "https://www.python.org/ftp/python/$EmbeddedPythonVersion/$EmbeddedPythonArchiveName"
$EmbeddedPythonSha256 = "8766a8775746235e23cf5aee5027ab1060bb981d93110577adcf3508aa0cbd55"

[xml] $GuiProjectXml = Get-Content -LiteralPath $GuiProject -Raw -Encoding UTF8
$Version = @($GuiProjectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "The WinUI project does not define a release version"
}
$PortableName = "SATLInstaller-Portable-v$Version.zip"
$SetupName = "SATLInstaller-Setup-v$Version.exe"
$PortableArchive = Join-Path $ReleaseRoot $PortableName
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

@($DistRoot, $CliRoot, $PackageRoot, $GuiPublishRoot, $ReleaseRoot, $GuiBuildRoot,
    $CliBuildRoot, $CliPayloadRoot, $DownloadRoot) |
    ForEach-Object { Assert-WithinProject $_ }

Push-Location $ProjectRoot
try {
    foreach ($Path in @($CliRoot, $CliBuildRoot, $CliPayloadRoot)) {
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

    $EmbeddedRuntimeRoot = Join-Path $CliRoot "_satl_runtime"
    New-Item -ItemType Directory -Path $EmbeddedRuntimeRoot | Out-Null
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

    & dotnet publish $CliLauncherProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        "-p:Version=$Version" `
        "-p:AssemblyVersion=$Version.0" `
        "-p:FileVersion=$Version.0" `
        "-p:InformationalVersion=$Version" `
        -o $CliBuildRoot
    if ($LASTEXITCODE -ne 0) {
        throw "SATL native launcher publish failed"
    }
    Copy-Item -Path (Join-Path $CliBuildRoot "*") -Destination $CliRoot -Recurse
    $CliVersion = & (Join-Path $CliRoot "satl.exe") --version
    if ($LASTEXITCODE -ne 0 -or $CliVersion -ne "satl $Version") {
        throw "Built satl.exe has unexpected version: $CliVersion"
    }

    foreach ($Path in @($GuiPublishRoot, $GuiBuildRoot, $PackageRoot, $ReleaseRoot)) {
        if (Test-Path $Path) {
            Remove-Item -LiteralPath $Path -Recurse -Force
        }
        New-Item -ItemType Directory -Path $Path | Out-Null
    }

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
    Copy-Item -Path (Join-Path $CliRoot "*") -Destination $PackageRoot -Recurse
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

    Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $PortableArchive

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

    $HashLines = foreach ($ReleaseFile in @($SetupExecutable, $PortableArchive)) {
        $Hash = (Get-FileHash -LiteralPath $ReleaseFile -Algorithm SHA256).Hash.ToLowerInvariant()
        "$Hash  $([System.IO.Path]::GetFileName($ReleaseFile))"
    }
    Set-Content -LiteralPath $Checksums -Value $HashLines -Encoding ascii
    Write-Host "Built release assets in $ReleaseRoot"
}
finally {
    Pop-Location
}
