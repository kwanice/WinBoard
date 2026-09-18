# WinBoard

**Version 0.8.19**

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

## Fonctionnalités (0.8.19)

Le clavier ressemble à un vrai clavier de téléphone (inspiré de Gboard AZERTY) :

- **Réglages dans une fenêtre séparée** : l’engrenage (et **Paramètres** du menu plateau) ouvre **Réglages — WinBoard** (fenêtre normale, ~520×780, peut prendre le focus). Le clavier reste visible à côté pour prévisualiser taille, opacité, contours, rangée de chiffres, etc. Fermer les réglages rétablit le comportement no-activate du clavier. Bouton **Diagnostic swipe** (saisie par glissement) → fenêtre **Diag swipe** (manuel, pas activé par défaut).
- **Panneau emoji** : la touche 🙂 ouvre un panneau façon Gboard (catégories Smileys, Personnes, Nature, Nourriture, Activités, Voyages, Objets, Symboles, Drapeaux, plus **Récents**). Recherche par mots-clés FR/EN via des **lettres dans le panneau** (pas de `TextBox` système, pour ne pas voler le focus). Un tap injecte le glyphe via `SendInput` Unicode (séquences ZWJ / drapeaux en un seul lot). Les récents sont enregistrés dans `settings.json` (local).
- **Zone de notification** : icône Win32 `Shell_NotifyIcon` (application non empaquetée). Clic gauche = afficher / masquer le clavier **sans activer** la fenêtre. Menu contextuel : **Afficher / Masquer**, **Paramètres**, **Quitter**. La croix du clavier **masque** vers le plateau ; **Quitter** (menu ou bouton des Réglages) termine le processus.
- **Lexiques FR/EN à grande échelle** (~100 000 mots chacun, embarqués, CC BY-SA 4.0) : voir [DICTIONARIES.md](src/WinBoard/Assets/DICTIONARIES.md).
- **Déplacer** : glisser le mince bandeau haut (pastille 28×3). Un suivi non bloquant lit la position écran (souris ou `GetPointerInfo` tactile) et appelle `SetWindowPos(..., SWP_NOACTIVATE)` en continu, jusqu’au relâchement. Les touches / le swipe ne déclenchent jamais le déplacement.
- **Shift multitouch (façon Gboard)** : un doigt maintient **⇧**, un autre tape une lettre (chiffre / ponctuation si un glyphe décalé existe déjà sur la touche) → `SendInput` injecte la forme **majuscule / décalée**, sans voler le focus. Relâcher ⇧ revient au minuscule. Une **tape** sur ⇧ (sans autre touche) cycle toujours Off → Shift collant → Caps → Off. Pendant un swipe à un doigt, un second doigt est **ignoré** (le tracé n’est pas `ResetPress`).
- **Saisie par glissement (swipe typing)** : tracez un chemin sur les lettres, relâchez, et WinBoard décode le mot le plus probable puis l’injecte **sans espace finale**. Le swipe suivant préfixe une espace si besoin (comme Gboard). **⌫** juste après un swipe efface **tout le chunk** (mot + espace préfixe) ; Espace, une lettre, ou un autre commit revient au ⌫ caractère par caractère. Une **barre de suggestions** en haut propose les meilleurs candidats — touchez une puce pour remplacer le mot. Le **tracé** est dessiné pendant le glissement.
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
  - Tests `WinBoard.Core.Tests` : **comment** ≫ content / collent / commenceront, hello ≫ jello (ancre), hit M, longueur, verrou LM, latch, schéma JSON diagnostic **v1+v2 (phrases / CaptureLost)**, politique TOPMOST, **replay diag 0.8.4 (13)** et **0.8.9 (14, dont france / keyboard)**, machine d’état **swipe-commit / ⌫ mot entier**, **session pointeur / CaptureLost** (enchaînement de tracés).
