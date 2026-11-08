# SonNuage bureau

Appli de bureau de [SonNuage](https://github.com/SonNuage/sonnuage), serveur de musique auto-hébergé, pour
Windows, macOS et Linux. Les pages du client web s'affichent dans la webview du système (WebView2 sous Windows,
WKWebView sous macOS, WebKitGTK sous Linux) grâce à [Photino](https://www.tryphotino.io/). Pas de Chromium embarqué :
environ 40 Mo, runtime .NET compris, et 120 Mo de mémoire.

| Dossier | Rôle |
|---|---|
| `src/SonNuage.Desktop` | Application : hôte Photino, icône de la barre système, stockage des comptes |
| `build/` | Paquets par système (`package.sh`), Info.plist et icône macOS, lanceur Linux |

Les pages, les composants et le lecteur (`WebPlayer`) viennent de la bibliothèque `SonNuage.UI` du dépôt
[sonnuage](https://github.com/SonNuage/sonnuage) :

- **en CI** : paquet NuGet publié par la CI de `sonnuage` (version dans `Directory.Packages.props`)
- **en développement** : si `sonnuage` est cloné à côté de ce dépôt (`../sonnuage`), ses sources sont utilisées
  directement (voir `Directory.Build.props`, propriété `SonNuageRepo`).

Pour suivre une nouvelle version du serveur, monter `SonNuage.UI` dans `Directory.Packages.props` à la version
publiée (`X.Y.Z` pour un tag du serveur, `0.1.0-ci.N` pour un build de `main`).

## Développement

```bash
dotnet run --project src/SonNuage.Desktop
dotnet test
```

Les tests (`tests/SonNuage.Desktop.Tests`) couvrent le stockage local (`DesktopStore`) et l'adresse du serveur
(`AccountEndpoint`). L'icône de la barre système et la fenêtre s'essaient à la main, sur chaque système.

## Fonctionnement

- **Comptes** : au premier lancement, l'appli demande l'adresse du serveur puis l'identifiant. Plusieurs serveurs
  et comptes, comme sur Android. Changer de compte arrête la lecture et recharge la file de l'autre compte.
- **Barre système** : fermer la fenêtre la cache et la musique continue. L'icône la rouvre (clic sous Windows,
  menu ailleurs) et offre lecture/pause, suivant, précédent et Quitter. Sans icône (Linux sans indicateur), fermer
  la fenêtre quitte l'appli.
- **Touches média** et contrôles du système : par l'API MediaSession de la webview, comme dans un navigateur.
- **Données** : `%APPDATA%\SonNuage`, `~/Library/Application Support/SonNuage` ou `~/.config/SonNuage`. Réglages
  dans `settings.json`. La session est dans `session.json`, chiffrée par DPAPI sous Windows, lisible par le seul
  utilisateur sous macOS et Linux.

## Paquets

```bash
build/package.sh win-x64 0.1.0 dist
```

| Système | RID | Paquet |
|---|---|---|
| Windows | `win-x64` | `SonNuage-<version>-win-x64.zip` : `SonNuage.exe` autonome. WebView2 est fourni par Windows 10 et 11 |
| macOS | `osx-arm64`, `osx-x64` | `SonNuage-<version>-<rid>.zip` : `SonNuage.app`, non signée. Premier lancement : clic droit → Ouvrir |
| Linux | `linux-x64` | `SonNuage-<version>-linux-x64.tar.gz` : exécutable et lanceur `.desktop` |

Sous Linux, il faut `libwebkit2gtk-4.1-0` et, pour l'icône, `libayatana-appindicator3-1` (GNOME : avec l'extension
AppIndicator).

## CI/CD

- **GitLab** ([.gitlab-ci.yml](.gitlab-ci.yml)) : les tests et les quatre paquets en parallèle à chaque push, en artefacts.
  Sur `main`, semantic-release crée le tag `vX.Y.Z` et sa release, à laquelle le pipeline du tag joint les paquets. Le projet doit être autorisé dans
  sonnuage → Settings → CI/CD → Job token permissions, pour lire le paquet `SonNuage.UI`.
- **GitHub Actions** ([.github/workflows/ci.yml](.github/workflows/ci.yml)) : même chose, avec les sources de
  `sonnuage` extraites à côté. Dépôt privé : secret `SONNUAGE_REPO_TOKEN`.

## Versions

Les versions sont publiées par [semantic-release](https://semantic-release.gitbook.io/) ([.releaserc.json](.releaserc.json)),
d'après les messages de commit au format [Conventional Commits](https://www.conventionalcommits.org/fr/) :
`fix: …` donne un correctif (1.0.1), `feat: …` une version mineure (1.1.0), `feat!: …` ou un pied de page
`BREAKING CHANGE:` une version majeure (2.0.0). `docs`, `chore`, `ci`, `refactor`, `test` ne publient rien.
Sur `main`, le job `semantic-release` crée le tag `vX.Y.Z` et la release GitLab avec ses notes. Le pipeline du
tag publie ensuite la version. Il utilise `GITLAB_TOKEN`, jeton du groupe `sonnuage` (variable masquée et protégée,
tags `v*` protégés).

## Limites connues

- macOS et Linux ne sont pas encore essayés sur une vraie machine. Seul Windows l'a été.
- Pré-chargement des pistes suivantes inactif : il passe par `fetch` depuis la page et le serveur n'envoie pas
  d'en-têtes CORS sur le flux. La lecture, elle, fonctionne.

## Licence

SonNuage est un logiciel libre sous licence [GNU AGPL v3.0 ou ultérieure](LICENSE) : chacun peut l'utiliser,
l'étudier, le modifier et le redistribuer. Une version modifiée, y compris proposée comme service en ligne, doit
publier son code source sous la même licence.

Copyright © 2026 pmj.bailly et les contributeurs de SonNuage.
