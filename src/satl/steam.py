from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

from satl.errors import PreflightError
from satl.models import DiscoveryRecord, SteamAccount
from satl.vdf import get_casefold, load_vdf

try:
    import winreg
except ImportError:  # pragma: no cover - Windows-only runtime
    winreg = None  # type: ignore[assignment]


APP_MANIFEST_RE = re.compile(r"^appmanifest_([0-9]+)\.acf$", re.IGNORECASE)
STEAM_ID_BASE = 76561197960265728


def validate_steam_dir(path: Path) -> Path:
    resolved = Path(path).expanduser().resolve()
    if not (resolved / "steam.exe").is_file():
        raise PreflightError(f"Steam 目录无效，未找到 steam.exe：{resolved}")
    return resolved


def find_steam_dir(explicit: str | Path | None = None) -> Path:
    if explicit:
        return validate_steam_dir(Path(explicit))

    if winreg is not None:
        registry_candidates = (
            (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath"),
            (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam", "InstallPath"),
        )
        for root, key_name, value_name in registry_candidates:
            try:
                with winreg.OpenKey(root, key_name) as key:
                    value, _ = winreg.QueryValueEx(key, value_name)
                candidate = Path(str(value)).expanduser()
                if (candidate / "steam.exe").is_file():
                    return candidate.resolve()
            except OSError:
                continue

    candidates: list[Path] = []
    for variable in ("PROGRAMFILES(X86)", "PROGRAMFILES"):
        if os.environ.get(variable):
            candidates.append(Path(os.environ[variable]) / "Steam")
    for drive in "CDEFG":
        candidates.append(Path(f"{drive}:\\Steam"))
    for candidate in candidates:
        if (candidate / "steam.exe").is_file():
            return candidate.resolve()
    raise PreflightError("未检测到 Steam，请使用 --steam-dir 指定安装目录")


def is_steam_running() -> bool:
    if os.name != "nt":
        return False
    try:
        completed = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq steam.exe", "/NH"],
            check=False,
            capture_output=True,
            text=False,
            timeout=5,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except (OSError, subprocess.SubprocessError):
        return False
    return _tasklist_contains_steam(completed.stdout)


def _tasklist_contains_steam(stdout: bytes | None) -> bool:
    """Check tasklist output without decoding the active Windows code page."""
    return b"steam.exe" in (stdout or b"").lower()


def discover_library_dirs(steam_dir: Path) -> tuple[Path, ...]:
    steam_dir = Path(steam_dir).resolve()
    paths: list[Path] = [steam_dir]
    library_file = steam_dir / "steamapps" / "libraryfolders.vdf"
    if library_file.is_file():
        data = load_vdf(library_file)
        root = get_casefold(data, "libraryfolders", data)
        if isinstance(root, dict):
            for key, value in root.items():
                if not str(key).isdigit():
                    continue
                raw_path = get_casefold(value, "path") if isinstance(value, dict) else value
                if isinstance(raw_path, str) and raw_path.strip():
                    paths.append(Path(raw_path).expanduser())

    unique: list[Path] = []
    seen: set[str] = set()
    for path in paths:
        try:
            resolved = path.resolve()
        except OSError:
            resolved = path.absolute()
        key = os.path.normcase(str(resolved))
        if key not in seen:
            seen.add(key)
            unique.append(resolved)
    return tuple(unique)


def discover_installed_apps(steam_dir: Path) -> set[str]:
    return set(discover_installed_games(steam_dir))


def discover_installed_games(steam_dir: Path) -> dict[str, str]:
    games: dict[str, str] = {}
    for library in discover_library_dirs(steam_dir):
        steamapps = library / "steamapps"
        if not steamapps.is_dir():
            continue
        try:
            children = steamapps.iterdir()
            for path in children:
                match = APP_MANIFEST_RE.fullmatch(path.name)
                if match and path.is_file():
                    app_id = match.group(1)
                    game_name = ""
                    try:
                        manifest = load_vdf(path)
                        app_state = get_casefold(manifest, "AppState", manifest)
                        game_name = str(get_casefold(app_state, "name", "")).strip()
                    except PreflightError:
                        # A damaged manifest still identifies a local App ID.
                        pass
                    games[app_id] = game_name
        except OSError as exc:
            raise PreflightError(f"无法读取 Steam 库目录：{steamapps}：{exc}") from exc
    return games


def discover_accounts(steam_dir: Path) -> tuple[SteamAccount, ...]:
    path = Path(steam_dir) / "config" / "loginusers.vdf"
    if not path.is_file():
        return ()
    data = load_vdf(path)
    users = get_casefold(data, "users", data)
    if not isinstance(users, dict):
        return ()
    accounts: list[SteamAccount] = []
    for steam_id, raw in users.items():
        if not str(steam_id).isdigit() or not isinstance(raw, dict):
            continue
        account_name = str(get_casefold(raw, "AccountName", ""))
        persona_name = str(get_casefold(raw, "PersonaName", account_name or steam_id))
        most_recent = str(get_casefold(raw, "MostRecent", "0")) == "1"
        accounts.append(SteamAccount(str(steam_id), account_name, persona_name, most_recent))
    accounts.sort(key=lambda item: (not item.most_recent, item.persona_name.casefold(), item.steam_id))
    return tuple(accounts)


def steam_id32(steam_id: str) -> str:
    try:
        numeric = int(steam_id)
    except ValueError:
        return steam_id
    return str(numeric - STEAM_ID_BASE) if numeric >= STEAM_ID_BASE else str(numeric)


def discover_account_cached_apps(steam_dir: Path, account: SteamAccount) -> set[str]:
    path = (
        Path(steam_dir)
        / "userdata"
        / steam_id32(account.steam_id)
        / "config"
        / "localconfig.vdf"
    )
    if not path.is_file():
        return set()
    data: object = load_vdf(path)
    for key in ("UserLocalConfigStore", "Software", "Valve", "Steam", "apps"):
        data = get_casefold(data, key, {})
    if not isinstance(data, dict):
        return set()
    return {str(app_id) for app_id in data if str(app_id).isdigit()}


def discover_local_games(steam_dir: Path, account_id: str | None = None) -> dict[str, DiscoveryRecord]:
    records: dict[str, DiscoveryRecord] = {}
    for app_id, game_name in discover_installed_games(steam_dir).items():
        records.setdefault(app_id, DiscoveryRecord(app_id, game_name)).discovery.add("installed")

    accounts = discover_accounts(steam_dir)
    if account_id:
        selected = tuple(account for account in accounts if account.steam_id == account_id)
        if not selected:
            raise PreflightError(f"本机没有 Steam 账号：{account_id}")
    else:
        selected = accounts
    for account in selected:
        for app_id in discover_account_cached_apps(steam_dir, account):
            record = records.setdefault(app_id, DiscoveryRecord(app_id))
            record.discovery.add("account-cache")
            record.accounts.add(account.steam_id)
    return records


def schema_target(steam_dir: Path, app_id: str) -> Path:
    if not app_id.isdigit():
        raise PreflightError(f"无效的 Steam App ID：{app_id}")
    return Path(steam_dir) / "appcache" / "stats" / f"UserGameStatsSchema_{app_id}.bin"
