# WinBoard

**Version 0.3.1**

Clavier tactile flottant bilingue **FR/EN** pour Windows, façon Gboard. Il reste au-dessus des autres fenêtres, n’envoie **pas** le focus vers lui-même, et injecte les caractères dans l’application déjà active via Win32 `SendInput`.

Windows uniquement. **Saisie par glissement (swipe typing) désormais disponible** (décodeur géométrique, sans ML).

## Stack

- **WinUI 3** + **C# / .NET 9** (`net9.0-windows10.0.19041.0`)
- **Windows App SDK 2.4.0** (application bureau non empaquetée / unpackaged)
- **Windows SDK BuildTools** 10.0.28000.2705
- Intégration native Win32 : `WS_EX_NOACTIVATE` + `SendInput` (`KEYEVENTF_UNICODE`)

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

## Fonctionnalités (0.3.1)

Le clavier ressemble à un vrai clavier de téléphone (inspiré de Gboard AZERTY) :

- **Saisie par glissement (swipe typing)** : tracez un chemin sur les lettres, relâchez, et WinBoard décode le mot le plus probable puis l’injecte (avec une espace). Une **barre de suggestions** en haut propose les meilleurs candidats — touchez une puce pour remplacer le mot. Le **tracé** est dessiné pendant le glissement.
  - Décodeur **géométrique** (pas d’IA) : filtre par première/dernière touche, puis compare la forme du tracé au polyligne des centres de lettres de chaque mot (ré-échantillonnage par longueur d’arc), avec un léger bonus de fréquence.
  - Listes de mots **FR/EN** fréquentielles embarquées (`Assets/words_fr.txt`, `words_en.txt`).
  - Réglages : activer/désactiver le swipe, afficher/masquer le tracé.
- **Dispositions complètes** : **AZERTY (FR)** et **QWERTY (EN)** — lettres, modificateurs (⇧ Maj, ⌫ Retour, ⏎ Entrée), `?123`, virgule, emoji, espace, point
- **Rangée de chiffres** optionnelle (1–0), activable dans les réglages
- **Symboles secondaires** sur les touches (petits glyphes dans le coin), masquables
- **Appui long** → popup de caractères spéciaux (accents français sur AZERTY, extras sur QWERTY) ; glisser puis relâcher pour choisir
- **Répétition des touches** (⌫ et chiffres/symboles) avec délai initial + intervalle réglables
- **Glissement du Retour arrière** pour supprimer **mot par mot** (style Gboard, via Ctrl+Retour), en plus de l’appui répété caractère par caractère
- **Page symboles** `?123` (chiffres + ponctuation)
- **Maj / Verr. Maj** : un appui = majuscule ponctuelle, deux appuis = verrouillage
- **Espace** : appui = espace ; appui long = bascule **FR ⟷ EN**
- **Menu Réglages** (icône ⚙) : swipe + tracé, disposition, rangée de chiffres, symboles secondaires, appui long, répétition (+ délais), thème sombre/clair, opacité, taille des touches, et le **numéro de version**
- **Réglages persistants** : `%LOCALAPPDATA%\WinBoard\settings.json`
- Fenêtre toujours au premier plan, **sans voler le focus** (`WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE`/`WM_POINTERACTIVATE` → `MA_NOACTIVATE`), déplaçable par la poignée

Non inclus : suggestions de mots pendant la frappe normale (au-delà du swipe), icône de notification (tray), panneau emoji complet, empaquetage MSIX. Le décodeur swipe est volontairement simple (géométrique) : listes de mots compactes, pas de modèle de langue.

## Structure

```
WinBoard.sln
src/WinBoard/
  App.xaml(.cs)          Démarrage (thème, show sans activation)
  UI/KeyboardWindow      Fenêtre clavier : rendu, Maj, répétition, appui long, glissement Retour, swipe + suggestions
  Input/                 P/Invoke, SendInput (Unicode, Ctrl+Retour, Entrée), helper no-activate
  Layouts/               Dispositions AZERTY / QWERTY / symboles + touches
  Services/              Réglages persistants, disposition active, version, décodeur swipe + listes de mots
  Assets/                words_fr.txt, words_en.txt (listes fréquentielles, ressources embarquées)
```

## Prochaines étapes

- Améliorer le décodeur swipe (listes plus larges, modèle de langue, correction)
- **Suggestions** pendant la frappe normale (pas seulement le swipe)
- **Icône de notification** (tray) pour afficher / masquer sans barre des tâches
- Panneau **emoji** complet
- Option MSIX empaquetée si besoin du Store / d’une install propre
