# SonNuage bureau

[English](README.md) · **Français**

Appli de bureau de [SonNuage](https://github.com/SonNuage/sonnuage), serveur de musique auto-hébergé, pour
Windows, macOS et Linux. Les pages du client web s'affichent dans la webview du système (WebView2 sous Windows,
WKWebView sous macOS, WebKitGTK sous Linux) grâce à [Photino](https://www.tryphotino.io/). Pas de Chromium embarqué :
environ 40 Mo, runtime .NET compris, et 120 Mo de mémoire.

État : fonctionne, utilisée de temps en temps, donc moins testée que le lecteur web et l'application Android. Voir
[Limites connues](#limites-connues).

| Dossier | Rôle |
|---|---|
| `src/SonNuage.Desktop` | Application : hôte Photino, icône de la barre système, stockage des comptes |
| `build/` | Paquets par système (`package.sh`) : installeur Windows (`windows/`), `.deb` (`linux/`), signature et droits macOS (`macos/`), Info.plist, icône, lanceur Linux |

Les pages, les composants et le lecteur (`WebPlayer`) viennent de la bibliothèque `SonNuage.UI` du dépôt
[sonnuage](https://github.com/SonNuage/sonnuage) :

- **en CI** : paquet NuGet publié par la CI de `sonnuage` (version dans `Directory.Packages.props`)
- **en développement** : si `sonnuage` est cloné à côté de ce dépôt (`../sonnuage`), ses sources sont utilisées
  directement (voir `Directory.Build.props`, propriété `SonNuageRepo`).

Pour suivre une nouvelle version du serveur, monter `SonNuage.UI` dans `Directory.Packages.props` à la version
publiée `X.Y.Z` (tag du serveur). Les builds `0.1.0-ci.N` de `main` ne restent pas dans le registre : ne pas s'y
fier pour une version de l'appli. Le test `CompositionTests` signale une dépendance nouvelle qui manquerait.

## Développement

```bash
dotnet run --project src/SonNuage.Desktop
dotnet test
```

Les tests (`tests/SonNuage.Desktop.Tests`) couvrent le stockage local (`DesktopStore`), l'adresse du serveur
(`AccountEndpoint`), l'instance unique, les entrées de démarrage de chaque système, les mises à jour (lecture de la
release, choix du fichier, somme SHA-256) et les composants affichés par SonNuage.UI. L'icône de la barre système,
la fenêtre et les installeurs s'essaient à la main, sur chaque système.

## Fonctionnement

- **Comptes** : au premier lancement, l'appli demande l'adresse du serveur puis l'identifiant. Plusieurs serveurs
  et comptes, comme sur Android. Changer de compte arrête la lecture et recharge la file de l'autre compte.
- **Barre système** : fermer la fenêtre la cache et la musique continue. L'icône la rouvre (clic sous Windows,
  menu ailleurs) et offre lecture/pause, suivant, précédent et Quitter. Sans icône (Linux sans indicateur), fermer
  la fenêtre quitte l'appli.
- **Touches média** et contrôles du système : par l'API MediaSession de la webview, comme dans un navigateur.
- **Une seule appli** : la relancer ramène la fenêtre de celle qui tourne. La première tient `instance.lock` (dossier
  de données) et écoute sur une socket Unix `app.sock` ; macOS, lui, envoie « reopen » à l'appli (Finder, Dock).
- **Lancement avec la session** (désactivé par défaut) et **démarrage caché** (seule l'icône de la barre système
  apparaît), dans Réglages → Appli de bureau. Entrée du système : valeur `SonNuage` sous
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `~/Library/LaunchAgents/org.sonnuage.desktop.plist`,
  `~/.config/autostart/sonnuage.desktop`. L'appli lancée ainsi reçoit `--autostart`.
- **Mises à jour** : la dernière release GitLab de ce projet est lue 30 s après le démarrage puis toutes les 6 heures
  (adresse posée au build par la CI, `UPDATE_FEED` ; aucune vérification en développement ni pour les builds
  `0.1.0-ci.N`). Un bandeau propose la nouvelle version : l'installeur Windows s'exécute en silence et relance
  l'appli, l'image disque macOS s'ouvre dans le Finder, le `.deb` dans la logithèque. Le fichier est contrôlé par le
  `SHA256SUMS` de la release. Archive (zip, tar.gz) ou échec : la page de la version s'ouvre dans le navigateur. Le
  projet GitLab doit être public (au moins ses releases et son registre de paquets) pour que l'appli y accède.
- **Données** : `%APPDATA%\SonNuage`, `~/Library/Application Support/SonNuage` ou `~/.config/SonNuage`. Réglages
  dans `settings.json`. La session est dans `session.json`, chiffrée par DPAPI sous Windows, lisible par le seul
  utilisateur sous macOS et Linux.

## Paquets

```bash
build/package.sh win-x64 0.1.0 dist
```

| Système | RID | Paquets |
|---|---|---|
| Windows | `win-x64` | `SonNuage-<version>-win-x64-setup.exe` : installeur NSIS pour l'utilisateur courant (sans droits d'administration, dans `%LOCALAPPDATA%\Programs\SonNuage`, désinstallable depuis les Paramètres) · `SonNuage-<version>-win-x64.zip` : `SonNuage.exe` autonome. WebView2 est fourni par Windows 10 et 11 |
| macOS | `osx-arm64`, `osx-x64` | `SonNuage-<version>-<rid>.dmg` : image disque, glisser SonNuage dans Applications · `SonNuage-<version>-<rid>.zip` : `SonNuage.app`. Non signée sans certificat : premier lancement par clic droit → Ouvrir (ou Réglages Système → Confidentialité → Ouvrir quand même) |
| Linux | `linux-x64` | `sonnuage_<version>_amd64.deb` : Debian, Ubuntu (dans `/opt/sonnuage`, commande `sonnuage`) · `SonNuage-<version>-linux-x64.tar.gz` : exécutable et lanceur `.desktop` |

