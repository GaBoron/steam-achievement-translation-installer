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


def test_installer_removes_per_user_and_machine_registry_entries_on_uninstall() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")
    uninstall_key = (
        r"Software\Microsoft\Windows\CurrentVersion\Uninstall"
        r"\{{8E4CF3D1-13E7-4FF7-A979-CE07F27F020A}_is1"
    )

    assert (
        f'Root: HKCU; Subkey: "{uninstall_key}"; '
        "Flags: deletekey uninsdeletekey dontcreatekey"
    ) in installer
    assert (
        f'Root: HKLM; Subkey: "{uninstall_key}"; '
        "Flags: uninsdeletekey dontcreatekey"
    ) in installer


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

    assert 'Include="Microsoft.WindowsAppSDK.WinUI"' in gui_project
    assert 'Include="Microsoft.WindowsAppSDK"' not in gui_project
    assert "<PublishTrimmed>False</PublishTrimmed>" in gui_project
    assert "<TrimMode>" not in gui_project
    assert "<Optimize>True</Optimize>" in gui_project
    assert "<EnableMsixTooling>true</EnableMsixTooling>" in gui_project
    assert "<PublishSingleFile>True</PublishSingleFile>" in gui_project
    assert "<IncludeAllContentForSelfExtract>True</IncludeAllContentForSelfExtract>" in gui_project
    assert "<IncludeNativeLibrariesForSelfExtract>True</IncludeNativeLibrariesForSelfExtract>" in (
        gui_project
    )
    assert "<EnableCompressionInSingleFile>True</EnableCompressionInSingleFile>" in gui_project

def test_release_build_has_size_guard_and_cleans_staging_directories() -> None:
    build_script = (ROOT / "scripts" / "build.ps1").read_text(encoding="utf-8")

    assert "$MaximumPackageSizeBytes = 140MB" in build_script
    assert "$PackageSizeBytes -gt $MaximumPackageSizeBytes" in build_script
    assert '$PackageRuntimeRoot = Join-Path $PackageRoot "_runtime"' in build_script
    assert "Installer payload root must contain only SATLInstaller.exe" in build_script
    assert "Installer payload root contains scattered runtime files" in build_script
    assert "WinUI single-file publish produced unexpected loose files" in build_script
    assert "PortableArchive" not in build_script
    assert "Compress-Archive" not in build_script
    assert "CliLauncherProject" not in build_script
    assert "CliBuildRoot" not in build_script
    assert "Uncompressed release payload:" in build_script
    assert "Remove-Item -LiteralPath $Path -Recurse -Force" in build_script


def test_gui_resolves_the_internal_python_runtime() -> None:
    gui_service = (
        ROOT / "src" / "Satl.Gui" / "Services" / "SatlCliService.cs"
    ).read_text(encoding="utf-8")

    assert "var processPath = Environment.ProcessPath;" in gui_service
    assert "var applicationDirectory = ResolveApplicationDirectory();" in gui_service
    assert 'Path.Combine(applicationDirectory, "_runtime")' in gui_service
    assert 'Path.Combine(runtimeDirectory, "python.exe")' in gui_service
    assert 'Path.Combine(runtimeDirectory, "satl.pyz")' in gui_service


def test_release_surfaces_installable_artifacts_only() -> None:
    surfaces = [
        ROOT / "README.md",
        ROOT / "scripts" / "build.ps1",
        ROOT / ".github" / "workflows" / "ci.yml",
        ROOT / "src" / "Satl.Gui" / "Services" / "UpdateService.cs",
    ]

    for surface in surfaces:
        content = surface.read_text(encoding="utf-8")
        assert "SATLInstaller-Portable" not in content
        assert "PortableDownload" not in content


def test_installer_removes_known_legacy_runtime_before_upgrade() -> None:
    installer = (ROOT / "installer" / "SATLInstaller.iss").read_text(encoding="utf-8")

    assert "[InstallDelete]" in installer
    for legacy_pattern in (
        r'{app}\*.dll',
        r'{app}\*.json',
        r'{app}\*.pri',
        r'{app}\*.winmd',
        r'{app}\*.xbf',
        r'{app}\satl.exe',
        r'{app}\createdump.exe',
        r'{app}\RestartAgent.exe',
        r'{app}\_satl_runtime',
        r'{app}\Assets',
        r'{app}\Microsoft.UI.Xaml',
        r'{app}\Pages',
    ):
        assert legacy_pattern in installer

    assert r'Type: filesandordirs; Name: "{app}\*"' not in installer
    assert r'Type: files; Name: "{app}\*.exe"' not in installer
