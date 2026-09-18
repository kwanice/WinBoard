# WinBoard

**Version 0.8.7**

Clavier tactile flottant bilingue **FR/EN** pour Windows, façon Gboard. Il reste au-dessus des autres fenêtres, n’envoie **pas** le focus vers lui-même, et injecte les caractères dans l’application déjà active via Win32 `SendInput`.

Windows uniquement. **Saisie par glissement (swipe typing)** : pipeline **spatial → beam (trie) → n-grammes**, 100 % local. SHARK2 est retiré.

## Stack

- **WinUI 3** + **C# / .NET 9** (`net9.0-windows10.0.19041.0`)
- **Windows App SDK 2.4.0** (application bureau non empaquetée / unpackaged)
- **Windows SDK BuildTools** 10.0.28000.2705
- Intégration native Win32 : `WS_EX_NOACTIVATE` + `SendInput` (`KEYEVENTF_UNICODE`) + icône de notification `Shell_NotifyIcon` (pas de paquet NuGet tray)

## Prérequis

1. **Windows 10** (2004 / build 19041) ou **Windows 11**
2. **.NET 9 SDK** — [dotnet.microsoft.com/download/dotnet/9.0](https://dotnet.microsoft.com/download/dotnet/9.0)
3. **Visual Studio 2022** (17.12 ou plus récent recommandé, pour .NET 9) avec :
   - charge de travail **Développement d’applications Windows** (*Windows application development*)
   - composants **.NET 9** et **Windows App SDK C# Templates**
   - Windows SDK **10.0.19041** (ou plus récent)
4. **Windows App Runtime 2.4** — requis pour lancer l’application non empaquetée. Si vous voyez encore une erreur mentionnant le **runtime 1.7** (ancienne version), installez le runtime **2.4** :
   - Installateur x64 : [aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe](https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe)
   - Installateur x86 : [aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x86.exe](https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x86.exe)
   - Installateur ARM64 : [aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-arm64.exe](https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-arm64.exe)
   - Redistribuable (ZIP, toutes archi) : [aka.ms/windowsappsdk/2.4/2.4.0/Microsoft.WindowsAppRuntime.Redist.2.4.zip](https://aka.ms/windowsappsdk/2.4/2.4.0/Microsoft.WindowsAppRuntime.Redist.2.4.zip)
   - Page officielle : [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

> Astuce : pour ne plus jamais dépendre du runtime installé sur la machine, on peut publier en **self-contained** (le runtime 2.4 est alors embarqué dans l’app) en ajoutant `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` au `.csproj`. Non activé par défaut ici pour garder une sortie légère.

## Ouvrir et compiler

1. Cloner le dépôt et ouvrir `WinBoard.sln` dans Visual Studio 2022.
2. Choisir la plateforme **x64** (ou x86 / ARM64 selon la machine) — pas AnyCPU.
3. Définir `src\WinBoard` comme projet de démarrage.
4. **F5** (Déboguer) ou **Ctrl+F5** (Démarrer sans débogage).

Le profil `WinBoard (unpackaged)` lance l’exécutable directement, sans MSIX.

En ligne de commande (Invite de commandes **Développeur** Visual Studio, sur Windows) :

```bat
dotnet build WinBoard.sln -c Debug -p:Platform=x64
```

### Tâches VS Code (Windows)

Le dépôt contient `.vscode/tasks.json` (`1.Dbg`, `2.Rel`, `3.Msi`, `4.Log`, `5.Msix`). Ces tâches appellent des scripts PowerShell sous `scripts/` (`run-debug.ps1`, `run-release.ps1`, `release-win-msi.ps1`, `release-changelog.ps1`, `release-win-msix.ps1`).

Ces `.ps1` de build/release sont dans `scripts/` (`run-debug.ps1`, `run-release.ps1`, `release-win-msi.ps1`, `release-changelog.ps1`, `release-win-msix.ps1`). `scripts/generate-dictionaries.py` régénère les lexiques.

## Fonctionnalités (0.8.7)

Le clavier ressemble à un vrai clavier de téléphone (inspiré de Gboard AZERTY) :

- **Réglages dans une fenêtre séparée** : l’engrenage (et **Paramètres** du menu plateau) ouvre **Réglages — WinBoard** (fenêtre normale, ~520×780, peut prendre le focus). Le clavier reste visible à côté pour prévisualiser taille, opacité, contours, rangée de chiffres, etc. Fermer les réglages rétablit le comportement no-activate du clavier. Bouton **Diagnostic swipe** (saisie par glissement) → fenêtre **Diag swipe** (manuel, pas activé par défaut).
- **Panneau emoji** : la touche 🙂 ouvre un panneau façon Gboard (catégories Smileys, Personnes, Nature, Nourriture, Activités, Voyages, Objets, Symboles, Drapeaux, plus **Récents**). Recherche par mots-clés FR/EN via des **lettres dans le panneau** (pas de `TextBox` système, pour ne pas voler le focus). Un tap injecte le glyphe via `SendInput` Unicode (séquences ZWJ / drapeaux en un seul lot). Les récents sont enregistrés dans `settings.json` (local).
- **Zone de notification** : icône Win32 `Shell_NotifyIcon` (application non empaquetée). Clic gauche = afficher / masquer le clavier **sans activer** la fenêtre. Menu contextuel : **Afficher / Masquer**, **Paramètres**, **Quitter**. La croix du clavier **masque** vers le plateau ; **Quitter** (menu ou bouton des Réglages) termine le processus.
- **Lexiques FR/EN à grande échelle** (~100 000 mots chacun, embarqués, CC BY-SA 4.0) : voir [DICTIONARIES.md](src/WinBoard/Assets/DICTIONARIES.md).
- **Déplacer** : glisser le mince bandeau haut (pastille 28×3). Un suivi non bloquant lit la position écran (souris ou `GetPointerInfo` tactile) et appelle `SetWindowPos(..., SWP_NOACTIVATE)` en continu, jusqu’au relâchement. Les touches / le swipe ne déclenchent jamais le déplacement.
- **Shift multitouch (façon Gboard)** : un doigt maintient **⇧**, un autre tape une lettre (chiffre / ponctuation si un glyphe décalé existe déjà sur la touche) → `SendInput` injecte la forme **majuscule / décalée**, sans voler le focus. Relâcher ⇧ revient au minuscule. Une **tape** sur ⇧ (sans autre touche) cycle toujours Off → Shift collant → Caps → Off. Pendant un swipe à un doigt, un second doigt est **ignoré** (le tracé n’est pas `ResetPress`).
- **Saisie par glissement (swipe typing)** : tracez un chemin sur les lettres, relâchez, et WinBoard décode le mot le plus probable puis l’injecte (avec une espace). Une **barre de suggestions** en haut propose les meilleurs candidats — touchez une puce pour remplacer le mot. Le **tracé** est dessiné pendant le glissement.
  - **Pipeline pérenne** (API `Decode` stable) : `geste → scores spatiaux par lettre → beam trie/dictionnaire → n-grammes hors-ligne → suggestions`. Un encodeur spatial neuronal (ex. FUTO) pourra remplacer **uniquement** `ISpatialEncoder` sans réécrire le beam ni le LM.
  - **Démarrage du geste** : le swipe s’enclenche après ~¼–½ largeur de touche **et** en quittant la touche de départ (le premier `PointerMoved` n’est jamais ignoré). Tap / appui long / swipe / glisser le bandeau sont des modes distincts ; un mouvement annule l’appui long.
  - **Moteur type OpenSwipe** (C# original, pas une copie GPL) : chemins idéaux par les centres de touches (AZERTY FR / QWERTY EN), DTW à bande Sakoe–Chiba + LB_Keogh / abandon anticipé, élagage début/fin + rapport de longueur + LCS permissif. Les hit-keys **boostent** en spatial doux (rayon voisin) ; elles ne tuent pas au millimètre. La longueur écrase encore les mots absurdes (12–16 lettres sur un geste ~7 touches). Unigrammes + bigrammes FR/EN hors-ligne. **Aucune liste noire** de mots.
  - **Précision 0.8.3** : ancres début/fin plus lourdes (`AnchorWeight` 2,8) pour que les mots dont la 1ʳᵉ/dernière touche est loin du geste perdent ; hit-keys **centre** (M vs L) restent dures, graze milieu un peu plus tolérant.
  - **Retune 0.8.6** (session diag AZERTY réelle, sans liste noire) : lexique **bilingue FR∪EN** (+ `azerty` / `qwerty`) pour que swipe / thanks / Windows / hello décodent sur AZERTY ; `HitBoost` **plafonné par couverture** (plus de bonus empilé sur un mot long) ; miss hit-keys **uniquement sur les centre-hits** hors du gabarit (les grazes d’un gribouillis n’élisent plus un mot plus long) ; flyover = corridor 0,45 pitch **le long du gabarit** (une vraie traversée a→i sur la rangée du haut reste libre) ; lettres du mot loin du tracé pénalisées ; longueur-ratio sur le geste **simplifié** (RDP) ; léger prior contre les 9+ lettres ; LM un peu plus léger (`0,30` / verrou `0,36`). Replay : `SwipeDiagReplayTests` — **13/13** attendus #1.
  - **Poids par défaut (0.8.6)** — distances en pitches de touche (voisines ≈ 1,0) :
    | Knob | Valeur | Rôle |
    | --- | --- | --- |
    | `LocationWeight` | **1,05** | Canal principal : distance point-à-point (sans warp) |
    | `DtwWeight` / `BandFraction` | **0,45** / **0,12** | Forme ; bande étroite pour qu’un geste court et précis gagne |
    | `AnchorWeight` / `AnchorReject` | **2,8** / **0,78** | 1ʳᵉ et dernière touche vs caps du geste |
    | `HitKeyWeight` / `HitBoost` / `HitBoostCap` | **5,5** / **0,18** / **0,42** | Centre M vs L ≈ +1,7 ; bonus = couverture, plafonné |
    | `HitMissCap` | **2,45** | Somme des miss centre-hit (2 cliffs M–L) |
    | `FlyoverRadius` / `MidFlyover` | **0,45** / **0,52** | Traversée le long du gabarit (corridor fin) |
    | `MissingLetterWeight` / `MissingLetterRadius` | **0,72** / **0,88** | Lettre du mot jamais approchée par le tracé |
    | `LongWordPriorWeight` / `MinLetters` | **0,14** / **8** | Léger malus dès 9 lettres |
    | `LengthRatioLong` / `HardReject` | **1,08** / **1,18** | Sur la longueur **simplifiée** (pas le scribble) |
    | `LetterCountWeight` / `MinHits` | **8** / **3** | Écrase 12 lettres sur un geste ~7 (ou 3 hit-keys) |
    | `LanguageWeight` / `LanguageLockGap` | **0,30** / **0,36** | N-grammes : voisins très proches seulement |
  - Tests `WinBoard.Core.Tests` : **comment** ≫ content / collent / commenceront, hello ≫ jello (ancre), hit M, longueur, verrou LM, latch, schéma JSON diagnostic, politique TOPMOST, **replay des 13 tracés diag 0.8.4**.
- **Diagnostic swipe (0.8.4)** : outil manuel pour capturer de vrais tracés vs le décodeur. Voir [Diagnostic swipe](#diagnostic-swipe).
- **0.8.5** : **Exporter** copie le chemin JSON dans le presse-papiers. Always-on-top : le WndProc empêche WinUI d’enlever `WS_EX_TOPMOST` ; `SetBorderAndTitleBar` n’est plus rappelé à chaque changement de réglage (ça cassait le z-order).
- **0.8.6** : retune OpenSwipe ci-dessus. Shift+lettre, Diag swipe et AlwaysOnTop inchangés.
- **0.8.7** : correctif build (CS0136) — `SettingsWindow.Show` ne déclare plus deux fois la variable de motif `keyboard` dans la même portée.
- **Rangée de chiffres** (1–0) : interrupteur **Rangée de chiffres** dans Réglages — masque/affiche immédiatement.
- **Contours des touches** : trait Fluent **1 px**, faible contraste ; hors = sans bord. Live depuis la fenêtre Réglages.
- **Appui long** → popup d’accents ; **répétition** ⌫ / chiffres ; **glissement ⌫** = mot par mot.
- **Espace** :
  - tap = espace
  - glisser gauche/droite = **déplacer le curseur** (flèches SendInput, style Gboard)
  - appui long **immobile** = bascule **FR ⟷ EN**
- **Transparence** : le curseur d’opacité applique `WS_EX_LAYERED` + `SetLayeredWindowAttributes` (le fond Acrylic était opaque avant). Les réglages (opacité, taille, lettres, thème, …) sont lus au démarrage depuis `%LOCALAPPDATA%\WinBoard\settings.json` et réécrits à chaque changement (fenêtre Réglages).
- **Taille** : curseur plus fin (0,70–1,80, pas 0,02) + **taille des lettres indépendante**.
- **MyClipboard** : bouton 📋 sur la barre de suggestions. Voir [Connexion MyClipboard](#connexion-myclipboard).
- Fenêtre always-on-top : `OverlappedPresenter.IsAlwaysOnTop` **et** `WS_EX_TOPMOST`. WinUI (AppWindow, `SetBorderAndTitleBar`, une 2ᵉ fenêtre Réglages/Diag) retire souvent TOPMOST — le WndProc réécrit `WM_WINDOWPOSCHANGING` / `WM_STYLECHANGING`, et le clavier ré-applique `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` après show, réglages, diag, glisser, plateau, opacité/taille. **Sans voler le focus**.

## Confidentialité

WinBoard **ne collecte aucune donnée de saisie automatiquement**. Tout est local :

- pas de télémétrie, analytics, ni appel réseau pour la frappe / le swipe (dictionnaires **100 % locaux**)
- pas de journalisation automatique des caractères, chemins de swipe, ni du presse-papiers
- les réglages (thème, taille, emojis récents, etc.) : `%LOCALAPPDATA%\WinBoard\settings.json`
- **Diag swipe** n’écrit un fichier que si vous cliquez sur **Exporter** : `%LOCALAPPDATA%\WinBoard\diagnostics\swipe-YYYYMMDD-HHMMSS.json` (jamais envoyé)
- MyClipboard n’est lu que depuis `%LOCALAPPDATA%\MyClipBoard\integration\clips.json` (jamais envoyé, jamais écrit par WinBoard)
- lexiques FR/EN : voir [Assets/DICTIONARIES.md](src/WinBoard/Assets/DICTIONARIES.md) (FrequencyWords 2018 + Lexique383 / SCOWL, CC BY-SA 4.0 ; bigrammes compactes hors-ligne)

Le panneau Réglages affiche : *« Aucune donnée de saisie n’est collectée automatiquement / 100 % local »*.

## Diagnostic swipe

Outil **manuel** (Réglages → **Diagnostic swipe** / **Diag swipe**). Pas activé par défaut. Fermer la fenêtre coupe la capture ; la frappe normale n’est pas affectée.

1. Ouvrir Réglages → **Diagnostic swipe**. Une fenêtre séparée (comme Réglages) montre le mot cible, la progression `n/N`, et le dernier top-N du décodeur.
2. Glisser le mot sur le **vrai clavier**. OK / Échec / Passer / Réessayer / Suivant. **Recommencer** vide la session.
3. **Exporter** écrit `%LOCALAPPDATA%\WinBoard\diagnostics\` (créé si besoin) **et copie le chemin complet dans le presse-papiers**. **Ouvrir le dossier** lance l’explorateur. Rien n’est uploadé.

Liste par défaut (~13 mots FR+EN) : comment, bonjour, hello, merci, clavier, swipe, azerty, qwerty, maison, demain, please, thanks, Windows. Les distracteurs d’analyse (`collent`, `content`) ne sont **pas** des cibles.

Schéma JSON **version 1** (`schemaVersion`, camelCase). Espace des coordonnées : **`key-pitch`**.

- `path.x/y` = `(layoutDip - originDip) / pitchDip`
- `originDip` = min des centres de lettres (x, y) dans l’espace DIP du clavier (`RootGrid` / tracé)
- `pitchDip` = espacement médian des touches lettres
- Replay : `dip = originDip + pitchDip * (x, y)`
- `centers` : centres des lettres dans le même espace key-pitch
- `path.t` : millisecondes depuis le premier échantillon (optionnel)

```json
{
  "schemaVersion": 1,
  "appVersion": "0.8.6",
  "layout": "AZERTY",
  "keyboardScale": 1.0,
  "coordinateSpace": "key-pitch",
  "words": [
    {
      "expected": "comment",
      "decoded": "comment",
      "ok": true,
      "hitKeys": ["c", "o", "m", "e", "n", "t"],
      "path": [{ "x": 2.0, "y": 1.0, "t": 0 }],
      "candidates": [
        {
          "word": "comment",
          "score": 0.42,
          "breakdown": {
            "spatial": 0.31, "dtw": 0.12, "location": 0.19,
            "length": 0.0, "anchors": 0.02, "hitKeys": -0.22, "language": 0.11
          }
        }
      ],
      "notes": "optionnel"
    }
  ]
}
```

`ok` : `true` (OK), `false` (Échec), `null` (Passer / non marqué). Chaque entrée peut aussi porter `layout`, `keyboardScale`, `originDip`, `pitchDip`, `centers`, `timestampUtc` (changement FR/EN en cours de session).

Tests Core : `SwipeDiagnosticTests` (sérialisation, coordonnées, session, nom de fichier). Test manuel Windows : ouvrir Diag swipe → glisser 2–3 mots → OK/Échec → Exporter → vérifier le fichier ; fermer Diag swipe → un swipe normal s’injecte sans capture.

## Connexion MyClipboard

Contrat figé avec **MyClipboard Desktop** (MCB_App PR #6). WinBoard **ne fait que lire** un fichier JSON local (pas de SQLite, pas de réseau, pas d’écriture). La casse ordinale n’est exigée que sur le suffixe `MyClipBoard\integration\clips.json` — le préfixe `%LOCALAPPDATA%` (`Users` / profil / `AppData` / `Local`) est résolu via `File.Exists` (insensible à la casse sous Windows).

| | |
| --- | --- |
| Chemin | `%LOCALAPPDATA%\MyClipBoard\integration\clips.json` (casse **MyClipBoard**) |
| Protocole | `myclipboard://authorize-winboard` |
| Schéma | version **1** (ci-dessous) |
| Exemple | `src/WinBoard/Assets/clips.example.json` |
| Côté WinBoard | `MyClipboardContract` / `ClipboardClipParser` / `FileClipboardClipSource` / `MyClipboardAccess` |

```json
{
  "version": 1,
  "updatedAtMs": 0,
  "authorized": true,
  "clips": [ { "id": "…", "text": "…", "type": "text", "updatedAtMs": 0 } ]
}
```

États du panneau 📋 :

- **Fichier absent** — bouton **« Demander l’accès à MyClipboard »** ouvre `myclipboard://authorize-winboard`.
- **`authorized: false`** — extraits ignorés (liste vide) ; même bouton pour demander l’accès.
- **Vide** — accès OK, aucun extrait.
- **JSON illisible** — schéma version 1 camelCase attendu.
- Panneau ouvert : **FileSystemWatcher + polling** ; un tap injecte le texte via `SendInput`.

100 % local.

## Tests du décodeur (Linux / CI)

```bat
dotnet test tests/WinBoard.Core.Tests/WinBoard.Core.Tests.csproj
```

`ShiftChordTests` couvre le cycle Off / Shift collant / Caps, le hold+lettre (pas de cycle), Caps qui reste, et le routage des pointeurs (⇧ + lettre coexistent ; second doigt pendant un swipe ignoré).

### Test manuel — Shift + lettre (Windows, tactile)

1. **Accord** : un doigt sur ⇧, un autre tape `a` → injecte `A`. Relâcher ⇧ → les tapes suivantes sont minuscules.
2. **Tape ⇧ seule** (sans autre touche) : 1× Shift collant (prochaine lettre en majuscule), 2× Caps ⇪, 3× Off.
3. **Swipe** : un doigt sur les lettres, glisser, relâcher → mot inchangé. Un second doigt posé pendant le tracé ne doit **pas** annuler le swipe.
4. **Bandeau** : glisser le bandeau haut déplace toujours la fenêtre (un doigt).
5. **Chiffre / ponctuation** (si glyphe d’angle) : ⇧ maintenu + `1` injecte `¹` ; ⇧ + `'` injecte `?` sur AZERTY.

Pour régénérer les listes (dev uniquement, nécessite le réseau une fois) :

```bash
python3 scripts/generate-dictionaries.py
```

## Structure

```
WinBoard.sln
.vscode/tasks.json        1.Dbg / 2.Rel / 3.Msi / 4.Log / 5.Msix (scripts/*.ps1 locaux)
src/WinBoard.Core/        Pipeline swipe (spatial, DTW, trie, LM) + diagnostic JSON + ShiftChord/pointeurs + lexiques + emoji + parser clips (net9, sans WinUI)
src/WinBoard/
  UI/KeyboardWindow       Clavier, bandeau titre, emoji, MyClipboard, capture diag
  UI/SettingsWindow       Fenêtre de réglages séparée (focus OK)
  UI/SwipeDiagnosticWindow Fenêtre Diag swipe (capture locale, export JSON)
  Input/                  SendInput, no-activate, suivi tactile écran, WS_EX_LAYERED, Shell_NotifyIcon
  Layouts/                AZERTY / QWERTY / symboles
  Services/               Réglages JSON, clips MyClipboard, version
  Assets/                 words_*.txt, bigrams_*.txt, DICTIONARIES.md, clips.example.json, winboard.ico
scripts/                  generate-dictionaries.py, generate-bigrams.py + scripts PowerShell de build/release
tests/WinBoard.Core.Tests Vecteurs swipe, smoke lexiques, recherche emoji, parser clips, schéma diagnostic
```

## Prochaines étapes

- IPC MyClipboard plus riche si l’app expose autre chose que le fichier d’intégration
- Encodeur spatial neuronal (FUTO ou équivalent) branché sur `ISpatialEncoder`
- Option MSIX (empaquetage Store) — volontairement hors de cette version
