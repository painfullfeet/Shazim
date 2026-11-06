<div align="center">

<img src="docs/banner.png" alt="Shazim — reconnaissance musicale pour Windows" width="100%">

# Shazim

**Reconnais la musique jouée sur ton PC ou autour de toi, en un clic.**<br>
Application Windows gratuite et open source pour identifier une chanson : vidéo YouTube, stream Twitch, jeu, Discord, film, radio… ou la musique de la pièce via ton micro.

[![Télécharger](https://img.shields.io/github/v/release/painfullfeet/Shazim?label=T%C3%A9l%C3%A9charger&style=for-the-badge&color=ff4d6d&logo=windows)](https://github.com/painfullfeet/Shazim/releases/latest)
[![Téléchargements](https://img.shields.io/github/downloads/painfullfeet/Shazim/total?style=for-the-badge&color=2b2540&label=T%C3%A9l%C3%A9chargements)](https://github.com/painfullfeet/Shazim/releases)

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 10](https://img.shields.io/badge/.NET 10-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-5c2d91)
[![Licence GPL-3.0](https://img.shields.io/badge/licence-GPL--3.0-blue)](LICENSE)
![Langues](https://img.shields.io/badge/langues-FR%20%7C%20EN%20%7C%20ES%20%7C%20DE-ff4d6d)

**Français** · [English](README.en.md) · [Español](README.es.md) · [Deutsch](README.de.md)

</div>

---

<p align="center">
  <img src="docs/screenshots/home.png" alt="Écran d'écoute de Shazim avec le gros bouton qui réagit à la musique" width="49%">
  <img src="docs/screenshots/library.png" alt="Bibliothèque des morceaux shazimés" width="49%">
</p>

## Sommaire

- [Pourquoi Shazim ?](#pourquoi-shazim-)
- [Fonctionnalités](#fonctionnalités)
- [Installation](#installation)
- [Guide d'utilisation](#guide-dutilisation)
- [Raccourcis clavier](#raccourcis-clavier)
- [Confidentialité](#confidentialité)
- [Questions fréquentes](#questions-fréquentes)
- [Compiler depuis les sources](#compiler-depuis-les-sources)
- [Crédits et licences](#crédits-et-licences)

## Pourquoi Shazim ?

Sur téléphone, il suffit d'ouvrir Shazam. Sur PC, c'est plus compliqué : la musique sort du casque, le téléphone ne l'entend pas.
**Shazim écoute directement le son de ton ordinateur** (sans micro, sans câble) et te donne le titre en quelques secondes.
Il peut aussi utiliser ton micro pour reconnaître la musique de la pièce, ou les deux en même temps.

- ✅ Gratuit, sans pub, sans compte, sans clé API
- ✅ Installation en un clic, **sans droits administrateur**
- ✅ L'audio ne quitte jamais ton PC : seule une empreinte est envoyée
- ✅ Interface sombre et animée, 5 couleurs, 4 langues

## Fonctionnalités

| | |
|---|---|
| 🎧 **Son du PC, micro ou les deux** | Capte ce que tu entends (WASAPI loopback), le micro (casque compris) ou les deux en parallèle : la première source qui reconnaît le morceau l'emporte. |
| ⚡ **Résultat rapide** | La recherche commence après 4 s d'écoute et continue (8 s, 12 s) tant que rien n'est trouvé. |
| 💿 **Fiche complète** | Pochette, album, année, genre, label, extrait de 30 s, plateformes où l'écouter (Spotify, Apple Music, Deezer, YouTube Music), nombre de fois shazimé et historique. |
| 📚 **Bibliothèque** | Tous tes morceaux, recherche, favoris, tris, écoute des extraits dans la liste, export CSV ou JSON. |
| 🔁 **Auto Shazim** | Écoute en continu en arrière-plan et note chaque nouveau morceau (idéal pour une playlist, un live ou un mix). |
| 🌊 **Bouton vivant** | Le bouton d'écoute réagit à la musique : aura façon visualiseur ou pulsation du logo au rythme. |
| 🎨 **Thèmes et couleurs** | 4 thèmes (*Nuit*, *Minuit* noir pur pour OLED, *Clair*, *Suivre Windows*), 5 couleurs d'accent ou ta propre couleur (teinte + code hexadécimal). |
| 🎤 **Paroles synchronisées** | Façon karaoké sur la fiche du morceau : la ligne chantée s'allume et défile toute seule, avec un réglage ± si besoin. Source : [LRCLIB](https://lrclib.net). |
| 🪟 **Widget compact** | Petite carte toujours visible : pochette, titre, artiste, Auto Shazim, bouton Shazimer et la ligne des paroles en cours. Déplaçable, transparence réglable. |
| 📊 **Statistiques** | Morceaux et shazims, graphique des 30 derniers jours, morceau du mois, le plus shazimé, tout premier morceau, top artistes et genres, PC / micro / Auto. |
| 🔔 **Pop-up Shazim** | Quand Auto Shazim trouve un morceau, une petite carte s'affiche au-dessus de la barre des tâches (❤ et Ouvrir), sans voler le focus. Ou une notification Windows, avec ou sans son. |
| ⬆️ **Mises à jour intégrées** | Shazim vérifie GitHub une fois par jour, télécharge la nouvelle version, contrôle sa somme SHA-256 et s'installe tout seul. |
| 🧭 **Toujours là** | Icône près de l'horloge, raccourcis globaux, lancement avec Windows, démarrage plus rapide. |
| 🌍 **4 langues** | Français, English, Español, Deutsch — changement instantané, sans redémarrer. |

<p align="center">
  <img src="docs/screenshots/track.png" alt="Fiche d'un morceau : pochette, plateformes, informations et historique" width="80%">
</p>

### Nouveautés de la version 2.1

<p align="center">
  <img src="docs/screenshots/lyrics.png" alt="Paroles synchronisées sur la fiche d'un morceau" width="49%">
  <img src="docs/screenshots/stats.png" alt="Page Statistiques" width="49%">
</p>
<p align="center">
  <img src="docs/screenshots/theme-light.png" alt="Thème Clair" width="49%">
  <img src="docs/screenshots/widget.png" alt="Widget compact avec la ligne des paroles" width="24%">
  <img src="docs/screenshots/toast.png" alt="Pop-up Shazim quand un morceau est trouvé" width="24%">
</p>

## Installation

### 1. Télécharger

Va sur la page **[Releases](https://github.com/painfullfeet/Shazim/releases/latest)** et télécharge `Shazim-Setup-x.y.z.exe` (environ 230 Mo, rien d'autre à installer : .NET est inclus).

### 2. Lancer l'installateur

Double-clique sur le fichier téléchargé, choisis tes options puis clique sur **Installer**.

<p align="center">
  <img src="docs/screenshots/installer.png" alt="Installateur de Shazim" width="36%">
  &nbsp;&nbsp;
  <img src="docs/screenshots/installer-done.png" alt="Installation terminée" width="36%">
</p>

> [!NOTE]
> **Windows affiche « Windows a protégé votre ordinateur » ?** C'est normal pour un logiciel gratuit qui n'a pas de certificat de signature payant.
> Clique sur **Informations complémentaires** puis **Exécuter quand même**. Le code source est entièrement public ici, et tu peux vérifier le fichier avec la somme SHA-256 publiée dans la release.

L'installation se fait **pour ton compte Windows uniquement**, dans `%LOCALAPPDATA%\Programs\Shazim`, avec un raccourci dans le menu Démarrer (et sur le Bureau si tu le souhaites).

### Mettre à jour

Shazim te prévient tout seul quand une nouvelle version sort (**Réglages → À propos → Rechercher les mises à jour**) : un clic sur **Mettre à jour** la télécharge, vérifie sa somme SHA-256 et l'installe. Tu peux aussi télécharger la nouvelle version et la lancer : elle remplace l'ancienne. **Ta bibliothèque et tes réglages sont conservés.**

### Désinstaller

**Paramètres Windows → Applications → Applications installées → Shazim → Désinstaller.**
Tu peux choisir de garder ou de supprimer ta bibliothèque.

## Guide d'utilisation

### Identifier un morceau

1. Lance la musique (vidéo, jeu, stream, Spotify…).
2. Dans Shazim, touche le **gros bouton** de l'écran *Écouter* (ou appuie sur `Espace`).
3. En quelques secondes, la fiche du morceau s'affiche. Il est automatiquement ajouté à ta bibliothèque.

Même quand Shazim est réduit, appuie sur **`Ctrl+Maj+S`** depuis n'importe quelle application.

### Choisir ce que Shazim écoute

Sous le bouton, ou dans **Réglages → Ce que Shazim écoute** :

- **Son du PC** : tout ce qui sort de tes haut-parleurs ou de ton casque. Aucun micro nécessaire.
- **Micro** : la musique dans la pièce (enceinte, soirée, magasin…). Le micro du casque marche aussi.
- **Les deux** : les deux sources sont analysées en même temps.

Le bouton **Tester** des réglages vérifie que la source choisie reçoit bien du son.

### La bibliothèque

- Clique sur un morceau pour ouvrir sa fiche : extrait, plateformes, informations, historique d'écoute.
- ▶ écoute l'extrait directement dans la liste ; l'icône 🔊 règle son volume.
- ❤ ajoute aux favoris ; le filtre **Favoris** n'affiche qu'eux.
- Le bouton d'export enregistre ta bibliothèque en **CSV** (Excel) ou **JSON**.

### Auto Shazim

Active l'interrupteur **Auto Shazim** dans la barre latérale : Shazim écoute en boucle et enregistre chaque nouveau morceau, même fenêtre fermée. Les doublons sont ignorés pendant un délai réglable (15 min par défaut). Raccourci : **`Ctrl+Maj+A`**.

### Réglages utiles

<p align="center">
  <img src="docs/screenshots/settings.png" alt="Réglages de Shazim" width="80%">
</p>

- **Langue** : Français, English, Español, Deutsch.
- **Effet du bouton** : *Aura* (vagues qui suivent la musique), *Pulsation* (le logo bat au rythme) ou *Aucun*.
- **Couleur** : rouge, bleu, violet, vert ou orange.
- **Démarrage** : lancer avec Windows, rester dans la zone de notification, fermer la fenêtre sans quitter.
- **Raccourcis globaux** et **notifications** personnalisables.

## Raccourcis clavier

| Raccourci | Action |
|---|---|
| `Ctrl+Maj+S` | Shazimer (partout dans Windows) |
| `Ctrl+Maj+A` | Activer / couper Auto Shazim (partout dans Windows) |
| `Espace` | Shazimer / écouter l'extrait sélectionné |
| `Ctrl+1` · `Ctrl+2` · `Ctrl+3` · `Ctrl+4` | Écouter · Bibliothèque · Statistiques · Réglages |
| `Ctrl+B` | Réduire / agrandir la barre latérale |
| `F11` | Plein écran |
| `Entrée` | Ouvrir le morceau sélectionné |
| `Échap` | Fermer la fiche / revenir |

## Confidentialité

- L'audio est gardé **en mémoire** le temps de l'écoute (12 s au plus) puis effacé : **rien n'est enregistré sur le disque**.
- Seule une **empreinte spectrale** calculée sur ton PC est envoyée au service de reconnaissance, jamais le son.
- Pas de compte, pas de publicité, pas de statistiques d'utilisation.
- **Aucune géolocalisation** : le service de reconnaissance exige une position ; Shazim envoie toujours la même position fixe et générique (centre de la France), jamais la tienne.
- **Mises à jour signées** : l'application n'installe une mise à jour que si sa signature (clé gardée hors de GitHub) et son SHA-256 sont valides. Détails dans [SECURITY.md](SECURITY.md).
- **Liens filtrés** : seuls les liens web et Spotify/Deezer reçus des services en ligne peuvent être ouverts.
- Tes données restent chez toi, dans `%LOCALAPPDATA%\Shazim` : bibliothèque (`shazim.db`), réglages, pochettes en cache, journal.
- Les paroles sont demandées à [LRCLIB](https://lrclib.net) avec seulement l'artiste, le titre, l'album et la durée du morceau, puis gardées en cache. La recherche de mises à jour interroge GitHub sans rien envoyer de personnel. Les deux se désactivent dans les réglages.

## Questions fréquentes

<details>
<summary><b>« Aucun son détecté » : que faire ?</b></summary>

Vérifie que la musique joue vraiment et pas en sourdine. Dans **Réglages**, choisis la bonne **sortie audio** (celle de ton casque si tu en utilises un) puis clique sur **Tester**.
</details>

<details>
<summary><b>« Morceau introuvable »</b></summary>

Réessaie pendant un passage plus caractéristique (refrain, mélodie). Les remix très rares, les morceaux très récents ou les passages parlés sont parfois inconnus. Le micro marche moins bien s'il y a beaucoup de bruit autour.
</details>

<details>
<summary><b>Faut-il un compte Shazam ou une clé API ?</b></summary>

Non. Shazim fonctionne sans compte et sans clé.
</details>

<details>
<summary><b>Mon antivirus ou SmartScreen bloque le fichier</b></summary>

Le fichier n'est pas signé avec un certificat payant, ce qui déclenche parfois un avertissement. Le code est public ; tu peux vérifier l'empreinte SHA-256 indiquée dans la release (`Get-FileHash .\Shazim-Setup-x.y.z.exe`) ou compiler toi-même l'application.
</details>

<details>
<summary><b>Est-ce que ça marche hors ligne ?</b></summary>

Non : la reconnaissance a besoin d'Internet. La bibliothèque, elle, reste consultable hors ligne.
</details>

<details>
<summary><b>Où sont mes données ? Comment tout effacer ?</b></summary>

Dans `%LOCALAPPDATA%\Shazim` (bouton **Ouvrir le dossier des données** dans les réglages). À la désinstallation, coche « Supprimer aussi ma bibliothèque et mes réglages ».
</details>

<details>
<summary><b>Shazim existe-t-il sur Mac ou Linux ?</b></summary>

Non, Shazim est une application Windows (10 et 11, 64 bits). Sous Linux, le projet [SongRec](https://github.com/marin-m/SongRec) dont s'inspire la reconnaissance est une excellente alternative.
</details>

## Compiler depuis les sources

Prérequis : Windows 10/11 et le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/painfullfeet/Shazim.git
cd Shazim
./build.ps1        # compile
./run.ps1          # compile et lance
./test.ps1         # tests unitaires
./release.ps1      # produit dist\Shazim-Setup-<version>.exe (un seul fichier)
./install.ps1      # ouvre l'installateur produit
```

L'architecture du code est décrite dans [ARCHITECTURE.md](ARCHITECTURE.md). Les contributions et les signalements de bugs sont les bienvenus dans les [Issues](https://github.com/painfullfeet/Shazim/issues).

<a id="licences"></a>

## Crédits et licences

Créé par **Painfullfeet**, avec l'aide de **[Claude](https://claude.ai)**, l'assistant IA d'Anthropic.

| Élément | Source | Licence |
|---|---|---|
| Algorithme de signature audio | Portage C# de [SongRec](https://github.com/marin-m/SongRec) (marin-m) | GPL-3.0 |
| Capture et lecture audio | [NAudio](https://github.com/naudio/NAudio) | MIT |
| Base de données | [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | MIT |
| Police | [Outfit](https://github.com/Outfitio/Outfit-Fonts) | [SIL OFL 1.1](assets/licenses/Outfit-OFL.txt) |
| Icônes | [Phosphor Icons](https://phosphoricons.com) | [MIT](assets/licenses/Phosphor-MIT.txt) |
| Inspiration design | [ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill) | — |
| Métadonnées et extraits | Service de reconnaissance Shazam | — |
| Paroles synchronisées | [LRCLIB](https://lrclib.net) (base libre et gratuite) | — |

Shazim est un logiciel libre distribué sous licence **[GNU GPL v3](LICENSE)**.

> [!IMPORTANT]
> Shazim est un projet personnel et indépendant. Il **n'est ni affilié, ni approuvé, ni soutenu par Shazam ou Apple Inc.**
> « Shazam » est une marque d'Apple Inc. Shazim utilise un service de reconnaissance non documenté qui peut changer ou cesser de fonctionner à tout moment.
> Les pochettes, titres et extraits appartiennent à leurs ayants droit.

---

<div align="center">
<sub>Mots-clés : reconnaissance musicale PC · identifier une chanson sur Windows · Shazam pour PC · trouver le titre d'une musique · audio fingerprinting · music recognition Windows · song identifier desktop</sub>
<br><br>
<sub>Fait avec ❤ par Painfullfeet · Si Shazim t'est utile, laisse une ⭐ !</sub>
</div>
