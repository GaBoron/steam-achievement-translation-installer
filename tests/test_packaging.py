from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_gui_and_installer_request_administrator_privileges() -> None:
    manifest = (ROOT / "src" / "Satl.Gui" / "app.manifest").read_text(encoding="utf-8")
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")

    assert 'requestedExecutionLevel level="requireAdministrator"' in manifest
    assert "PrivilegesRequired=admin" in installer
    assert "UsedUserAreasWarning=no" in installer
    assert "DefaultDirName={autopf}" in installer
    assert "runascurrentuser" in installer
    assert "runasoriginaluser" not in installer


def test_installed_app_name_does_not_include_the_version() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")

    assert "UninstallDisplayName={#MyAppName}" in installer
    assert "Root: HKCU" in installer
    assert "{{8E4CF3D1-13E7-4FF7-A979-CE07F27F020A}_is1" in installer


def test_settings_page_owns_log_display_settings() -> None:
    settings_page = (ROOT / "src" / "Satl.Gui" / "Pages" / "SettingsPage.xaml").read_text(
        encoding="utf-8"
    )
    logs_page = (ROOT / "src" / "Satl.Gui" / "Pages" / "LogsPage.xaml").read_text(
        encoding="utf-8"
    )

    assert "LogWordWrapSwitch" in settings_page
    assert "OpenLogs_Click" in settings_page
    assert "WordWrapButton" not in logs_page
    assert 'Label="打开目录"' not in logs_page


def test_release_projects_keep_runtime_payloads_small() -> None:
    gui_project = (ROOT / "src" / "Satl.Gui" / "Satl.Gui.csproj").read_text(encoding="utf-8")
    launcher_project = (
        ROOT / "src" / "Satl.CliLauncher" / "Satl.CliLauncher.csproj"
    ).read_text(encoding="utf-8")

    assert 'Include="Microsoft.WindowsAppSDK.WinUI"' in gui_project
    assert 'Include="Microsoft.WindowsAppSDK"' not in gui_project
    assert "<PublishTrimmed>True</PublishTrimmed>" in gui_project
    assert "<TrimMode>partial</TrimMode>" in gui_project
    assert "<Optimize>True</Optimize>" in gui_project

    assert "<PublishSingleFile>true</PublishSingleFile>" in launcher_project
    assert "<PublishTrimmed>true</PublishTrimmed>" in launcher_project
    assert "<TrimMode>partial</TrimMode>" in launcher_project
    assert "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>" in (
        launcher_project
    )
    assert "<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>" in (
        launcher_project
    )


def test_release_build_has_size_guard_and_cleans_staging_directories() -> None:
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")

    assert "$MaximumPackageSizeBytes = 140MB" in build_script
    assert "-CompressionLevel Optimal" in build_script
    assert "$PackageSizeBytes -gt $MaximumPackageSizeBytes" in build_script
    assert "Uncompressed release payload:" in build_script
    assert "Remove-Item -LiteralPath $Path -Recurse -Force" in build_script
