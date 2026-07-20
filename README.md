# VpnHood.Tools.ResourceTranslator

**Keep your app's translations up to date automatically.** Edit your base language file, run one
command, and every other locale catches up — placeholders, HTML, and formatting intact.

[![NuGet](https://img.shields.io/nuget/v/VpnHood.Tools.ResourceTranslator.svg)](https://www.nuget.org/packages/VpnHood.Tools.ResourceTranslator)
[![Build](https://github.com/vpnhood/VpnHood.Tools.ResourceTranslator/actions/workflows/build.yml/badge.svg)](https://github.com/vpnhood/VpnHood.Tools.ResourceTranslator/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-LGPL--2.1-blue.svg)](LICENSE)

```console
$ vhtranslator
Processing fr.json (fr) - 3 changed, 1 missing entries...
✓ fr.json: 4 translated/updated.
  de.json: Up to date, no changes needed.
Done.
```

It works on JSON (`en.json`, `fr.json`, …) and Microsoft `.resx` files, backed by Google Gemini,
OpenAI, or Grok. It ships as a .NET tool, so any repository can adopt it without vendoring code.

---

## Why

Machine-translating a whole locale file on every build is slow, expensive, and overwrites work your
translators did by hand. This tool tracks the **source text** behind every key, so a run only sends
what actually changed — usually a handful of strings. Everything else is left exactly as it is.

- 🔄 **Incremental** — only changed entries are retranslated; hand-written translations survive
- 🎯 **Placeholder-safe** — `{variables}`, HTML tags, and URLs come back intact
- 🤖 **Multi-engine** — Gemini, OpenAI, or Grok, inferred from the model name
- 📦 **Two formats** — JSON and Microsoft `.resx`
- 🗂️ **Zero-argument runs** — commit a `vhtranslator.json` and just run `vhtranslator`
- 🔧 **Your terminology** — project glossaries and per-key rules
- 🛡️ **CI-friendly** — retries with backoff, and stable exit codes

## Install

Requires the .NET 10 SDK or later.

```bash
# Pin it per repository (recommended — everyone and CI get the same version)
dotnet new tool-manifest
dotnet tool install VpnHood.Tools.ResourceTranslator

# ...or install it globally
dotnet tool install --global VpnHood.Tools.ResourceTranslator
```

Set the API key for the engine you use:

| Engine | Variable | Get a key |
| --- | --- | --- |
| Gemini *(default)* | `GEMINI_API_KEY` | [makersuite.google.com](https://makersuite.google.com/app/apikey) |
| OpenAI | `OPENAI_API_KEY` | [platform.openai.com](https://platform.openai.com/api-keys) |
| Grok | `GROK_API_KEY` | [console.x.ai](https://console.x.ai/) |

## Usage

### Translate everything that changed

Point it at your base language file. Every sibling locale is brought up to date:

```bash
vhtranslator --base locales/en.json
```

```text
locales/
├── en.json    ← base (never modified)
├── fr.json    ← updated
├── de.json    ← updated
└── es.json    ← updated
```

### Preview first

`--show-changes` lists what a run would translate and exits. It needs no API key:

```bash
vhtranslator -b locales/en.json --show-changes
```

### Add a language

Create the file — even empty — then rebuild it:

```bash
echo "{}" > locales/it.json
vhtranslator -b locales/en.json --rebuild-lang it
```

Or list it under `languages` in the config file, and it will be created for you.

### Adopt existing translations

If you already have hand-written locale files, tell the tool they are current so it does not
retranslate them on the first run:

```bash
vhtranslator -b locales/en.json --ignore-changes
```

### Review before committing

Translations are just file changes — read them like any other diff:

```bash
vhtranslator && git diff locales/
```

## Configuration

Drop a `vhtranslator.json` in your repository and the tool needs no arguments at all. It is found by
walking up from the base file, or from the working directory.

```json
{
  "base": "locales/en.json",
  "model": "gemini-flash-lite-latest",
  "batch": 20,
  "extraPrompt": "translation-guidelines.txt",
  "languages": ["fr", "de", "es"]
}
```

| Key | Description |
| --- | --- |
| `base` | Base language file, relative to the config file |
| `engine` | `gemini`, `gpt`, or `grok`. Inferred from `model` when omitted |
| `model` | Model name. Defaults to `gemini-flash-lite-latest` |
| `batch` | Entries per request. Default `20` |
| `extraPrompt` | Extra instructions file, relative to the config file |
| `languages` | Target languages. **Missing files are created.** Omit to update only existing locale files |

Command-line options always win, so one-off overrides stay easy:

```bash
vhtranslator                       # uses vhtranslator.json
vhtranslator -m gpt-4o-mini        # same config, different model
vhtranslator --config ci/vhtranslator.json
```

## How it decides what to translate

After each successful run the tool records the source text of every key in
`vh_translator/<base>_watch.json`. On the next run a key is translated when:

- its **source text changed** since that record, **or**
- it is **missing or empty** in the target file.

> **Commit the watch file.** Without it nothing is known to be current, so the next run retranslates
> every key in every language.

## Customizing translations

Add project rules via `--extra-prompt`, the `extraPrompt` config key, or by creating
`vh_translator/custom_prompt.txt` next to your base file, which is picked up automatically.

```text
- Keep "VPN", "API", and "JSON" untranslated
- The brand name "VpnHood" never changes
- Use formal tone for German; use Latin American Spanish
- Return "*" for keys ending in _URL
```

Returning `*` skips an entry: the existing translation is kept, or the source text is used if there
is none. Useful for brand names, URLs, and region-specific strings.

## Working with .resx

Standard .NET naming — a neutral base file with culture-specific siblings:

```text
Resources/
├── Strings.resx        ← base (neutral culture, treated as source "en")
├── Strings.fr.resx
└── Strings.de-DE.resx
```

```bash
vhtranslator -b Resources/Strings.resx
vhtranslator -b Resources/Strings.resx -r es    # creates Strings.es.resx
```

Only string `<data>` entries are touched. Typed and binary entries, comments, metadata, and the
schema are preserved byte for byte.

## Continuous integration

```yaml
- run: dotnet tool install --global VpnHood.Tools.ResourceTranslator
- run: vhtranslator
  env:
    GEMINI_API_KEY: ${{ secrets.GEMINI_API_KEY }}
```

Pair it with a create-pull-request action so translations arrive as reviewable diffs rather than
unattended commits.

## Reference

### Options

```text
vhtranslator [options]

  -b, --base <path>          Base language file (.json / .resx)
      --config <path>        Config file to use (default: nearest vhtranslator.json)
  -x, --extra-prompt <path>  Extra instructions appended to the AI prompt
  -c, --show-changes         List changed keys and exit
  -r, --rebuild-lang <code>  Retranslate every entry for one language
  -i, --ignore-changes       Mark all current entries as translated, without calling the AI
  -k, --api-key <key>        API key (or use the engine's environment variable)
  -m, --model <name>         AI model (default depends on the engine)
  -e, --engine <name>        gemini, gpt, or grok (default: inferred from the model name)
  -n, --batch <number>       Entries per request (default: 20)
  -?, -h, --help             Show help and usage information
      --version              Show version information
```

`--show-changes` and `--ignore-changes` never contact the AI and need no API key.

### Default models

| Engine | Default model |
| --- | --- |
| `gemini` | `gemini-flash-lite-latest` |
| `gpt` | `gpt-4o-mini` |
| `grok` | `grok-4-latest` |

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Invalid arguments or configuration |
| `2` | File not found, or unsupported file type |
| `3` | Could not parse the base file |
| `4` | Missing API key |
| `10` | Translation failed after retries |

## Troubleshooting

**Everything is retranslated on every run.** The watch file is missing or not committed. Run once,
then commit `vh_translator/`.

**`Missing API key`.** Set `GEMINI_API_KEY`, `OPENAI_API_KEY`, or `GROK_API_KEY` for your engine, or
pass `-k`.

**`Could not parse the base file`.** JSON must be a **flat** object of string values — nested objects
are not supported. Validate with `jq . locales/en.json`. `.resx` files must be well-formed XML.

**Rate limits.** Lower `--batch`, or use a lighter model such as `gemini-flash-lite-latest`. The tool
already retries with increasing backoff.

**A translation is wrong or should not happen.** Add a rule to your custom prompt, or fix the value
by hand and run `--ignore-changes` so it is not overwritten.

## Contributing

```bash
dotnet build
dotnet test
```

See [CLAUDE.md](CLAUDE.md) for repository layout, architecture notes, and the release process.

## License

Open source (LGPL) — see [LICENSE](LICENSE).
