# Third-Party Notices

## Desktop application dependencies

The Windows desktop interface uses the MIT-licensed Microsoft Windows App SDK,
Microsoft Windows SDK Build Tools, and CommunityToolkit.Mvvm projects. Test-only
dependencies include the MIT-licensed xUnit.net runner and Microsoft.NET.Test.Sdk.
Their respective copyright and license notices remain with those projects.

The installable Windows package is built with Inno Setup and includes its
official Simplified Chinese message file from the Inno Setup source repository.
Inno Setup and its message files remain subject to the Inno Setup license and
their respective copyright notices.

Windows release packages include the official CPython 3.13 embeddable runtime.
Python is distributed under the Python Software Foundation License; the
runtime's complete `LICENSE.txt` is included beside its binaries in
`_satl_runtime`.

## Translation data

This program downloads community-maintained achievement schema files from
[GaBoron/steam-achievement-translation-library](https://github.com/GaBoron/steam-achievement-translation-library).
The translation repository has its own mixed rights notice. Original game
content, achievement text, Steam schema content, names, and trademarks remain
the property of their respective rights holders.

## Acknowledgements

Thanks to GitHub user [KneeArcher](https://github.com/KneeArcher) for sharing a
prototype in translation-library Issue #94 and for helping define the desired
workflow. The contributor explicitly licensed the source ZIP they attached to
that issue under the MIT License in
[this comment](https://github.com/GaBoron/steam-achievement-translation-library/issues/94#issuecomment-4935293487).
The initial implementation remains an independent clean-room implementation
and does not copy the prototype's source.

The separate MIT-licensed
[PanVena/SteamAchievementLocalizer](https://github.com/PanVena/SteamAchievementLocalizer)
project edits and authors Steam achievement localizations. SATL has a narrower
role: discovering, installing, and restoring translations published by the
translation library.
