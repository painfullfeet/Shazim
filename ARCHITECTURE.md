# Architecture

```
src/Shazim/
  Core/    logique métier, sans interface (net8.0-windows, NAudio)
  Data/    stockage SQLite
  App/     application WPF (exécutable Shazim.exe)
  Tests/   tests xUnit
```

## Chemin d'une identification

```
Clic / raccourci / menu / Auto Shazim
  → IdentificationService.IdentifyAsync(sources)
      → IAudioCaptureService.StartSession     une session par source : son du PC (loopback) et/ou micro
      → toutes les 4 s jusqu'au maximum :
          Snapshot (12 dernières secondes) de chaque source → test de silence
          → ShazamRecognitionService           en parallèle sur les sources qui entendent quelque chose
              ShazamSignature (16 kHz mono, FFT 2048, pics spectraux)
              → POST amp.shazam.com/discovery/v5/.../tag
      → LibraryService.AddTrack + RecordDetection (sauf doublon en mode auto)
      → événement TrackIdentified
```

## Core

| Élément | Rôle |
|---|---|
| `AppPaths` | Dossier de données `%LOCALAPPDATA%\Shazim` (ou `SHAZIM_DATA_DIR`), transfert depuis l'ancien nom |
| `CoreText` | Textes du cœur traduits par l'app (le français sert de repli) |
| `Audio/AudioClip`, `AudioDsp` | Échantillons, mixage mono, rééchantillonnage à noyau sinc tabulé, FFT, WAV |
| `Services/AudioCaptureService` | Sessions WASAPI (sortie ou micro, par défaut ou choisi), niveau RMS en direct |
| `Recognition/ShazamSignature` | Portage de l'algorithme SongRec (vérifié contre sa source), encodage binaire + CRC32 |
| `Recognition/ShazamRecognitionService` | Requête et lecture de la réponse : métadonnées, extrait, plateformes disponibles |
| `Recognition/IdentificationService` | Pipeline progressif multi-sources, silence, erreurs réseau, annulation |
| `Services/LibraryService` | Bibliothèque, recherche sans accents, tris, favoris, historique par morceau, export |
| `Services/DeduplicationService` | Fenêtre anti-doublon pour Auto Shazim |
| `Services/FileLogger` | Journal fichier avec gabarits `{Nom}` et rotation à 2 Mo |

## Data

`SqliteTrackRepository` : schéma créé une seule fois, clés étrangères actives (suppression en cascade), mode WAL,
migration des anciennes bases, requêtes jointes (compteur d'écoutes, première et dernière écoute, favoris).

## App

| Élément | Rôle |
|---|---|
| `App.xaml.cs` | Instance unique, hôte d'injection de dépendances, zone de notification, raccourcis, erreurs globales |
| `SettingsService` | `settings.json` (v4) : source audio, couleur, Auto Shazim… ; écriture atomique, migrations, purge des anciennes clés |
| `Services/IdentifyController` | État partagé des identifications manuelles (bouton, mini lecteur, menu, raccourci) |
| `Services/AutoShazamService` | Boucle d'arrière-plan avec attente interrompable et repli progressif en cas d'erreur |
| `Services/PreviewPlayer` | Lecture des extraits (Media Foundation + WASAPI), un seul lecteur pour toute l'app |
| `Services/ThemeService` | Couleurs d'accent, appliquées à chaud via `DynamicResource` |
| `Services/TrayService`, `HotkeyService`, `StartupService` | Icône et notifications, `RegisterHotKey`, clé `Run` de Windows |
| `Views/HomeView` | Bouton d'écoute avec aura ou pulsation au rythme (`MusicVisualizer`), résultat en plein écran |
| `Views/LibraryView` | Liste des morceaux avec extraits ; ouvre `TrackDetailView` en pop-up |
| `Views/TrackDetailView` | Fiche réutilisable : extrait, plateformes, informations, historique |
| `Views/SettingsView`, `MiniWindow` | Réglages enregistrés automatiquement, mini lecteur flottant |
| `Themes/Theme.xaml`, `Icons.xaml` | Palette sombre, police Outfit intégrée, icônes Phosphor vectorielles, styles de tous les contrôles |
| `Infrastructure/Loc` | Langues (fr, en, es, de) : fichiers `Strings/*.json` intégrés, `{infra:T clé}` en XAML, changement à chaud |
| `Setup/Installer`, `SetupWindow` | Le même `Shazim.exe` sert d'installateur (`Shazim-Setup*.exe` ou `--setup`) et de désinstalleur (`--uninstall`), par utilisateur, sans droits administrateur |
| `Infrastructure/DevSnapshot` | Outil de développement (inactif sans `SHAZIM_SNAPSHOT_DIR`) : captures d'écran hors écran pour le README |

## Choix

- **Un seul service de reconnaissance** : Shazam, sans clé. Les anciens fournisseurs (AcoustID, AudD) et leurs clés ont été retirés ; la migration efface toute clé encore présente sur le disque.
- **Pas de capture permanente** : les sessions WASAPI sont ouvertes le temps d'une identification puis fermées.
- **Une seule `HttpClient`** pour toute l'app, avec délai d'expiration par requête.

## Publication

`release.ps1` publie un exécutable unique autonome (`PublishSingleFile`, .NET inclus, compressé) dans
`dist\Shazim-Setup-<version>.exe` avec sa somme SHA-256. Le logo, l'icône, la police, les textes et les licences
sont des ressources intégrées : aucun fichier n'est nécessaire à côté de l'exécutable.

## Ajouter une langue

1. Copier `src/Shazim/App/Strings/en.json` en `<code>.json` et traduire les valeurs (garder `{0}`, `{1}`).
2. Ajouter `("<code>", "Nom")` à `Loc.Languages` et la culture correspondante dans `Loc.SetLanguage`.