Outils : `zip`, `makensis` et `osslsigncode` (Windows), `dpkg-deb` (Linux), `xorriso` et `curl` (macOS), tous sous
Linux : la CI n'a pas besoin de machine Windows ni de Mac. L'image macOS est une ISO 9660 avec Rock Ridge (droits et
lien vers Applications), que macOS ouvre comme un `.dmg`, non compressée.

### Signature

Chaque étape ne s'active que si ses variables existent (GitLab : Settings → CI/CD → Variables, **protégées** pour
ne servir qu'aux tags `v*`, mots de passe **masqués**). Sans elles, les paquets sont produits non signés.

| Système | Variables | Étape |
|---|---|---|
| Windows | `WINDOWS_CERT_PFX` (type File, certificat de signature de code .pfx), `WINDOWS_CERT_PASSWORD`, facultatif `WINDOWS_TIMESTAMP_URL` | `build/windows/sign.sh` : `SonNuage.exe` puis l'installeur, par `osslsigncode` |
| macOS | `MACOS_CERT_P12` (type File, « Developer ID Application » et sa clé), `MACOS_CERT_PASSWORD` | `build/macos/sign.sh` : signature avec runtime renforcé (`build/macos/entitlements.plist`) par `rcodesign` 0.27.0, téléchargé à ce moment (`RCODESIGN_SHA256` pour vérifier l'archive) |
| macOS | `APPLE_API_KEY_JSON` (type File, clé App Store Connect convertie par `rcodesign encode-app-store-connect-api-key`) | notarisation et agrafage du ticket sur `SonNuage.app`, avant le `.dmg` |

Aucune valeur secrète n'est affichée dans le journal : les mots de passe passent par des fichiers temporaires.

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
- Pré-chargement des pistes suivantes : il passe par `fetch` depuis la page (origine `http://localhost` sous
  Windows, `app://localhost` ailleurs). Le serveur l'autorise sur le flux et les pochettes à partir de sa version
  1.25.0 ; avec un serveur plus ancien, seule la lecture fonctionne.
- Réglages « Appli de bureau » et bandeau de mise à jour : affichés par SonNuage.UI à partir de 1.25.0 (déclarés
  par `DesktopPlatform.SettingsSections` et `Notices`). Avec 1.24.0, l'appli se construit mais ne les montre pas.

## Licence

SonNuage est un logiciel libre sous licence [GNU AGPL v3.0 ou ultérieure](LICENSE) : chacun peut l'utiliser,
l'étudier, le modifier et le redistribuer. Une version modifiée, y compris proposée comme service en ligne, doit
publier son code source sous la même licence.

Copyright © 2026 pmj.bailly et les contributeurs de SonNuage.
