# Developer guide

## Requirements

- **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10`)
- Visual Studio **2026**, or VS Code with the C# Dev Kit. *(Visual Studio 2022 cannot build .NET 10 projects.)*
- Windows 10/11 (WinForms).

```powershell
dotnet build StudyApp.sln
dotnet test tests/StudyApp.Tests/StudyApp.Tests.csproj
dotnet run --project src/StudyApp.App            # debug run; data goes to src/StudyApp.App/bin/Debug/.../data
./build/publish.ps1 -Version 1.2.0               # release package → artifacts/StudyApp-1.2.0-win-x64.zip
```

## Solution layout

| Project | Purpose |
|---|---|
| `src/StudyApp.Domain` | Pure logic, no IO: models, item-type registry, SM-2 scheduler, grading, JSON import parser / exporter, prompt templates (EN+BG), translations, sample content |
| `src/StudyApp.Data` | SQLite (Microsoft.Data.Sqlite): `Db` wrapper, numbered migrations, `StudyStore` (partial classes per area), services: import, backup, sessions (builder, review runner, test runner) |
| `src/StudyApp.App` | WinForms UI → `StudyApp.exe`: theming layer (`UI/`), shell (`Shell/`), pages, dialogs, update client |
| `src/StudyApp.Updater` | .NET Framework 4.7.2 helper that swaps the files during an update (no runtime needed on any Windows 10/11) |
| `tests/StudyApp.Tests` | xUnit tests for domain + data, incl. a test that every UI string has a Bulgarian translation |

### Why Microsoft.Data.Sqlite and not EF Core
About ten tables with simple queries. Plain SQL is easy to read and debug, the app stays small (single-file
publish ~47 MB with the runtime), start-up is fast, and migrations are just numbered SQL scripts
(`Data/Migrations.cs`, tracked by `PRAGMA user_version`) — no `dotnet-ef` tooling needed.

### Why no third-party UI library
A thin, own theming layer (`src/StudyApp.App/UI`) gives exact control over light/dark/sepia themes, accent colour,
per-monitor DPI and the reading typography (line height, width) that stock WinForms labels can't do.
Everything is custom-painted and double-buffered; icons come from the built-in *Segoe Fluent Icons* font.

## Data

- Database: `<app folder>/data/study.db` (WAL mode). Backups: `data/backups`. Logs: `data/logs`.
- Soft deletes (`deleted_at`) back the recycle bin; purged after N days (setting).
- Settings are one JSON document in the `settings` table (`AppSettings`); new properties simply get defaults.
- **Migrations**: append a new SQL string to `Migrations.Steps`. Never edit a released step. The app migrates on start;
  updates therefore migrate automatically.
- Type-specific item fields live in the `items.data` JSON column (`ItemData`), so new item types need no migration.

## Adding an item type

1. `Domain/Models/ItemTypes.cs` – add the key, display names, `Normalize` aliases.
2. `Domain/Import/ImportParser.cs` – parse it (`ParseItem` switch); `Domain/Export/JsonExporter.cs` – write it.
3. `Domain/Grading/Grader.cs` – grade it.
4. `App/Pages/StudyViews.cs` – render it in `QuestionView` (or a new view) and `App/Dialogs/ItemEditorDialog.cs` – edit it.
5. Mention it in the prompt templates (`Domain/Prompts`) and `docs/json-format.md`.

## Translations (English / Bulgarian)

UI text is written in English and wrapped: `T("Text")`, `F("Hello {0}", name)`, `N(count, "item", "items")`.
Bulgarian lives in `Domain/Localization/Bulgarian.Strings.cs` (and plural forms in `Bulgarian.cs`).
The test `EveryUiStringHasABulgarianTranslation` fails when a translation is missing, and
`TranslationsKeepFormatPlaceholders` checks `{0}` placeholders. Language changes rebuild the UI.

## UI framework in short

- `Dpi.S(px)` scales every pixel value (monitor DPI × the "size of the app" setting); fonts are created in pixels.
- Layout: `Column` (vertical stack), `Row` (horizontal, `.Flex()` children share space), `CardGrid` (responsive grid),
  `TwoColumns`, all implementing `IHeightForWidth`; `ScrollHost` provides smooth scrolling with a themed bar.
- Pages derive from `Shell/PageBase` and are rebuilt from their factory on back-navigation, theme, language or DPI changes,
  so pages never handle those changes themselves.
- Study text uses `RichLayout` (Markdown subset with real line-height control).

## Releases and the in-app updater

1. Update `build/release-notes.md` (shown in the app's update dialog).
2. Tag and push: `git tag v1.2.0 && git push origin v1.2.0`.
3. `.github/workflows/release.yml` runs the tests, builds the portable zip (+ `.sha256`) and publishes the GitHub Release.

The app checks `https://api.github.com/repos/mugen89/Study-app/releases/latest` (on start if enabled, or via
Settings → Updates). Installing downloads the zip into `<app>/update`, verifies the SHA-256, backs up the database,
starts `StudyApp.Updater.exe` *from the new package*, and exits. The updater waits for the app to close, copies the
program files over the old ones — **never `data/`** — and restarts the app with `--updated <old version>`.
The new version migrates the database on start and cleans up `update/`.

**Testing an update locally**: build two packages with different versions, write a release JSON like GitHub's
(`tag_name`, `body`, `assets[].name/browser_download_url` with local file paths) and start the old version with
`STUDYAPP_UPDATE_FEED=C:\path\release.json` set.

## Developer start flags

| Flag | Effect |
|---|---|
| `--open=home|subjects|review|search|import|prompts|settings|trash` | open a page |
| `--open=subject:1` / `deck:2` / `read:1` / `stats:1` / `study:1` / `test:1` | open a specific screen |
| `--dev-sample` | load the sample subject if the database is empty |
| `--lang=bg`, `--theme=dark|light|sepia` | override language / theme for this run (not saved) |
| `--dev-dialog=subject|study|test|item|deck|chapter|shortcuts|update` | open a dialog on start |
| `<file.json>` | open the Import page with that file (also works with "Open with…") |