- **Diagnostic swipe (0.8.4)** : outil manuel pour capturer de vrais tracés vs le décodeur. Voir [Diagnostic swipe](#diagnostic-swipe).
- **0.8.5** : **Exporter** copie le chemin JSON dans le presse-papiers. Always-on-top : le WndProc empêche WinUI d’enlever `WS_EX_TOPMOST` ; `SetBorderAndTitleBar` n’est plus rappelé à chaque changement de réglage (ça cassait le z-order).
- **0.8.6** : retune OpenSwipe ci-dessus. Shift+lettre, Diag swipe et AlwaysOnTop inchangés.
- **0.8.7** : correctif build (CS0136) — `SettingsWindow.Show` ne déclare plus deux fois la variable de motif `keyboard` dans la même portée.
- **0.8.8** : nouvelle liste par défaut du **Diag swipe** (mots courts FR+EN + 3 longs) — distincte de la session 0.8.4 utilisée pour le retune. Poids du décodeur inchangés.
- **0.8.9** : **latence swipe** — le décodage tourne sur le thread pool (injection / suggestions remises au UI) ; lexique+trie+LM préchauffés au démarrage ; candidats indexés par 1ʳᵉ **et** dernière lettre ; DTW s’arrête tôt sur le pool trié « cheap ». Objectif : dizaines de ms une fois chaud (le 1ʳᵉ geste ne bloque plus le pointeur même si le chargement n’est pas fini). Diag exporte `decodeMs` (local, pas de télémétrie). Poids inchangés.
- **0.8.10** : retune ciblé session diag 0.8.9 — `france` était **absent du lexique** (nom propre coupé) donc impossible à décoder ; couverture hit-keys compte aussi les *soft hits* ; plus de malus `extraLetters²` qui empilait les lettres sautées (b→r sans o/a) et faisait gagner kirkyard sur **keyboard** malgré une bien meilleure location. Async 0.8.9 inchangé. Replay `diag-azerty-0.8.4` + `diag-azerty-0.8.9`.
- **0.8.11** : swipe façon Gboard — pas d’espace traînante ; espace préfixe sur le swipe suivant ; ⌫ annule le dernier chunk. Enchaînement de swipes : le clavier ne vole plus le focus (SendInput sans ExtraInfo du pointeur, puces suggestion non focusables, restauration du HWND cible si l’overlay s’active). AlwaysOnTop, Shift+lettre, diag, async 0.8.9 inchangés.
- **0.8.12** : le tracé ne meurt plus au milieu d’un enchaînement. Cause : l’injection / `SetForegroundWindow` du mot précédent faisait un `PointerCaptureLost` traité comme un abandon (`EndSwipe(false)` vidait le canvas). Désormais CaptureLost tant que le doigt est bas **continue** le geste ; restauration HWND et rebuild suggestions **attendent** le relâchement. ⌫ mot entier, no-activate, AlwaysOnTop, Shift+lettre, async 0.8.9 inchangés.
- **0.8.13** : **Diag swipe** en phrases 4–5 mots (FR+EN) pour capturer les pannes d’enchaînement (tracé qui meurt, CaptureLost, mauvais mot). Chaque swipe est enregistré automatiquement (pas besoin de cliquer OK entre les mots). Export JSON **schéma 2** : `phraseId` / `wordIndex` + métadonnées de chaîne. **Poids du décodeur inchangés** (outil uniquement).
- **0.8.14** : correctif build ARM64 — `TryRecapturePointer` utilisait `Border ?? Grid` (CS0019) ; cast commun `UIElement` pour la recapture après CaptureLost (0.8.12). Comportement inchangé.
- **0.8.15** : enchaînement de swipes — un nouveau doigt pendant un glide **commit** le mot courant et démarre le suivant (les contacts ignorés laissaient des trous de `pointerId` sans export). CaptureLost ne cancel plus le geste (`IsInContact` est faux après SendInput) ; capture sur `RootGrid`. Diag : `abortedGestures[]` (schéma 3) sans avancer le mot ; auto-`ok` si expected==decoded (pli), y compris `va`. Poids du décodeur inchangés.
- **0.8.16** : un swipe décodé **vais** n’injecte plus la touche sous le doigt (`ssss`). Cause : lettres `Repeatable` + commit-then-begin 0.8.15 démarraient un appui/repeat sur la dernière hit-key, et `ResetPress` incrémentait le serial async donc **sautait** le SendInput du mot (le diag voyait encore vais). Désormais : pas de tap/repeat lettre pendant le glide ni tant que l’inject est en file ; les mots s’injectent **dans l’ordre**. ⌫ mot entier, AlwaysOnTop, Shift+lettre, diag 0.8.15 inchangés.
- **0.8.17** : le 1ᵉʳ mot après une pause marchait, les suivants injectaient `ssss`/`uu`/`eeeee` et le tracé mourait. Cause 0.8.16 : capture/`Repeat`/file d’inject non remis à zéro entre gestes, et SendInput/HWND restore **pendant** le swipe suivant. Correctif : reset capture+timers à chaque pointer-up ; **pas de Repeat sur les lettres** ; inject seulement doigt levé + restauration de la cible avant SendInput. Diag, ⌫ mot entier, AlwaysOnTop, Shift+lettre inchangés.
- **0.8.18** : **retour partiel** de l’orchestration pointeur/inject 0.8.15–0.8.17. Un nouveau doigt pendant un glide **n’auto-commit plus** la session (`CommitPrimaryThenBegin` retiré) ; CaptureLost **Continue seulement si le contact est encore bas** (plus de Continue inconditionnel + recapture qui se battait avec `ReleasePointerCaptures`) ; plus de portes globales swipe-only / inject-pending (trous « idle » faux et mauvais timing). Le décodage/inject est une **file séparée** : elle ne vole pas le `pointerId` ; l’inject part après un relâchement terminal, **quand aucun contact n’est actif** ; le HWND cible est mémorisé **avant** le geste (Diag n’est jamais la cible texte). Diag et inject restent **alignés** sur un complete réussi. **Conservé** : tracker Gboard (espace préfixe, ⌫ mot entier), decode async 0.8.9, UI phrases + `abortedGestures`, lettres **non Repeatable**, AlwaysOnTop, Shift+lettre, export Diag. Poids du décodeur inchangés.
- **0.8.19** : après un swipe **latché**, le pointer-up ne fait plus un tap lettre sur la dernière hit-key (`ssss`/`iiiii`/`eeeee` alors que le diag décodait vais/au/…). `EndSwipe` n’appelle plus `PerformTap` ; le même `pointerId` est ignoré jusqu’au tick suivant (faux `PointerPressed` WinUI après perte de capture). Le mot décodé est toujours enfilé vers `InjectSwipeWord` (HWND texte mémorisé, jamais Diag). Pas de `CommitPrimaryThenBegin`, pas de CaptureLost always-Continue, pas de porte globale. ⌫ mot entier, phrases, async, lettres non Repeatable, AlwaysOnTop, Shift+lettre inchangés.
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

### Comment capturer un enchaînement (0.8.13)

1. Ouvrir Réglages → **Diagnostic swipe**. La fenêtre montre la **phrase entière**, le mot courant en gras souligné, et la progression `Phrase n/N · mot w/W · swipe i/total`.
2. Glissez **chaque mot à la suite**, comme sur Gboard — **ne cliquez pas OK entre les mots**. Chaque relâchement est enregistré et avance tout seul. Un geste incomplet (CaptureLost, trop court, jamais latché) va dans `abortedGestures` **sans avancer** le mot cible.
3. OK / Échec corrigent le **dernier** mot si le marque auto (pli accents/casse) est faux. **Réessayer** retire le dernier mot. **Passer** saute le mot courant. **Recommencer** vide la session.
4. **Exporter** écrit `%LOCALAPPDATA%\WinBoard\diagnostics\` (créé si besoin) **et copie le chemin complet dans le presse-papiers** (0.8.5). **Ouvrir le dossier** lance l’explorateur. Rien n’est uploadé.

Une phrase n’est **OK** que si **tous** ses mots sont OK. Sinon `phrases[].ok` est `false` (un échec) ou `null` (mot non marqué).

Liste par défaut (0.8.13) — phrases 4 mots FR/EN, plus deux échauffements d’un mot :

- *je vais au marché*
- *bonjour comment ça va*
- *the quick brown fox*
- *I need a coffee*
- *oui*, *keyboard*

Distincte des fixtures de retune 0.8.4 / 0.8.9 (`diag-azerty-0.8.4.json`, `diag-azerty-0.8.9.json`). Les distracteurs d’analyse (`collent`, `content`) ne sont **pas** des cibles. **Aucun retune des poids** dans cette version.

### Schéma JSON

Export **version 3** (`schemaVersion`, camelCase). Le parseur **accepte les versions 1 et 2** (fixtures de replay / dumps 0.8.14). Espace des coordonnées : **`key-pitch`**.

- `path.x/y` = `(layoutDip - originDip) / pitchDip`
- `originDip` = min des centres de lettres (x, y) dans l’espace DIP du clavier (`RootGrid` / tracé)
- `pitchDip` = espacement médian des touches lettres
- Replay : `dip = originDip + pitchDip * (x, y)`
- `centers` : centres des lettres dans le même espace key-pitch
- `path.t` : millisecondes depuis le premier échantillon (optionnel)
- **v2** — chaque mot : `phraseId`, `phrase`, `wordIndex` (0-based), `wordCount`, `gestureOrdinal`, `captureLostCount`, `trailPointCount`, `gestureAborted`, `timeSincePreviousSwipeMs`, `pointerId`, `trailClearedMidGesture`, plus `decodeMs` (0.8.9)
- **v2** — `phrases[]` : résumé par phrase (`ok` seulement si tous les mots sont OK)
- **v3** — `abortedGestures[]` : sessions pointeur incomplètes (ne consomment pas le mot courant). `reason` : `canceled` | `captureLost` | `tooShort` | `neverLatched` | `trailCleared` | `superseded`. Auto-`ok` = expected==decoded (pli accents/casse), y compris les mots de 2 lettres (`va`).

```json
{
  "schemaVersion": 3,
  "appVersion": "0.8.15",
  "layout": "AZERTY",
  "keyboardScale": 1.0,
  "coordinateSpace": "key-pitch",
  "phrases": [
    {
      "id": "je-vais-au-marche",
      "text": "je vais au marché",
      "ok": true,
      "words": ["…mêmes objets que words[]…"]
    }
  ],
  "words": [
    {
      "expected": "va",
      "decoded": "va",
      "ok": true,
      "phraseId": "bonjour-comment-ca-va",
      "wordIndex": 3,
      "wordCount": 4,
      "pointerId": 5220
    }
  ],
  "abortedGestures": [
    {
      "expected": "need",
      "decoded": null,
      "ok": false,
      "gestureAborted": true,
      "reason": "captureLost",
      "pointerId": 5225,
      "captureLostCount": 1,
      "trailPointCount": 4,
      "trailClearedMidGesture": true,
      "wordIndex": 1,
      "path": [{ "x": 2.0, "y": 1.0, "t": 0 }]
    }
  ]
}
```

`ok` : `true` (OK / auto-match plié), `false` (Échec), `null` (Passer / non marqué). Les gestes abortés sont dans `abortedGestures`, pas dans `words`. Chaque entrée peut aussi porter `layout`, `keyboardScale`, `originDip`, `pitchDip`, `centers`, `timestampUtc`, `decodeMs`.

Tests Core : `SwipeDiagnosticTests` (phrases, `abortedGestures`, auto-ok `va`, sérialisation v1–v3). Test manuel Windows : Diag swipe → enchaîner *je vais au marché* sans double-essai → Exporter → `words[].pointerId` consécutifs ; un tracé mort doit apparaître dans `abortedGestures`.

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
