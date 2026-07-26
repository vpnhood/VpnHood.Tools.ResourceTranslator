# VpnHood.Tools.ResourceTranslator

**Keep your translations up to date automatically.** Edit your base language file — or your
website's pages — run one command, and every other locale catches up: placeholders, HTML,
and formatting intact.

[![NuGet](https://img.shields.io/nuget/v/VpnHood.Tools.ResourceTranslator.svg)](https://www.nuget.org/packages/VpnHood.Tools.ResourceTranslator)
[![Build](https://github.com/vpnhood/VpnHood.Tools.ResourceTranslator/actions/workflows/build.yml/badge.svg)](https://github.com/vpnhood/VpnHood.Tools.ResourceTranslator/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/license-LGPL--2.1-blue.svg)](LICENSE)

```console
$ vhtranslator                       # resource files (JSON / .resx)
Processing fr.json (fr) - 3 changed, 1 missing entries...
✓ fr.json: 4 translated/updated.
Done.

$ vhtranslator site                  # static website (Jekyll-style)
✓ fr/free-vpn/index.html: translated.
✓ de/free-vpn/index.html: translated.
Done.
```

The tool has two modes that share the same engines, configuration file, options, and workflow:

| Mode | Command | Translates | Safety guarantee |
| --- | --- | --- | --- |
| **Resource files** | `vhtranslator` | JSON / Microsoft `.resx` values | Placeholders and HTML tags preserved |
| **Static website** | `vhtranslator site` | Whole Jekyll-style pages into per-language folders | Every page structurally verified; unverifiable pages are never written |

Backed by Google Gemini, OpenAI, or Grok. Ships as a .NET tool, so any repository can adopt it
without vendoring code.

---

## Why

Machine-translating everything on every build is slow, expensive, and overwrites work your
translators did by hand. This tool tracks the **source** behind every key and every page, so a
run only sends what actually changed — usually a handful of strings or pages. Everything else is
left exactly as it is. And because website translations often ship **without human review**, the
site mode refuses to write any page it cannot prove structurally intact.

- 🔄 **Incremental** — only changed entries/pages are retranslated; hand-written work survives
- 🎯 **Placeholder-safe** — `{variables}`, HTML tags, Liquid tags, and URLs come back intact
- 🛡️ **Verified** — translated pages are checked element-by-element and rejected on any damage
- ↔️ **RTL-aware** — for right-to-left targets (fa, ar, he, ur) an invisible direction mark keeps
  trailing Latin `!`/`?` — think "VpnHood!" — rendering on the correct side
- 🤖 **Multi-engine** — Gemini, OpenAI, or Grok, inferred from the model name
- 🗂️ **Zero-argument runs** — commit a `vhtranslator.json` and just run `vhtranslator`
- 🔧 **Your terminology** — project glossaries and per-key rules
- 🏗️ **CI-friendly** — retries with backoff, and stable exit codes

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

## Part 1 — Translating resource files

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

### A folder per language

Projects that split their strings across many files keep them in one folder per language.
Point `--base` at the **folder** — its name is the source language — and every resource file
inside is translated to sibling language folders:

```bash
vhtranslator --base i18n/en
```

```text
i18n/
├── en/            ← base folder (never modified)
│   ├── home.json
│   └── about.json
├── fr/            ← updated file by file
│   ├── home.json
│   └── about.json
└── fa/            ← created when listed under "languages"
```

Everything else works exactly as with a single file: only changed or missing entries are
translated, and `--show-changes`, `--rebuild-lang`, and `--ignore-changes` apply per file.
Bookkeeping goes next to your `vhtranslator.json` when one exists, in a subfolder named after
the language trees' parent (`vh_translator/watches/i18n/home_watch.json` for `--base i18n/en`),
so data trees that are consumed verbatim — a Jekyll `_data/` folder, an app bundle — stay clean.

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

### Configuration

Drop a `vhtranslator.json` in your repository and the tool needs no arguments at all. It is found
by walking up from the base file (or the working directory), checking each folder **and its
`vh_translator/` folder** — keep it at the root or tuck it away, both work. Relative paths always
resolve against the repo/site root.

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
| `base` | Base language file — or language folder (see above) — relative to the site/repo root |
| `engine` | `gemini`, `gpt`, or `grok`. Inferred from `model` when omitted |
| `model` | Model name. Defaults to `gemini-flash-lite-latest` |
| `batch` | Entries per request. Default `20` |
| `extraPrompt` | Extra instructions file, relative to the site/repo root |
| `languages` | Target languages. **Missing files are created.** Omit to update only existing locale files |

Command-line options always win, so one-off overrides stay easy:

```bash
vhtranslator                       # uses vhtranslator.json
vhtranslator -m gpt-4o-mini        # same config, different model
vhtranslator --config ci/vhtranslator.json
```

### How it decides what to translate

After each successful run the tool records the source text of every key in
`vh_translator/watches/<base>_watch.json`. On the next run a key is translated when:

- its **source text changed** since that record, **or**
- it is **missing or empty** in the target file.

> **Commit the watch file.** Without it nothing is known to be current, so the next run
> retranslates every key in every language.

### Working with .resx

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

## Part 2 — Translating a static website (Jekyll)

### Translate everything that changed

One command translates every stale page into per-language folders that mirror the source paths —
on a Jekyll site the folder path *is* the URL, so `/fr/free-vpn/` just works:

```bash
vhtranslator site
```

```text
site/
├── index.html                ← base pages (never modified)
├── free-vpn/index.html
├── fr/                       ← generated, mirrors the source tree
│   ├── index.html
│   └── free-vpn/index.html
└── de/
    ├── index.html
    └── free-vpn/index.html
```

### Preview first

Lists the stale page/language pairs (and stale `data` entries) and exits. It needs no API key:

```bash
vhtranslator site --show-changes
```

### Add a language

Add the code to `languages` in the config — missing target pages are always created on the next
run. To force-refresh one language that already exists:

```bash
vhtranslator site --rebuild-lang fr     # must be listed under "languages"
```

A language rebuild retranslates that language's pages **and** its `data` files — use it after
switching models to regenerate one language wholesale.

### Adopt an existing site

Mark every current page as translated so the first run only fills in what is missing:

```bash
vhtranslator site --ignore-changes
```

### Review before committing

```bash
vhtranslator site && git diff fr/ de/
```

### Configuration

The `site` section lives in the same `vhtranslator.json`; the top-level `model`, `engine`,
`batch`, and `extraPrompt` keys apply to site runs too.

```json
{
  "model": "gemini-2.5-flash",
  "site": {
    "pages": ["**/index.html"],
    "exclude": ["privacy-policy/**", "terms-of-use/**"],
    "languages": ["fr", "de"],
    "output": "{lang}/{path}",
    "titleMustContain": "MyBrand",
    "data": ["_data/i18n/en"]
  }
}
```

| Key | Description |
| --- | --- |
| `pages` | Globs selecting the source pages. Default `**/index.html` |
| `exclude` | Globs excluded from discovery (e.g. legal pages you keep in the source language). `.git/`, `_site/`, `_includes/`, `node_modules/`, `vh_translator/`, and the generated locale trees are always excluded |
| `languages` | **Required.** Target language codes; each becomes an output folder |
| `output` | Output path template with `{lang}` and `{path}`. Default `{lang}/{path}`. May place files outside the served tree (e.g. a Jekyll collection folder, `_langs/{lang}/{path}`) — every generated page carries an explicit `permalink`, so the URL stays `/{lang}/{path}` regardless |
| `titleMustContain` | Text every translated page title must keep (typically the brand). Skipped with a warning when the source title itself lacks it |
| `data` | Key/value resources (e.g. shared UI strings) translated with the resource-file pipeline as part of every site run, into the same languages. Each entry is a file (`_data/i18n/en.json`) or a language folder (`_data/i18n/en`) |
| `sourceLanguage` | Language of the source pages. Default `en` |
| `pageBody` | `translate` (default) sends whole page bodies to the model. `copy` keeps bodies byte-identical and translates **only** the front-matter `title`/`description` — for sites whose page text lives in i18n data files (see below) |

**The `copy` + `data` pattern.** When every visible string of a page lives in per-language data
files (`_data/i18n/en/home.json` rendered via `page.lang`), the pages themselves contain nothing
to translate except their front-matter title and description. Set `"pageBody": "copy"` and list
the language folder under `data`: a site run then translates the data files key by key, and
generates per-language page copies whose bodies are byte-identical by construction — no
structural verification lottery, a fraction of the tokens, and the `titleMustContain` rule
still applies.

The end state of that pattern moves even `title`/`description` into the data files (as keys the
site injects at build time, e.g. via a small Jekyll plugin) and drops them from front matter:
pages then have **no translatable metadata at all** and are written as pure copies without a
single model call — page metadata gets translated by the incremental key-by-key data pipeline
like every other string.

> **Pin an exact model version** (e.g. `gemini-2.5-flash`, not `-latest`) — when translations
> ship without review, silent model drift means silent output drift.

### Opting content out

Three levels, from coarse to fine — pick the one that matches the shape of the exclusion:

| Level | Where | Use for |
| --- | --- | --- |
| `exclude` globs | `vhtranslator.json` | Whole trees — legal pages, archives |
| `translate: false` | The page's front matter | A single page, decided where the content lives |
| `translate="no"` | Any HTML element | A fragment inside a translated page — an address, a testimonial quote, a brand slogan |

When a previously translated page is opted out (either way), its generated copies are pruned on
the next run.

### How it decides what to translate

After each successful run the tool records a hash of every page in
`vh_translator/watches/pages/site_watch.json`. On the next run a page is translated for a language when:

- its **source content changed** since that record, **or**
- the **target page is missing** for that language.

> **Commit the watch file.** Without it nothing is known to be current, so the next run
> retranslates every page in every language.

### How a page is translated — and verified

1. **Front matter is never model-generated.** Only the `title` and `description` values are
   translated; every other line is copied verbatim, and `lang: <code>`, an
   `auto_translated: true` marker, and an explicit `permalink: /<lang>/<path>/` are added.
2. **Liquid is never model-visible.** `{% ... %}` and `{{ ... }}` tags are masked with opaque
   tokens the model must return untouched, so template syntax cannot be corrupted.
3. **The body is verified fail-closed** against the source: identical element tree, identical
   attributes (only `alt`, `title`, `aria-label`, `placeholder` values may change), untouched
   `script`/`style`/`svg` content, every Liquid token back exactly once, and a sane
   visible-text length. On failure the errors are fed back to the model and the page is
   retried; after 3 attempts it is **not written** (exit code `11`) and the previously
   generated page keeps serving.

### The `auto_translated` marker

The marker in a generated page's front matter is what makes automation safe around human work:

- A target file **without** it is treated as hand-authored and is **never overwritten** — write
  your own `fr/about/index.html` and the tool leaves it alone forever.
- A discovered *source* page carrying it is skipped — a generated page can never be
  retranslated as if it were content.
- When a source page is deleted, its generated counterparts are **pruned automatically** — but
  only files carrying the marker; hand-authored files are never deleted.

## Customizing translations

Add project rules via `--extra-prompt`, the `extraPrompt` config key, or by creating
`vh_translator/custom_prompt.txt` next to your base file / at your site root, which is picked up
automatically. The same rules apply to both modes:

```text
- Keep "VPN", "API", and "JSON" untranslated
- The brand name "VpnHood" never changes
- Use formal tone for German; use Latin American Spanish
- Return "*" for keys ending in _URL
```

Returning `*` skips an entry (resource files only): the existing translation is kept, or the
source text is used if there is none. Useful for brand names, URLs, and region-specific strings.

## Continuous integration

```yaml
- run: dotnet tool restore
- run: dotnet tool run vhtranslator        # resource files
- run: dotnet tool run vhtranslator site   # static website
  env:
    GEMINI_API_KEY: ${{ secrets.GEMINI_API_KEY }}
```

Pair it with a create-pull-request action so translations arrive as reviewable diffs rather than
unattended commits — or commit them directly before the build step when the fail-closed
verification is your review.

## Reference

### Commands and options

```text
vhtranslator [options]           Translate resource files (JSON / .resx)
vhtranslator site [options]      Translate a static website

Shared options:
      --config <path>        Config file to use (default: nearest vhtranslator.json,
                             also found inside vh_translator/)
  -x, --extra-prompt <path>  Extra instructions appended to the AI prompt
  -c, --show-changes         List what would be translated and exit (no API key needed)
  -r, --rebuild-lang <code>  Force retranslation of everything for one language
                             (site mode: the pages and all "data" files)
  -i, --ignore-changes       Mark everything as already translated, without calling the AI
  -k, --api-key <key>        API key (or use the engine's environment variable)
  -m, --model <name>         AI model (default depends on the engine)
  -e, --engine <name>        gemini, gpt, or grok (default: inferred from the model name)
  -n, --batch <number>       Entries per request (default: 20; in site mode this applies
                             to the "data" files — pages always go one per request)
  -?, -h, --help             Show help and usage information
      --version              Show version information

vhtranslator only:
  -b, --base <path>          Base language file (.json / .resx) or language folder (i18n/en)
```

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
| `11` | One or more pages failed verification and were not written (site mode) |

## Troubleshooting

**Everything is retranslated on every run.** The watch file is missing or not committed. Run
once, then commit `vh_translator/`.

**`Missing API key`.** Set `GEMINI_API_KEY`, `OPENAI_API_KEY`, or `GROK_API_KEY` for your engine,
or pass `-k`.

**`Could not parse the base file`.** JSON must be a **flat** object of string values — nested
objects are not supported. Validate with `jq . locales/en.json`. `.resx` files must be
well-formed XML.

**Rate limits.** Lower `--batch`, or use a lighter model such as `gemini-flash-lite-latest`. The
tool already retries with increasing backoff.

**A translation is wrong or should not happen.** Add a rule to your custom prompt, or fix the
value by hand and run `--ignore-changes` so it is not overwritten. On a site, hand-fixing a
*generated* page does not survive the next source edit — put the rule in the custom prompt, or
remove the `auto_translated` marker to own that page permanently.

**A page keeps failing verification (exit `11`).** Read the reported errors — the model is
damaging structure (dropped elements, lost placeholders). Try a stronger model. Meanwhile the
previously generated page keeps serving; nothing broken is ever written.

**"skipped — carries the 'auto_translated' marker".** A generated page was discovered as a
source: your `pages`/`exclude` globs or a non-default `output` pattern let a locale tree leak
into discovery. Fix the globs; the marker guard is the safety net, not the mechanism.

## Contributing

```bash
dotnet build
dotnet test
```

See [CLAUDE.md](CLAUDE.md) for repository layout, architecture notes, and the release process.

## License

Open source (LGPL) — see [LICENSE](LICENSE).
