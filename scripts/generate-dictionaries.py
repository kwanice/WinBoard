#!/usr/bin/env python3
"""Build WinBoard FR/EN swipe lexicons from license-clean frequency lists.

Source: hermitdave/FrequencyWords 2018 (OpenSubtitles / OPUS).
Content license: CC-BY-SA-4.0. This script's code is original to WinBoard.

Downloads happen only when regenerating; the app never touches the network.
"""

from __future__ import annotations

import argparse
import sys
import unicodedata
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "WinBoard" / "Assets"
CACHE = Path(__file__).resolve().parent / ".cache"

# Prefer the full 2018 lists; fall back to the 50k cuts if the full file is unavailable.
SOURCES: dict[str, list[str]] = {
    "fr": [
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/fr/fr_full.txt",
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/fr/fr_50k.txt",
    ],
    "en": [
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_full.txt",
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_50k.txt",
    ],
}

# Everyday words that must appear even if a fallback 50k cut dropped them.
CRITICAL: dict[str, list[str]] = {
    "fr": [
        "comment",
        "content",
        "comme",
        "commencer",
        "bonjour",
        "merci",
        "aujourd'hui",
        "être",
        "avoir",
        "faire",
        "aller",
        "pouvoir",
        "vouloir",
        "parce",
        "très",
        "aussi",
        "alors",
        "cette",
        "c'est",
    ],
    "en": [
        "the",
        "and",
        "you",
        "that",
        "have",
        "comment",
        "content",
        "because",
        "people",
        "would",
        "could",
        "should",
        "which",
        "there",
        "their",
        "about",
        "would",
        "think",
        "please",
        "thanks",
    ],
}

INJECT_AT = 80
DEFAULT_LIMIT = 100_000
MIN_FOLDED = 2
MAX_FOLDED = 24


def fold_letters(value: str) -> str:
    expanded = (
        value.replace("œ", "oe")
        .replace("Œ", "oe")
        .replace("æ", "ae")
        .replace("Æ", "ae")
        .replace("ß", "ss")
    )
    normalized = unicodedata.normalize("NFD", expanded)
    return "".join(
        ch.lower()
        for ch in normalized
        if "a" <= ch.lower() <= "z"
    )


def is_swipe_token(word: str) -> bool:
    word = word.replace("’", "'").replace("`", "'")
    if not word:
        return False
    for i, ch in enumerate(word):
        category = unicodedata.category(ch)
        if category.startswith("L"):
            continue
        if ch in "'-" and 0 < i < len(word) - 1:
            continue
        return False
    folded = fold_letters(word)
    return MIN_FOLDED <= len(folded) <= MAX_FOLDED


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"Downloading {url}", file=sys.stderr)
    req = urllib.request.Request(url, headers={"User-Agent": "WinBoard-dictionary-generator"})
    with urllib.request.urlopen(req, timeout=120) as response, dest.open("wb") as out:
        out.write(response.read())


def load_source(language: str, cache_dir: Path) -> Path:
    last_error: Exception | None = None
    for index, url in enumerate(SOURCES[language]):
        name = url.rsplit("/", 1)[-1]
        dest = cache_dir / name
        if not dest.exists() or dest.stat().st_size == 0:
            try:
                download(url, dest)
            except Exception as exc:  # noqa: BLE001 — try the next mirror-style URL
                last_error = exc
                print(f"Failed {url}: {exc}", file=sys.stderr)
                continue
        if dest.exists() and dest.stat().st_size > 0:
            if index > 0:
                print(f"Using fallback {name}", file=sys.stderr)
            return dest
    raise RuntimeError(f"Could not download a {language} frequency list") from last_error


def parse_frequency_file(path: Path) -> list[str]:
    ordered: list[str] = []
    seen: set[str] = set()
    with path.open(encoding="utf-8") as handle:
        for raw in handle:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            word = line.split()[0]
            word = word.replace("’", "'")
            if not is_swipe_token(word):
                continue
            key = fold_letters(word)
            if key in seen:
                continue
            seen.add(key)
            ordered.append(word)
    return ordered


def inject_critical(language: str, words: list[str]) -> None:
    present = {fold_letters(word) for word in words}
    insert_at = min(INJECT_AT, len(words))
    for word in CRITICAL[language]:
        key = fold_letters(word)
        if key in present:
            continue
        words.insert(insert_at, word)
        present.add(key)
        insert_at += 1
        print(f"Injected missing {language} word: {word}", file=sys.stderr)


def write_lexicon(language: str, words: list[str], dest: Path) -> None:
    label = "French" if language == "fr" else "English"
    header = [
        f"# WinBoard swipe lexicon ({label})",
        "# Adapted from hermitdave/FrequencyWords 2018 (OpenSubtitles / OPUS)",
        "# License: CC-BY-SA-4.0 — see Assets/DICTIONARIES.md",
        "# Generated by scripts/generate-dictionaries.py — do not edit by hand",
    ]
    dest.parent.mkdir(parents=True, exist_ok=True)
    with dest.open("w", encoding="utf-8", newline="\n") as handle:
        for line in header:
            handle.write(line + "\n")
        for word in words:
            handle.write(word + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--limit", type=int, default=DEFAULT_LIMIT, help="Max unique words per language")
    parser.add_argument("--cache", type=Path, default=CACHE, help="Download cache directory")
    parser.add_argument("--source-dir", type=Path, default=None, help="Use already-downloaded {fr,en}_full.txt here")
    args = parser.parse_args()

    for language in ("fr", "en"):
        if args.source_dir is not None:
            candidates = [
                args.source_dir / f"{language}_full.txt",
                args.source_dir / f"{language}_50k.txt",
            ]
            source = next((path for path in candidates if path.exists()), None)
            if source is None:
                raise FileNotFoundError(f"No {language} source list in {args.source_dir}")
        else:
            source = load_source(language, args.cache)

        words = parse_frequency_file(source)
        inject_critical(language, words)
        words = words[: args.limit]
        if len(words) < 20_000:
            raise SystemExit(f"{language}: only {len(words)} words after filtering (need ≥ 20000)")

        dest = ASSETS / f"words_{language}.txt"
        write_lexicon(language, words, dest)
        size_kb = dest.stat().st_size / 1024
        print(f"{language}: {len(words)} words → {dest} ({size_kb:.0f} KiB)")

        missing = [word for word in CRITICAL[language] if fold_letters(word) not in {fold_letters(w) for w in words}]
        if missing:
            raise SystemExit(f"{language}: still missing {missing}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
