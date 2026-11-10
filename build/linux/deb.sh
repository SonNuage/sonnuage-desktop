#!/bin/sh
# Paquet Debian/Ubuntu de l'appli : deb.sh <dossier publié> <version> <fichier .deb>
#   /opt/sonnuage            exécutable et pages (wwwroot)
#   /usr/bin/sonnuage        lien vers l'exécutable
#   /usr/share/applications  lanceur · /usr/share/icons/hicolor/256x256/apps : icône
# Dépendances : la webview (WebKitGTK 4.1) ; l'icône de la barre système (AppIndicator) est recommandée.
set -eu

APPDIR="$1"
VERSION="$2"
DEB="$3"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PKG="$(mktemp -d)"
trap 'rm -rf "$PKG"' EXIT

mkdir -p "$PKG/DEBIAN" "$PKG/opt/sonnuage" "$PKG/usr/bin" "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps"
cp -r "$APPDIR/." "$PKG/opt/sonnuage/"
chmod 755 "$PKG/opt/sonnuage/SonNuage"
ln -s /opt/sonnuage/SonNuage "$PKG/usr/bin/sonnuage"
sed 's|^Exec=.*|Exec=sonnuage|; /^#/d' "$ROOT/build/sonnuage.desktop" > "$PKG/usr/share/applications/sonnuage.desktop"
cp "$APPDIR/wwwroot/icons/sonnuage.png" "$PKG/usr/share/icons/hicolor/256x256/apps/sonnuage.png"

SIZE="$(du -sk "$PKG/opt" | cut -f1)"
cat > "$PKG/DEBIAN/control" <<EOF
Package: sonnuage
Version: $VERSION
Architecture: amd64
Maintainer: SonNuage <noreply@sonnuage.invalid>
Installed-Size: $SIZE
Depends: libwebkit2gtk-4.1-0, libgtk-3-0
Recommends: libayatana-appindicator3-1
Section: sound
Priority: optional
Homepage: https://github.com/SonNuage/sonnuage
Description: SonNuage, lecteur du serveur de musique auto-hébergé
 Appli de bureau de SonNuage : bibliothèque, playlists, lecture en arrière-plan
 avec icône dans la barre système et touches média.
EOF

dpkg-deb --root-owner-group --build "$PKG" "$DEB" > /dev/null
echo "$DEB"
