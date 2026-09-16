# WinBoard

Clavier tactile flottant bilingue **FR/EN** pour Windows. Il reste au-dessus des autres fenêtres, n’envoie **pas** le focus vers lui-même, et injecte les caractères dans l’application déjà active via Win32 `SendInput`.

Windows uniquement. Saisie par glissement (swipe) : prévue, pas encore implémentée.

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

## État actuel (MVP)

Squelette fonctionnel, pas un clavier complet :

- Fenêtre flottante toujours au premier plan, chrome réduit, fond Acrylic sombre (pas de bouton barre des tâches : `WS_EX_TOOLWINDOW` ; le tray viendra plus tard)
- Clic / tap **sans voler le focus** (`WS_EX_NOACTIVATE` + `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`)
- Disposition **AZERTY** (lettres + espace + retour arrière)
- Bascule **FR (AZERTY) / EN (QWERTY)**
- Injection Unicode via `SendInput` dans l’app qui a le focus
- Dossiers clairs : `UI`, `Input`, `Layouts`, `Services`

Non inclus : swipe, suggestions, barre des tâches / tray, Shift, chiffres, accents, emojis, empaquetage MSIX.

## Structure

```
WinBoard.sln
src/WinBoard/
  App.xaml(.cs)          Démarrage (thème sombre, show sans activation)
  UI/KeyboardWindow      Fenêtre clavier + rendu des touches
  Input/                 P/Invoke, SendInput, helper no-activate
  Layouts/               Définitions AZERTY / QWERTY
  Services/              Layout actif
```

## Prochaines étapes

- **Swipe** : collecter le chemin sur `PointerMoved` (TODO déjà posé) et brancher un décodeur
- **Suggestions** de mots au-dessus du clavier
- **Icône de notification** (tray) pour afficher / masquer sans barre des tâches
- Shift, chiffres, accents français
- Option MSIX empaquetée si besoin du Store / d’une install propre
