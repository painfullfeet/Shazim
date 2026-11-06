# Sécurité / Security

## Signaler une vulnérabilité / Reporting a vulnerability

**Merci de ne pas ouvrir d'issue publique pour une faille de sécurité.**
Utilise le signalement privé de GitHub : onglet **Security → Report a vulnerability**
([lien direct](https://github.com/painfullfeet/Shazim/security/advisories/new)).

**Please do not open a public issue for a security problem.** Use GitHub's private reporting:
**Security → Report a vulnerability** ([direct link](https://github.com/painfullfeet/Shazim/security/advisories/new)).

Indique si possible la version de Shazim, les étapes pour reproduire et l'impact. Une première réponse est donnée
sous 7 jours ; un correctif est publié dès que possible, avec crédit si tu le souhaites.

## Versions prises en charge / Supported versions

Seule la dernière version publiée reçoit des correctifs. / Only the latest release gets fixes.

## Ce que Shazim fait pour rester sûr / How Shazim stays safe

- **Mises à jour signées** : `SHA256SUMS.txt` est signé (ECDSA P-256) avec une clé privée qui ne quitte jamais le PC de
  l'auteur. L'application vérifie la signature avec la clé publique intégrée, puis le SHA-256 de l'installateur, et
  n'accepte que des fichiers téléchargés depuis les releases de ce dépôt. Un compte GitHub piraté ne suffit donc pas
  à distribuer une fausse mise à jour.
  *Signed updates: the checksum file is signed with a key kept off GitHub; the app checks the signature, then the
  installer hash, and only downloads from this repository's releases.*
- **Liens filtrés** : les liens reçus des services en ligne (Shazam, LRCLIB, GitHub) ne sont ouverts que s'ils sont
  `https`/`http`, `spotify:` ou `deezer:` ; fichiers locaux, partages réseau et autres protocoles sont refusés.
- **Audio** : capturé en mémoire le temps de l'écoute puis effacé ; seule une empreinte spectrale est envoyée.
  La position envoyée au service de reconnaissance est fixe et générique (jamais la vraie position).
- **Sans droits administrateur** : installation et désinstallation pour l'utilisateur courant uniquement.
- **Dépendances** : auditées à chaque build GitHub Actions (`dotnet list package --vulnerable`), mises à jour par Dependabot.

## Vérifier un téléchargement / Verify a download

```powershell
Get-FileHash .\Shazim-Setup-x.y.z.exe   # compare avec SHA256SUMS.txt de la release
```

L'exécutable n'est pas signé Authenticode (certificat payant) : Windows SmartScreen peut afficher un avertissement.
*The executable has no Authenticode signature, so SmartScreen may warn on first run.*
