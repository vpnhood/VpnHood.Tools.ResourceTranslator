# VpnHood.Tools.ResourceTranslator

AI-backed i18n resource translator for JSON and Microsoft `.resx` files, shipped as a .NET tool
(`vhtranslator`) so other repositories can consume it without vendoring code.

## Layout

```
src/VpnHood.Tools.ResourceTranslator/
  Cli/            command-line surface, console reporter
  Configuration/  vhtranslator.json discovery, option resolution
  Formats/        IResourceFormat implementations (JSON, .resx)
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

`Directory.Build.props` holds `VersionPrefix`. Tagging drives publication:

```bash
git tag v1.1.0 && git push origin v1.1.0
```

`.github/workflows/publish.yml` builds, tests, packs with the tag's version, and pushes to NuGet.
`.github/workflows/build.yml` runs build/test/pack on pushes and pull requests.

Publishing uses **nuget.org Trusted Publishing** (OIDC) — there is no long-lived API key. The job
requests a GitHub OIDC token (`permissions: id-token: write`), exchanges it via `NuGet/login@v1`
for a key valid for one hour, and pushes with that. Two consequences worth remembering:

- The trusted publishing policy on nuget.org is bound to the **workflow file name**
  (`publish.yml`) plus owner/repo. Renaming or moving that file breaks publishing until the
  policy is updated to match.
- The only repository secret involved is `NUGET_USER`, the nuget.org profile name. If the login
  step fails with a policy mismatch, check the policy's owner/repo/workflow/environment fields
  rather than looking for a missing API key.
