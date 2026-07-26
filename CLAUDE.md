# VpnHood.Tools.ResourceTranslator

AI-backed i18n resource translator for JSON and Microsoft `.resx` files, shipped as a .NET tool
(`vhtranslator`) so other repositories can consume it without vendoring code.

## Layout

```
src/VpnHood.Tools.ResourceTranslator/
  Cli/            command-line surface, console reporter
  Configuration/  vhtranslator.json discovery, option resolution
  Formats/        IResourceFormat implementations (JSON, .resx)
  Site/           `site` command: whole-page translation of static (Jekyll) sites
  Translation/    engines, prompt building, response parsing
  Watch/          change tracking (which keys need retranslation)
  Program.cs      parse-and-dispatch only; the pipeline lives in TranslationRunner
tests/VpnHood.Tools.ResourceTranslator.Tests/
```

## Build and test

```bash
dotnet build VpnHood.Tools.ResourceTranslator.slnx
dotnet test  VpnHood.Tools.ResourceTranslator.slnx
dotnet pack  src/VpnHood.Tools.ResourceTranslator/VpnHood.Tools.ResourceTranslator.csproj -o ./artifacts
```

Gotchas worth knowing before debugging a broken test run:

- Tests run on **Microsoft.Testing.Platform**, not VSTest. `global.json` contains
  `"test": { "runner": "Microsoft.Testing.Platform" }`; the .NET 10 SDK dropped the VSTest bridge,
  so `dotnet test` fails with a confusing error if that entry is removed.
- MSTest 4 removed `Assert.ThrowsException`. Use `Assert.ThrowsExactly` /
  `Assert.ThrowsExactlyAsync`.
- Package versions are centrally managed in `Directory.Packages.props`. Do not put `Version=`
  on a `PackageReference`. Test packages are the exception: they come from the `MSTest.Sdk`
  version pinned in the test project's `Sdk` attribute.
- Shared build settings and package metadata live in `Directory.Build.props`, including the
  single `VersionPrefix` used for releases.

## Architecture notes

`TranslationRunner` is the whole pipeline and takes its collaborators by constructor, so tests
drive it with a `FakeTranslator` and no network. Prefer adding behaviour there over `Program.cs`.

**Adding a resource format:** implement `IResourceFormat`, register it in `ResourceFormatFactory`
(including `SupportedExtensions`). Nothing else should need to change.

**Adding an engine:** implement `ITranslator`, add a value to the `TranslationEngine` enum, and
register it in `TranslatorFactory` plus `EngineModelSelector` (aliases, default model, API-key
variable name).

**Errors:** throw `TranslatorException` for anything the user should see, with the appropriate
`ExitCodes` value. Exit codes are a contract with CI scripts — keep them stable. Anything else
is a bug and should surface with its stack trace.

**Change tracking:** `vh_translator/<base>_watch.json` stores the source text last translated per
key. A missing or corrupt watch file means "nothing is known to be current", so everything is
retranslated — that is deliberate, not a bug. Legacy MD5-hash watch files are migrated on the
next successful save.

**Site pipeline (`Site/`):** `SiteTranslationRunner` mirrors `TranslationRunner` for whole pages:
discover (`SitePageDiscovery` globs) → mask Liquid (`LiquidMasker`) → translate title/description/
body as one 3-item batch through the existing `ITranslator` engines → verify (`PageVerifier`,
AngleSharp tree/attribute comparison) → compose (`PageDocument`) → write. Verification is
**fail-closed by design**: these translations ship unreviewed, so a page that cannot be proven
structurally intact is never written (exit code `11`, `VerificationFailed`) and the previous
output stays. Do not weaken a check to make a model's output pass — tighten the prompt or add
retry feedback instead. The site watch file stores `sha256:` hashes per page path, in the same
envelope as the classic watch file. Front matter is never model-generated: only the `title` and
`description` values are replaced, and files gain `lang` + `auto_translated: true` (the marker
that makes a file safe to overwrite; its absence means hand-authored, never clobber). The marker
also gates the other two destructive paths: sources carrying it are skipped (a generated page
must never become a source), and orphaned targets are pruned only when they carry it.
`vhtranslator.json` may live at the root or inside `vh_translator/`; either way
`TranslatorConfig.BaseDirectory` is the site root — code must resolve paths against it, never
against the config file's own folder.

## Conventions

Follow `.editorconfig`. Notable: file-scoped namespaces, usings outside the namespace, primary
constructors preferred, expression-bodied properties but not methods, and braces on the same line
for members (`csharp_new_line_before_open_brace = types,methods`).

## Engines

Supported and documented engines are `gemini`, `gpt`, and `grok`, with environment variables
`GEMINI_API_KEY`, `OPENAI_API_KEY`, and `GROK_API_KEY`. Default model for `grok` is
`grok-4-latest`. Do not add other providers to docs, samples, or help text without the
maintainer asking for it.

## Releasing

Publication is a manual GitHub Actions dispatch from `main` — there are no release tags:

```bash
gh workflow run publish_nugets.yml --ref main
```

`.github/workflows/publish_nugets.yml` checks out vpnhood/VpnHood's shared `pub/` scripts;
`Publish-ModuleNugetPackages.ps1` self-bumps the build number (`pub/PubVersion.json` + the
`<Version>` in `Directory.Build.props`), builds, packs, pushes to NuGet, and commits the version
bump back to this repo. A minor/major bump is a hand edit of both files before dispatching;
`-independentVersion` keeps this tool on its own 1.x line, independent of the monorepo's release
train. `.github/workflows/build.yml` runs build/test/pack on pushes and pull requests.

Publishing uses **nuget.org Trusted Publishing** (OIDC) — there is no long-lived API key. The job
requests a GitHub OIDC token (`permissions: id-token: write`), exchanges it via `NuGet/login@v1`
for a key valid for one hour, and pushes with that. Two consequences worth remembering:

- The trusted publishing policy on nuget.org is bound to the **workflow file name**
  (`publish_nugets.yml`) plus owner/repo. Renaming or moving that file breaks publishing until
  the policy is updated to match.
- The only repository secret involved is `NUGET_USER`, the nuget.org profile name. If the login
  step fails with a policy mismatch, check the policy's owner/repo/workflow/environment fields
  rather than looking for a missing API key.
