# Test vectors — cross-platform conversion contract

> **Draft.** Seeded as part of [#42](https://github.com/rashn/RuSwitcher/issues/42)
> (Greek in pair mode). The format is open to change per the maintainer's
> preference; every platform implementation must pass the same vectors.

## Format

Each file is JSON: `{ "schema": 1, "description": ..., "cases": [...] }`.

Input is expressed as **physical keys** (the US-QWERTY base character of each
key, lowercase; uppercase = Shift held), not as a typed string — with dead keys
the typed string differs between OSes, the key sequence does not.

Case fields:

| field          | meaning |
|----------------|---------|
| `name`         | unique id of the case |
| `keys`         | physical key sequence, US-QWERTY base chars |
| `sourceLayout` | layout the user actually typed in (`en`, `ru`, `el`, …) |
| `targetLayout` | candidate layout the word is converted into |
| `typed`        | expected rendering of `keys` in `sourceLayout` (after the shared dead-key composition rule + NFC) |
| `converted`    | expected rendering of `keys` in `targetLayout` (same rule) |
| `decide`       | expected `LayoutDetector.decide` verdict: `switchToConverted` / `keep` / `undecided`; omitted when the case only tests key→text conversion |
| `dict`         | dictionary stub for `decide` cases: map lang → list of words the stub accepts; the ShortWords frequency lists are bundled and always in force |
| `note`         | human context |

## Dead-key composition rule (shared)

A spacing accent produced by a dead key attaches as a combining mark to the
next base character, then the string is NFC-normalized:

- U+0384 GREEK TONOS → U+0301 COMBINING ACUTE
- U+00A8 DIAERESIS → U+0308 COMBINING DIAERESIS
- U+0385 GREEK DIALYTIKA TONOS → U+0308 + U+0301

A trailing accent with no following base character stays as the spacing char.
This is portable and needs no OS dead-key state.
