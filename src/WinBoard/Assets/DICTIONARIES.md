# Dictionnaires swipe WinBoard

Les listes `words_fr.txt` et `words_en.txt` sont des **fichiers locaux** embarqués dans `WinBoard.Core`. L’application **ne télécharge rien** au runtime : pas d’API cloud, pas de télémétrie, pas de requête réseau pour les suggestions.

Format : une entrée par ligne, **ordre de fréquence décroissant** (la première ligne utile = mot le plus courant). Les lignes `#` sont des commentaires ignorés par `WordList.FromLines`. Un poids numérique optionnel après le mot est accepté mais non requis (le rang suffit).

## Source

Les lexiques sont adaptés de **[hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords)** (coupure **2018**), elles-mêmes dérivées du corpus **OpenSubtitles 2018** publié via [OPUS](https://opus.nlpl.eu/).

| Langue | Fichier source | Fichier WinBoard | Taille cible |
| --- | --- | --- | --- |
| Français | `content/2018/fr/fr_full.txt` | `words_fr.txt` | ~100 000 formes uniques |
| English | `content/2018/en/en_full.txt` | `words_en.txt` | ~100 000 formes uniques |

Le script `scripts/generate-dictionaries.py` retombe sur les coupes `fr_50k.txt` / `en_50k.txt` si le fichier *full* est indisponible.

## Licence

- **Code** du dépôt FrequencyWords : MIT
- **Contenu** des listes de fréquences : [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)
- **Adaptation WinBoard** (`words_fr.txt`, `words_en.txt`) : **CC BY-SA 4.0** (ShareAlike)

Attribution requise :

> Word lists adapted from [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) (2018), based on OpenSubtitles 2018 / OPUS. Licensed under CC BY-SA 4.0.

Modifications par rapport à la source :

- conservation des tokens utilisables en swipe (lettres, apostrophe / tiret internes)
- rejet des chiffres, fragments (`c'`, `'s`, `comment-`) et doublons après repli d’accents
- troncature aux ~100 000 formes les plus fréquentes par langue
- une graphie par ligne, sans le compte brut OpenSubtitles

Le reste du code WinBoard n’est **pas** relicensé en CC BY-SA : seuls ces fichiers de lexique (et leurs dérivés) le sont.

Aucune liste propriétaire (Hunspell commercial, dictionnaires éditeur, etc.) n’est redistribuée.

## Régénération (Linux / CI)

Ne s’exécute **que** pour reconstruire les fichiers versionnés. Le clavier n’appelle pas ce script.

```bash
python3 scripts/generate-dictionaries.py
```

Options utiles :

```bash
python3 scripts/generate-dictionaries.py --limit 100000
python3 scripts/generate-dictionaries.py --source-dir /chemin/vers/listes
```

Les téléchargements sont mis en cache dans `scripts/.cache/` (ignoré par git).

## Chargement

`WordList.LoadLanguage("fr"|"en")` lit la ressource embarquée. Le décodeur swipe (`SwipeDecoder`) n’utilise ces listes que localement, comme départage de fréquence **après** le score géométrique.
