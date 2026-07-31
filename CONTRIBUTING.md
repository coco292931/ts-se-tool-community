# Contributing

Thank you for helping maintain TS SE Tool Community.

## Before opening a bug

1. Reproduce the issue on a copied manual save.
2. Close the game before editing.
3. Make one change only.
4. Reload the save in the editor.
5. Attach `log.log`, `errorlog.log` and `compatibility.log`.
6. State the game version, save format, enabled DLCs/mods and exact steps.
7. Do not upload a personal save publicly. Use an anonymized minimal fixture or offer it privately to a maintainer.

## Pull requests

- Preserve original copyright and license headers.
- Keep changes surgical and scoped to one domain.
- Never bypass modern writer validation or enable the legacy serializer for modern saves.
- Add log markers for initialization, staging and validation.
- Include before/after diff evidence and reload/game validation.
- Build `Release|Any CPU` with .NET Framework 4.7.2.

## Unsupported proposals

Pull requests that enable raw block insertion/deletion, manual unverified accessory paths or graph mutation without complete reference validation will not be merged.
