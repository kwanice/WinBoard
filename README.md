# WinBoard

**Version 0.6.4**

Clavier tactile flottant bilingue **FR/EN** pour Windows, façon Gboard. Il reste au-dessus des autres fenêtres, n’envoie **pas** le focus vers lui-même, et injecte les caractères dans l’application déjà active via Win32 `SendInput`.

Windows uniquement. **Saisie par glissement (swipe typing) désormais disponible** (décodeur géométrique, sans ML).

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

## Fonctionnalités (0.6.4)

Le clavier ressemble à un vrai clavier de téléphone (inspiré de Gboard AZERTY) :

- **Réglages dans une fenêtre séparée** : l’engrenage (et **Paramètres** du menu plateau) ouvre **Réglages — WinBoard** (fenêtre normale, ~520×780, peut prendre le focus). Le clavier reste visible à côté pour prévisualiser taille, opacité, contours, rangée de chiffres, etc. Fermer les réglages rétablit le comportement no-activate du clavier.
- **Panneau emoji** : la touche 🙂 ouvre un panneau façon Gboard (catégories Smileys, Personnes, Nature, Nourriture, Activités, Voyages, Objets, Symboles, Drapeaux, plus **Récents**). Recherche par mots-clés FR/EN via des **lettres dans le panneau** (pas de `TextBox` système, pour ne pas voler le focus). Un tap injecte le glyphe via `SendInput` Unicode (séquences ZWJ / drapeaux en un seul lot). Les récents sont enregistrés dans `settings.json` (local).
- **Zone de notification** : icône Win32 `Shell_NotifyIcon` (application non empaquetée). Clic gauche = afficher / masquer le clavier **sans activer** la fenêtre. Menu contextuel : **Afficher / Masquer**, **Paramètres**, **Quitter**. La croix du clavier **masque** vers le plateau ; **Quitter** (menu ou bouton des Réglages) termine le processus.
- **Lexiques FR/EN à grande échelle** (~100 000 mots chacun, embarqués, CC BY-SA 4.0) : voir [DICTIONARIES.md](src/WinBoard/Assets/DICTIONARIES.md).
- **Déplacer** : glisser le mince bandeau haut (pastille 28×3). Un suivi non bloquant lit la position écran (souris ou `GetPointerInfo` tactile) et appelle `SetWindowPos(..., SWP_NOACTIVATE)` en continu, jusqu’au relâchement. Les touches / le swipe ne déclenchent jamais le déplacement.
- **Saisie par glissement (swipe typing)** : tracez un chemin sur les lettres, relâchez, et WinBoard décode le mot le plus probable puis l’injecte (avec une espace). Une **barre de suggestions** en haut propose les meilleurs candidats — touchez une puce pour remplacer le mot. Le **tracé** est dessiné pendant le glissement.
  - Décodeur **local** (pas d’IA cloud) : touches réellement croisées (hit-rects dans le même espace DIP que le tracé, y compris après **changement d’échelle**) + Levenshtein spatial + alignement ordonné. La fréquence n’est qu’un départage minuscule.
  - Premier/dernier caractère : rayon serré (~0,6 pas de touche), pas un halo de 1,7 touche. Les lettres loin du tracé et les touches observées absentes du candidat sont pénalisées (règles générales, pas de liste noire de mots).
  - Listes **FR/EN** embarquées (~100 000 mots) selon la disposition (AZERTY→FR, QWERTY→EN).
  - Tests `WinBoard.Core.Tests` : comment vs content, chemins **hello** / **bonjour** comme régressions géométriques, invariance d’échelle.
- **Rangée de chiffres** (1–0) : interrupteur **Rangée de chiffres** dans Réglages — masque/affiche immédiatement.
- **Contours des touches** : trait Fluent **1 px**, faible contraste ; hors = sans bord. Live depuis la fenêtre Réglages.
- **Appui long** → popup d’accents ; **répétition** ⌫ / chiffres ; **glissement ⌫** = mot par mot.
- **Espace** :
  - tap = espace
  - glisser gauche/droite = **déplacer le curseur** (flèches SendInput, style Gboard)
  - appui long **immobile** = bascule **FR ⟷ EN**
