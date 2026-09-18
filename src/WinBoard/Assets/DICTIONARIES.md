# Dictionnaires swipe WinBoard

Les listes `words_fr.txt` et `words_en.txt` sont des **fichiers locaux** embarqués dans `WinBoard.Core`, ainsi que les tables compactes `bigrams_fr.txt` / `bigrams_en.txt`. L’application **ne télécharge rien** au runtime : pas d’API cloud, pas de télémétrie, pas de requête réseau pour les suggestions.

Format : une entrée par ligne, **ordre de fréquence décroissant** (la première ligne utile = mot le plus courant). Les lignes `#` sont des commentaires ignorés par `WordList.FromLines`. Un poids numérique optionnel après le mot est accepté mais non requis (le rang suffit).

## Pipeline

1. **Ordre de fréquence** : [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) **2018**, dérivé d’**OpenSubtitles 2018** via [OPUS](https://opus.nlpl.eu/).
2. **Allowlist** (évite les fautes de sous-titres du type *coment*) :
   - FR : [Lexique 3.83](http://www.lexique.org/) (`ortho`)
   - EN : [SCOWL 2020.12.07](http://wordlist.aspell.net/) — `english` / `american` / `british` **words** + **contractions**, taille ≤ 80
3. Filtrage swipe strict : uniquement des mots composés de lettres Unicode (accents conservés) ; tirets, apostrophes, espaces et autre ponctuation sont exclus pour éviter que leur repli ne crée de faux composés longs.
4. Troncature à ~100 000 formes uniques (repli d’accents) par langue.

| Langue | Fichier WinBoard | Cible |
| --- | --- | --- |
| Français | `words_fr.txt` | ~100 000 |
| English | `words_en.txt` | ~100 000 |

Le script `scripts/generate-dictionaries.py` retombe sur les coupes FrequencyWords `fr_50k.txt` / `en_50k.txt` si le fichier *full* est indisponible.

## Licences

Les fichiers générés (`words_fr.txt`, `words_en.txt`) sont une **adaptation** sous **[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)** (ShareAlike). Le reste du code WinBoard n’est pas relicensé.

| Source | Rôle | Licence |
| --- | --- | --- |
| [hermitdave/FrequencyWords](https://github.com/hermitdave/FrequencyWords) 2018 | rang / fréquence | contenu **CC BY-SA 4.0** (code du dépôt : MIT) |
| [Lexique 3.83](http://www.lexique.org/databases/Lexique383/README-Lexique.txt) | allowlist FR | **CC BY-SA 4.0** |
| [SCOWL 2020.12.07](http://wordlist.aspell.net/) (Kevin Atkinson) | allowlist EN | notice MIT-like ci-dessous |

Attribution :

> Word lists adapted from hermitdave/FrequencyWords (2018, OpenSubtitles / OPUS) and filtered with Lexique 3.83 and SCOWL 2020.12.07. FrequencyWords + Lexique: CC BY-SA 4.0.

Notice SCOWL (extrait de `Copyright`, Kevin Atkinson) :

> Permission to use, copy, modify, distribute and sell these word lists, the associated scripts, the output created from the scripts, and its documentation for any purpose is hereby granted without fee, provided that the above copyright notice appears in all copies and that both that copyright notice and this permission notice appear in supporting documentation. Kevin Atkinson makes no representations about the suitability of this array for any purpose. It is provided "as is" without express or implied warranty.

Aucune liste propriétaire (Hunspell commercial, dictionnaires éditeur payants, etc.) n’est redistribuée.

## Régénération (Linux / CI)

Ne s’exécute **que** pour reconstruire les fichiers versionnés. Le clavier n’appelle pas ce script.

```bash
python3 scripts/generate-dictionaries.py
```

Options utiles :

```bash
python3 scripts/generate-dictionaries.py --limit 100000
python3 scripts/generate-dictionaries.py --source-dir /chemin/vers/FrequencyWords
```

Les téléchargements (FrequencyWords, Lexique383.tsv, archive SCOWL) sont mis en cache dans `scripts/.cache/` (ignoré par git).

## Chargement

`WordList.LoadLanguage("fr"|"en")` lit la ressource embarquée. Le décodeur (v0.8.3) suit `geste → scores spatiaux → beam trie → n-grammes`. SHARK2 est retiré. Canal principal = **location** (0,90) + DTW à bande étroite (0,45 / 12 %) + **ancres** début/fin (2,8). Hit-keys **centre** (poids 5,5) dominent la forme ; graze milieu plus doux (2,2 / 0,62). La longueur **l’emporte** sur le prior de langue. Les n-grammes (poids 0,38, verrou 0,48) ne rescorent que des voisins spatiaux proches, après le filtre de longueur.

L’étage spatial par défaut (`GeometricSpatialEncoder`) score les lettres voisines avec une gaussienne (σ ≈ 0,52 pitch) pour le beam ; un croisement de centre (0,32) n’est pas un graze. Un module neuronal futur n’a qu’à implémenter `ISpatialEncoder`.

## Bigrammes

Tables compactes hors-ligne (quelques centaines de paires), **pas** une liste noire. Contextes de gauche = mots-outils fréquents ; droites = formes fréquentes de la même famille FrequencyWords 2018 (CC BY-SA 4.0). Régénération :

```bash
python3 scripts/generate-bigrams.py
```
