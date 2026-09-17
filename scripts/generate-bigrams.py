#!/usr/bin/env python3
"""Write compact offline FR/EN bigram tables for WinBoard 0.7.6.

License-clean: closed-class left contexts paired with high-frequency
content words from the same FrequencyWords 2018 rank family used by
generate-dictionaries.py (CC BY-SA 4.0). No word-pair blacklist — these
are positive P(w|prev) weights only.

The app never runs this script.
"""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "WinBoard" / "Assets"

# prev, word, weight (higher = more common continuation)
FR: list[tuple[str, str, int]] = [
    ("je", "suis", 90), ("je", "vais", 80), ("je", "veux", 70), ("je", "peux", 65),
    ("je", "sais", 60), ("je", "dois", 50), ("je", "fais", 55), ("je", "crois", 40),
    ("tu", "es", 85), ("tu", "vas", 70), ("tu", "peux", 60), ("tu", "veux", 55),
    ("tu", "sais", 50), ("il", "est", 95), ("il", "faut", 80), ("il", "y", 40),
    ("elle", "est", 80), ("on", "peut", 70), ("on", "va", 65), ("nous", "avons", 60),
    ("nous", "sommes", 55), ("vous", "etes", 70), ("vous", "avez", 60), ("vous", "pouvez", 50),
    ("cest", "pas", 40), ("cest", "bien", 35),
    ("et", "comment", 90), ("et", "puis", 70), ("et", "aussi", 50), ("et", "alors", 45),
    ("mais", "comment", 95), ("mais", "bonjour", 30), ("mais", "pourquoi", 60),
    ("mais", "oui", 50), ("mais", "non", 55), ("mais", "cest", 40),
    ("alors", "comment", 80), ("alors", "bonjour", 25), ("alors", "voila", 40),
    ("donc", "comment", 70), ("donc", "voila", 35),
    ("oui", "comment", 40), ("oui", "bonjour", 30), ("oui", "merci", 45),
    ("non", "comment", 35), ("bonjour", "comment", 50), ("bonjour", "bonjour", 10),
    ("merci", "beaucoup", 80), ("merci", "comment", 25),
    ("sais", "comment", 75), ("sait", "comment", 70), ("savais", "comment", 40),
    ("voir", "comment", 65), ("vois", "comment", 60), ("dire", "comment", 70),
    ("dis", "comment", 50), ("explique", "comment", 55), ("expliquer", "comment", 50),
    ("comprends", "comment", 45), ("comprendre", "comment", 50),
    ("demande", "comment", 40), ("demander", "comment", 40),
    ("montre", "comment", 35), ("juste", "comment", 45), ("surtout", "comment", 40),
    ("apres", "comment", 30), ("pour", "comment", 35), ("sans", "comment", 25),
    ("le", "monde", 50), ("le", "temps", 45), ("la", "vie", 50), ("la", "peine", 30),
    ("un", "peu", 70), ("une", "fois", 60), ("des", "fois", 40),
    ("de", "rien", 35), ("du", "coup", 50), ("au", "moins", 40),
    ("pas", "du", 30), ("pas", "tres", 35), ("tres", "bien", 55), ("tres", "bon", 40),
    ("tout", "le", 40), ("tous", "les", 45), ("plus", "tard", 50), ("plus", "rien", 30),
    ("comme", "ca", 40), ("comme", "quoi", 25),
    ("qui", "est", 50), ("que", "tu", 40), ("que", "je", 45),
    ("si", "tu", 50), ("si", "vous", 40), ("si", "je", 45),
    ("je", "comment", 15), ("vous", "comment", 40), ("tu", "comment", 20),
    ("fait", "comment", 30), ("faire", "comment", 45), ("va", "comment", 25),
    ("cest", "comment", 35), ("etait", "comment", 20),
]

EN: list[tuple[str, str, int]] = [
    ("i", "am", 80), ("i", "think", 70), ("i", "have", 75), ("i", "want", 60),
    ("i", "can", 55), ("i", "would", 50), ("i", "will", 50), ("i", "know", 45),
    ("you", "are", 85), ("you", "can", 70), ("you", "have", 65), ("you", "know", 50),
    ("we", "are", 70), ("we", "have", 60), ("we", "can", 55),
    ("they", "are", 65), ("they", "have", 50), ("it", "is", 90), ("it", "was", 60),
    ("this", "is", 80), ("that", "is", 75), ("that", "was", 45),
    ("and", "then", 70), ("and", "the", 60), ("and", "i", 50),
    ("but", "i", 55), ("but", "the", 40), ("but", "you", 45),
    ("thank", "you", 95), ("please", "help", 40), ("please", "let", 30),
    ("how", "are", 80), ("how", "is", 50), ("how", "do", 60), ("how", "can", 45),
    ("what", "is", 70), ("what", "are", 50), ("what", "do", 55),
    ("the", "people", 40), ("the", "same", 35), ("the", "best", 40),
    ("a", "little", 50), ("a", "lot", 55), ("a", "few", 40),
    ("to", "the", 45), ("to", "be", 60), ("to", "do", 50), ("to", "get", 40),
    ("of", "the", 70), ("in", "the", 75), ("on", "the", 60), ("for", "the", 55),
    ("have", "been", 50), ("has", "been", 45), ("will", "be", 50),
    ("can", "you", 55), ("could", "you", 50), ("would", "you", 50),
    ("let", "me", 45), ("let", "us", 30),
    ("hello", "there", 40), ("good", "morning", 50), ("good", "night", 45),
    ("please", "comment", 35), ("to", "comment", 30), ("a", "comment", 40),
    ("the", "comment", 25), ("and", "hello", 20), ("but", "hello", 15),
    ("i", "hello", 10), ("just", "hello", 15),
]


def write_table(language: str, pairs: list[tuple[str, str, int]]) -> None:
    label = "French" if language == "fr" else "English"
    dest = ASSETS / f"bigrams_{language}.txt"
    header = [
        f"# WinBoard compact bigrams ({label})",
        "# Offline P(word|prev) weights. Not a word-pair blacklist.",
        "# Left contexts are high-frequency function words; rights are",
        "# high-frequency content words (FrequencyWords 2018 rank family).",
        "# License: CC-BY-SA-4.0 — see Assets/DICTIONARIES.md",
        "# Generated by scripts/generate-bigrams.py — weights are relative.",
        "# Format: prev word weight",
    ]
    dest.parent.mkdir(parents=True, exist_ok=True)
    with dest.open("w", encoding="utf-8", newline="\n") as handle:
        for line in header:
            handle.write(line + "\n")
        for prev, word, weight in pairs:
            handle.write(f"{prev} {word} {weight}\n")
    print(f"{language}: {len(pairs)} bigrams → {dest}")


def main() -> int:
    write_table("fr", FR)
    write_table("en", EN)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