- **Transparence** : le curseur d’opacité applique `WS_EX_LAYERED` + `SetLayeredWindowAttributes` (le fond Acrylic était opaque avant).
- **Taille** : curseur plus fin (0,70–1,80, pas 0,02) + **taille des lettres indépendante**.
- **MyClipboard** : bouton 📋 sur la barre de suggestions. Voir [Connexion MyClipboard](#connexion-myclipboard).
- Fenêtre always-on-top, **sans voler le focus**.

## Confidentialité

WinBoard **ne collecte aucune donnée de saisie**. Tout est local :

- pas de télémétrie, analytics, ni appel réseau pour la frappe / le swipe (dictionnaires **100 % locaux**)
- pas de journalisation des caractères, chemins de swipe, ni du presse-papiers
- les réglages (thème, taille, emojis récents, etc.) sont le seul fichier écrit : `%LOCALAPPDATA%\WinBoard\settings.json`
- MyClipboard n’est lu que depuis un fichier local (voir ci-dessous), jamais envoyé
- lexiques FR/EN : voir [Assets/DICTIONARIES.md](src/WinBoard/Assets/DICTIONARIES.md) (FrequencyWords 2018 + Lexique383 / SCOWL, CC BY-SA 4.0)

Le panneau Réglages affiche : *« Aucune donnée de saisie n’est collectée / 100 % local »*.

## Connexion MyClipboard

Le dépôt public [kwanice/MyClipBoard](https://github.com/kwanice/MyClipBoard) n’expose **pas** d’API IPC pour l’instant (page d’accueil HTML seulement). WinBoard ne « plante » pas : il lit un **fichier local** (aucun réseau).

| | |
| --- | --- |
| Chemin préféré | `%LOCALAPPDATA%\MyClipBoard\clips.json` (casse historique du dépôt public) |
| Aussi accepté | `MyClipboard` (autre casse) sous LocalAppData **ou** Roaming |
| Format | `{ "clips": [ { "id": "…", "text": "…", "timestamp": "2026-09-16T12:00:00Z" } ] }` |
| Alias | tableau racine ; `items` ; champs `text` / `content` / `Content` |
| Exemple | `src/WinBoard/Assets/clips.example.json` (copié à côté de l’EXE) |
| Côté WinBoard | `IClipboardClipSource` / `FileClipboardClipSource` / `ClipboardClipParser` |

États du panneau 📋 :

- **Fichier absent** — message *« MyClipboard n’a pas encore écrit de fichier local »* + chemin à créer (ce n’est pas un bug WinBoard).
- **JSON illisible** — chemin du fichier + rappel du schéma.
- **Fichier vide / sans extraits** — distinct de « fichier manquant ».

Copiez l’exemple vers le chemin préféré pour tester l’injection sans MyClipboard. 100 % local.

## Tests du décodeur (Linux / CI)

```bat
dotnet test tests/WinBoard.Core.Tests/WinBoard.Core.Tests.csproj
```

Pour régénérer les listes (dev uniquement, nécessite le réseau une fois) :

```bash
python3 scripts/generate-dictionaries.py
```

## Structure

```
WinBoard.sln
.vscode/tasks.json        1.Dbg / 2.Rel / 3.Msi / 4.Log / 5.Msix (scripts/*.ps1 locaux)
src/WinBoard.Core/        Décodeur swipe + lexiques + catalogue emoji + parser clips (net9, sans WinUI)
src/WinBoard/
  UI/KeyboardWindow       Clavier, bandeau titre, emoji, MyClipboard
  UI/SettingsWindow       Fenêtre de réglages séparée (focus OK)
  Input/                  SendInput, no-activate, suivi tactile écran, WS_EX_LAYERED, Shell_NotifyIcon
  Layouts/                AZERTY / QWERTY / symboles
  Services/               Réglages JSON, clips MyClipboard, version
  Assets/                 words_*.txt, DICTIONARIES.md, clips.example.json, winboard.ico
scripts/                  generate-dictionaries.py + scripts PowerShell de build/release
tests/WinBoard.Core.Tests Vecteurs swipe, smoke lexiques, recherche emoji, parser clips
```

## Prochaines étapes

- IPC MyClipboard plus riche (named pipe) quand l’app exposera une API
- Suggestions pendant la frappe normale
- Option MSIX (empaquetage Store) — volontairement hors de cette version
