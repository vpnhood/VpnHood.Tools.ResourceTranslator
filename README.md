# VpnHood.ResourceTranslator

An i18n resource translator that uses AI (Google Gemini, OpenAI ChatGPT, or Grok AI) to translate
JSON and Microsoft `.resx` localization files while preserving placeholders, HTML tags, and formatting.

It ships as a .NET tool, so any repository — apps, sites, docs — can adopt it without vendoring code.

## Features

- 🤖 **Multi-engine** — Google Gemini, OpenAI ChatGPT, and Grok AI, with the engine inferred from the model name
- 📦 **Multiple formats** — JSON (`{lang}.json`) and Microsoft `.resx` (`Name.resx` / `Name.{culture}.resx`)
- 🔄 **Incremental** — only entries whose source text changed are retranslated
- 🎯 **Placeholder-safe** — `{variables}`, HTML tags, and URLs survive translation
- 🗂️ **Per-repo config** — commit a `vhtranslator.json` and just run `vhtranslator`
- 🔧 **Customizable prompts** — project-specific glossaries and rules
- 🛡️ **Retries and clear exit codes** — designed to run unattended in CI

## Installation

Requires the .NET 10 SDK or later.

```bash
# Global install
dotnet tool install --global VpnHood.ResourceTranslator

# Or pin it per repository (recommended for teams and CI)
dotnet new tool-manifest
dotnet tool install VpnHood.ResourceTranslator
```

Then invoke it as `vhtranslator` (or `dotnet tool run vhtranslator` for a local install).

You will also need an API key for your chosen engine:

