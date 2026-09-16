#!/usr/bin/env python3
"""Build WinBoard FR/EN swipe lexicons from license-clean sources.

Pipeline (dev-only; the app never touches the network):
  1. Rank tokens with hermitdave/FrequencyWords 2018 (OpenSubtitles / OPUS).
  2. Keep a spelling only if it appears in a clean allowlist:
       FR — Lexique 3.83 (CC BY-SA 4.0)
       EN — SCOWL 2020.12.07 words+contractions, size ≤ 80 (MIT-like)
  3. Always inject a short list of everyday plain-letter forms.
  4. Reject punctuation/whitespace: swipe entries are one letter-only token.

Output: frequency-ordered one-word-per-line files under src/WinBoard/Assets/.
"""

from __future__ import annotations

import argparse
import csv
import io
import sys
import tarfile
import unicodedata
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "WinBoard" / "Assets"
CACHE = Path(__file__).resolve().parent / ".cache"

FREQ_URLS: dict[str, list[str]] = {
    "fr": [
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/fr/fr_full.txt",
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/fr/fr_50k.txt",
    ],
    "en": [
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_full.txt",
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/en/en_50k.txt",
    ],
}

LEXIQUE_URL = "http://www.lexique.org/databases/Lexique383/Lexique383.tsv"
SCOWL_URL = "https://downloads.sourceforge.net/project/wordlist/SCOWL/2020.12.07/scowl-2020.12.07.tar.gz"

SCOWL_PREFIXES = (
    "english-words.",
    "american-words.",
    "british-words.",
    "english-contractions.",
    "american-contractions.",
    "british-contractions.",
)
SCOWL_MAX_LEVEL = 80

