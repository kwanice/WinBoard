# WinBoard

**Version 0.2.0**

Clavier tactile flottant bilingue **FR/EN** pour Windows, façon Gboard. Il reste au-dessus des autres fenêtres, n’envoie **pas** le focus vers lui-même, et injecte les caractères dans l’application déjà active via Win32 `SendInput`.

Windows uniquement. Saisie par glissement (swipe *typing*) : prévue, pas encore implémentée.

## Stack

- **WinUI 3** + **C# / .NET 8**
- **Windows App SDK 1.7** (application bureau non empaquetée / unpackaged)
- Intégration native Win32 : `WS_EX_NOACTIVATE` + `SendInput` (`KEYEVENTF_UNICODE`)

## Prérequis

1. **Windows 10** (2004 / build 19041) ou **Windows 11**
2. **Visual Studio 2022** (17.8 ou plus récent recommandé) avec :
   - charge de travail **Développement d’applications Windows** (*Windows application development*)
   - composants **.NET 8** et **Windows App SDK C# Templates**
   - Windows SDK **10.0.19041** (ou plus récent)
3. **Windows App Runtime 1.7** — en général installé avec la charge de travail. Si l’application refuse de démarrer, installer le runtime correspondant depuis [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).

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

## Fonctionnalités (0.2.0)

Le clavier ressemble maintenant à un vrai clavier de téléphone (inspiré de Gboard AZERTY) :

- **Dispositions complètes** : **AZERTY (FR)** et **QWERTY (EN)** — lettres, modificateurs (⇧ Maj, ⌫ Retour, ⏎ Entrée), `?123`, virgule, emoji, espace, point
- **Rangée de chiffres** optionnelle (1–0), activable dans les réglages
- **Symboles secondaires** sur les touches (petits glyphes dans le coin), masquables
- **Appui long** → popup de caractères spéciaux (accents français sur AZERTY, extras sur QWERTY) ; glisser puis relâcher pour choisir
- **Répétition des touches** (⌫ et chiffres/symboles) avec délai initial + intervalle réglables
- **Glissement du Retour arrière** pour supprimer **mot par mot** (style Gboard, via Ctrl+Retour), en plus de l’appui répété caractère par caractère
- **Page symboles** `?123` (chiffres + ponctuation)
- **Maj / Verr. Maj** : un appui = majuscule ponctuelle, deux appuis = verrouillage
- **Espace** : appui = espace ; appui long = bascule **FR ⟷ EN**
- **Menu Réglages** (icône ⚙) : disposition, rangée de chiffres, symboles secondaires, appui long, répétition (+ délais), thème sombre/clair, opacité, taille des touches, et le **numéro de version**
- **Réglages persistants** : `%LOCALAPPDATA%\WinBoard\settings.json`
- Fenêtre toujours au premier plan, **sans voler le focus** (`WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE`/`WM_POINTERACTIVATE` → `MA_NOACTIVATE`), déplaçable par la poignée

Non inclus : swipe *typing* (glisser sur les lettres pour écrire des mots), suggestions, icône de notification (tray), panneau emoji complet, empaquetage MSIX.

## Structure

```
WinBoard.sln
src/WinBoard/
  App.xaml(.cs)          Démarrage (thème, show sans activation)
  UI/KeyboardWindow      Fenêtre clavier : rendu, Maj, répétition, appui long, glissement Retour
  Input/                 P/Invoke, SendInput (Unicode, Ctrl+Retour, Entrée), helper no-activate
  Layouts/               Dispositions AZERTY / QWERTY / symboles + touches
  Services/              Réglages persistants, disposition active, version
```

## Prochaines étapes

- **Swipe typing** : collecter le tracé sur `PointerMoved` (TODO déjà posé) et brancher un décodeur de mots
- **Suggestions** de mots au-dessus du clavier
- **Icône de notification** (tray) pour afficher / masquer sans barre des tâches
- Panneau **emoji** complet
- Option MSIX empaquetée si besoin du Store / d’une install propre
