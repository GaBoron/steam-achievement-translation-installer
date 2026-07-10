from __future__ import annotations

from pathlib import Path

from satl.steam import discover_accounts, discover_library_dirs, discover_local_games, steam_id32
from satl.vdf import parse_vdf


STEAM_ID = "76561198000000000"


def test_vdf_parser_handles_comments_nested_objects_and_escapes() -> None:
    parsed = parse_vdf(
        r'''
        // comment
        "Root"
        {
            "path" "D:\\SteamLibrary"
            "quote" "a\"b"
        }
        '''
    )
    assert parsed["Root"]["path"] == r"D:\SteamLibrary"
    assert parsed["Root"]["quote"] == 'a"b'


def make_steam_tree(root: Path) -> tuple[Path, Path]:
    steam = root / "Steam"
    other = root / "OtherLibrary"
    (steam / "steamapps").mkdir(parents=True)
    (steam / "steam.exe").write_bytes(b"")
    (steam / "steamapps" / "appmanifest_100.acf").write_text('"AppState" {}', encoding="utf-8")
    (other / "steamapps").mkdir(parents=True)
    (other / "steamapps" / "appmanifest_200.acf").write_text('"AppState" {}', encoding="utf-8")
    escaped = str(other).replace("\\", "\\\\")
    (steam / "steamapps" / "libraryfolders.vdf").write_text(
        f'"libraryfolders" {{ "0" {{ "path" "{steam}" }} "1" {{ "path" "{escaped}" }} }}',
        encoding="utf-8",
    )
    (steam / "config").mkdir()
    (steam / "config" / "loginusers.vdf").write_text(
        f'"users" {{ "{STEAM_ID}" {{ "AccountName" "test" "PersonaName" "Tester" "MostRecent" "1" }} }}',
        encoding="utf-8",
    )
    local = steam / "userdata" / steam_id32(STEAM_ID) / "config"
    local.mkdir(parents=True)
    (local / "localconfig.vdf").write_text(
        '"UserLocalConfigStore" { "Software" { "Valve" { "Steam" { "apps" { "200" {} "300" {} } } } } }',
        encoding="utf-8",
    )
    return steam, other


def test_discovers_multiple_libraries_accounts_and_sources(tmp_path: Path) -> None:
    steam, other = make_steam_tree(tmp_path)
    libraries = discover_library_dirs(steam)
    assert libraries == (steam.resolve(), other.resolve())
    accounts = discover_accounts(steam)
    assert len(accounts) == 1
    assert accounts[0].persona_name == "Tester"

    records = discover_local_games(steam)
    assert records["100"].discovery == {"installed"}
    assert records["200"].discovery == {"installed", "account-cache"}
    assert records["300"].discovery == {"account-cache"}
    assert records["300"].accounts == {STEAM_ID}


def test_account_filter_accepts_local_steam_id(tmp_path: Path) -> None:
    steam, _ = make_steam_tree(tmp_path)
    records = discover_local_games(steam, STEAM_ID)
    assert records["300"].accounts == {STEAM_ID}