CRITICAL: dict[str, list[str]] = {
    "fr": [
        "comment",
        "content",
        "comme",
        "commencer",
        "bonjour",
        "merci",
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
        "think",
        "please",
        "thanks",
        "its",
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
    return "".join(ch.lower() for ch in normalized if "a" <= ch.lower() <= "z")


def is_swipe_token(word: str) -> bool:
    if not word:
        return False
    # Compounds/contractions become misleading long candidates when punctuation
    # is stripped by folding. Swipe lexicons only contain Unicode letters.
    if not all(unicodedata.category(ch).startswith("L") for ch in word):
        return False
    folded = fold_letters(word)
    return MIN_FOLDED <= len(folded) <= MAX_FOLDED


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    print(f"Downloading {url}", file=sys.stderr)
    req = urllib.request.Request(url, headers={"User-Agent": "WinBoard-dictionary-generator"})
    with urllib.request.urlopen(req, timeout=180) as response, dest.open("wb") as out:
        out.write(response.read())


def cached(url: str, cache_dir: Path, filename: str) -> Path:
    dest = cache_dir / filename
    if not dest.exists() or dest.stat().st_size == 0:
        download(url, dest)
    return dest


def load_frequency_source(language: str, cache_dir: Path, source_dir: Path | None) -> Path:
    if source_dir is not None:
        for name in (f"{language}_full.txt", f"{language}_50k.txt"):
            path = source_dir / name
            if path.exists():
                return path
        raise FileNotFoundError(f"No {language} FrequencyWords file in {source_dir}")

    last_error: Exception | None = None
    for url in FREQ_URLS[language]:
        name = url.rsplit("/", 1)[-1]
        dest = cache_dir / name
        if not dest.exists() or dest.stat().st_size == 0:
            try:
                download(url, dest)
            except Exception as exc:  # noqa: BLE001
                last_error = exc
                print(f"Failed {url}: {exc}", file=sys.stderr)
                continue
        if dest.exists() and dest.stat().st_size > 0:
            return dest
    raise RuntimeError(f"Could not download a {language} frequency list") from last_error


def load_lexique_allowlist(cache_dir: Path) -> set[str]:
    path = cached(LEXIQUE_URL, cache_dir, "Lexique383.tsv")
    allow: set[str] = set()
    with path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        for row in reader:
            ortho = (row.get("ortho") or "").strip()
            if not ortho:
                continue
            key = fold_letters(ortho)
            if MIN_FOLDED <= len(key) <= MAX_FOLDED:
                allow.add(key)
    if len(allow) < 20_000:
        raise RuntimeError(f"Lexique allowlist too small: {len(allow)}")
    return allow


def load_scowl_allowlist(cache_dir: Path) -> set[str]:
    archive = cached(SCOWL_URL, cache_dir, "scowl-2020.12.07.tar.gz")
    allow: set[str] = set()
    with tarfile.open(archive, "r:gz") as tar:
        for member in tar.getmembers():
            name = Path(member.name).name
            if not member.isfile() or not name.startswith(SCOWL_PREFIXES):
                continue
            try:
                level = int(name.rsplit(".", 1)[-1])
            except ValueError:
                continue
            if level > SCOWL_MAX_LEVEL:
                continue
            extracted = tar.extractfile(member)
            if extracted is None:
                continue
            text = io.TextIOWrapper(extracted, encoding="utf-8", errors="ignore")
            for line in text:
                word = line.strip().lower()
                if not word:
                    continue
                key = fold_letters(word)
                if MIN_FOLDED <= len(key) <= MAX_FOLDED:
                    allow.add(key)
    if len(allow) < 20_000:
        raise RuntimeError(f"SCOWL allowlist too small: {len(allow)}")
    return allow


def parse_frequency_file(path: Path) -> list[str]:
    ordered: list[str] = []
    seen: set[str] = set()
    with path.open(encoding="utf-8") as handle:
        for raw in handle:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            word = line.split()[0].replace("’", "'")
            if not is_swipe_token(word):
                continue
            key = fold_letters(word)
            if key in seen:
                continue
            seen.add(key)
            ordered.append(word)
    return ordered


def select_words(language: str, ranked: list[str], allow: set[str]) -> list[str]:
    selected: list[str] = []
    selected_keys: set[str] = set()
    for word in ranked:
        key = fold_letters(word)
        if key not in allow:
            continue
        selected.append(word)
        selected_keys.add(key)

    insert_at = min(INJECT_AT, len(selected))
    for word in CRITICAL[language]:
        if not is_swipe_token(word):
            raise RuntimeError(f"Invalid punctuation in critical {language} swipe word: {word!r}")
        key = fold_letters(word)
        if key in selected_keys:
            continue
        selected.insert(insert_at, word)
        selected_keys.add(key)
        insert_at += 1
        print(f"Injected missing {language} word: {word}", file=sys.stderr)

    return selected


def write_lexicon(language: str, words: list[str], dest: Path) -> None:
    label = "French" if language == "fr" else "English"
    header = [
        f"# WinBoard swipe lexicon ({label})",
        "# Ranked from hermitdave/FrequencyWords 2018 (OpenSubtitles / OPUS)",
        "# Filtered with Lexique383 (FR) / SCOWL 2020.12.07 (EN)",
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
    parser.add_argument("--source-dir", type=Path, default=None, help="Optional FrequencyWords directory")
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)

    allowlists = {
        "fr": load_lexique_allowlist(args.cache),
        "en": load_scowl_allowlist(args.cache),
    }
    print(f"allow FR={len(allowlists['fr'])} EN={len(allowlists['en'])}", file=sys.stderr)

    for language in ("fr", "en"):
        source = load_frequency_source(language, args.cache, args.source_dir)
        ranked = parse_frequency_file(source)
        words = select_words(language, ranked, allowlists[language])[: args.limit]
        if len(words) < 20_000:
            raise SystemExit(f"{language}: only {len(words)} words after filtering (need ≥ 20000)")

        dest = ASSETS / f"words_{language}.txt"
        write_lexicon(language, words, dest)
        size_kb = dest.stat().st_size / 1024
        print(f"{language}: {len(words)} words → {dest} ({size_kb:.0f} KiB)")

        present = {fold_letters(word) for word in words}
        missing = [word for word in CRITICAL[language] if fold_letters(word) not in present]
        if missing:
            raise SystemExit(f"{language}: still missing {missing}")
        if language == "fr" and fold_letters("coment") in present:
            raise SystemExit("French list still contains subtitle typo 'coment'")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