| Engine | Environment variable | Get a key |
| --- | --- | --- |
| Gemini (default) | `GEMINI_API_KEY` | [makersuite.google.com](https://makersuite.google.com/app/apikey) |
| ChatGPT | `OPENAI_API_KEY` | [platform.openai.com](https://platform.openai.com/api-keys) |
| Grok | `GROK_API_KEY` | [console.x.ai](https://console.x.ai/) |

## Quick start

```bash
export GEMINI_API_KEY="your-key"
vhtranslator -b locales/en.json
```

Every sibling locale file (`fr.json`, `es.json`, …) is brought up to date with `en.json`.

## Per-repository configuration

Drop a `vhtranslator.json` at the root of a repository and the tool needs no arguments at all.
It is discovered by walking up from the base file, or from the working directory.

```json
{
  "base": "locales/en.json",
  "engine": "gemini",
  "model": "gemini-flash-lite-latest",
  "batch": 20,
  "extraPrompt": "translation-guidelines.txt",
  "languages": ["fr", "de", "es"]
}
```

| Key | Meaning |
| --- | --- |
| `base` | Base language file, relative to the config file |
| `engine` | `gemini`, `gpt`, or `grok` |
| `model` | Model name; the engine is inferred from it when `engine` is omitted |
| `batch` | Entries per request (default 20) |
| `extraPrompt` | Extra instructions file, relative to the config file |
| `languages` | Target languages. Missing files are **created**. Omit to translate only existing sibling files |

Command-line options always override the config file:

```bash
vhtranslator                      # uses vhtranslator.json
vhtranslator -m gpt-4o-mini       # same config, different model
vhtranslator --config ci/vhtranslator.json
```

## Command-line options

```text
vhtranslator [options]

  -b, --base <path>          Path to the base language file (.json / .resx)
      --config <path>        Path to a vhtranslator.json file (default: nearest one found)
  -x, --extra-prompt <path>  Path to extra instructions appended to the AI prompt
  -c, --show-changes         Show changed keys since the last translation and exit
  -r, --rebuild-lang <code>  Force retranslation of every entry for one language
  -i, --ignore-changes       Mark all current entries as translated without calling the AI
  -k, --api-key <key>        API key (or use the engine's environment variable)
  -m, --model <name>         AI model (default depends on the engine)
  -e, --engine <name>        gemini, gpt, or grok (default: detected from the model name)
  -n, --batch <number>       Batch size for translation requests (default: 20)
  -?, -h, --help             Show help and usage information
      --version              Show version information
```

`--show-changes` and `--ignore-changes` do not call the AI and need no API key.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Invalid arguments or configuration |
| 2 | File not found or unsupported file type |
| 3 | Failed to parse the base file |
| 4 | Missing API key |
| 10 | Translation failed after retries |

## File layouts

The format is chosen from the base file's extension. The base file is never overwritten.

### JSON

Locale files are named `{language-code}.json`, and any language can be the base:

```text
locales/
├── en.json               # base
├── fr.json
├── de.json
└── vh_translator/
    └── en_watch.json     # change tracking (auto-generated, commit this)
```

### Microsoft .resx

Standard .NET naming: a neutral base file with culture-specific siblings. Only string `<data>`
entries are translated — typed/binary entries, comments, metadata, and the schema are preserved.

```text
Resources/
├── Strings.resx          # base (neutral culture, treated as source "en")
├── Strings.fr.resx
├── Strings.de-DE.resx
└── vh_translator/
    └── Strings_watch.json
```

```bash
vhtranslator -b Resources/Strings.resx
vhtranslator -b Resources/Strings.resx -r es   # creates Strings.es.resx
```

## How change tracking works

`vh_translator/<base>_watch.json` records the source text last translated for every key.
On the next run, a key is retranslated when its source text differs — and a key missing from a
target file is always filled in.

**Commit the watch file.** Without it, the next run treats every key as changed and retranslates
everything.

Adopting existing hand-written translations:

```bash
vhtranslator -b locales/en.json -i   # mark everything current, translate nothing
```

## Prompt customization

Add project-specific rules via `--extra-prompt`, the `extraPrompt` config key, or by creating
`vh_translator/custom_prompt.txt` next to the base file (picked up automatically).

```text
- Keep technical terms like "VPN", "API", "JSON" untranslated
- Brand name "VpnHood" should always remain unchanged
- Use formal tone for German; use Latin American variants for Spanish
- Return "*" to skip an entry entirely
```

Returning `*` makes the tool keep any existing translation, or fall back to the source text.
This is useful for brand names, URLs, and region-specific entries.

## Typical workflows

```bash
# See what would change
vhtranslator -c

# Translate new and changed entries, then review the diff
vhtranslator
git diff locales/

# Add a new language
vhtranslator -r it

# Re-translate one language after improving the prompt
vhtranslator -r fr
```

### In CI

```yaml
- run: dotnet tool install --global VpnHood.ResourceTranslator
- run: vhtranslator
  env:
    GEMINI_API_KEY: ${{ secrets.GEMINI_API_KEY }}
```

Pair it with a pull-request action so translations arrive as reviewable diffs.

## Repository layout

```text
src/VpnHood.ResourceTranslator/     # the tool
  Cli/                              # command-line surface
  Configuration/                    # config file discovery and option resolution
  Formats/                          # IResourceFormat: JSON and .resx
  Translation/                      # engines, prompts, response parsing
  Watch/                            # change tracking
tests/VpnHood.ResourceTranslator.Tests/
```

Adding a format means implementing `IResourceFormat` and registering it in `ResourceFormatFactory`.
Adding an engine means implementing `ITranslator` and registering it in `TranslatorFactory`.

## Building from source

```bash
git clone https://github.com/vpnhood/VpnHood.ResourceTranslator.git
cd VpnHood.ResourceTranslator
dotnet build
dotnet test
dotnet pack src/VpnHood.ResourceTranslator/VpnHood.ResourceTranslator.csproj -o ./artifacts
```

## Releasing

Version comes from `VersionPrefix` in `Directory.Build.props`; the publish workflow takes it from
the git tag:

```bash
git tag v1.1.0
git push origin v1.1.0
```

This requires a `NUGET_API_KEY` repository secret.

## Troubleshooting

**`Error: Missing API key`** — set the variable for your engine (`GEMINI_API_KEY`,
`OPENAI_API_KEY`, or `GROK_API_KEY`), or pass `-k`.

**`Error: Failed to parse base file`** — the base file must be a JSON object (validate with
`jq . locales/en.json`) or well-formed `.resx` XML. Nested JSON objects are not supported;
keys must be flat.

**Everything gets retranslated every run** — the watch file is missing or not committed.

**Rate limits** — lower `--batch`, or use a lighter model such as `gemini-flash-lite-latest`
or `gpt-4o-mini`. The tool already retries with backoff.

## License

LGPL-2.1-only — see [LICENSE](LICENSE). Same license as [VpnHood](https://github.com/vpnhood/VpnHood).
