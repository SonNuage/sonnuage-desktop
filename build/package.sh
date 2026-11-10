#!/bin/sh
# Paquets de l'appli de bureau pour un système : package.sh <rid> <version> <dossier de sortie>
#   win-x64              SonNuage-<version>-win-x64-setup.exe  installeur (NSIS, par utilisateur, sans droits admin)
#                        SonNuage-<version>-win-x64.zip        SonNuage.exe autonome, sans installation
#   linux-x64            sonnuage_<version>_amd64.deb          paquet Debian/Ubuntu (dans /opt/sonnuage)
#                        SonNuage-<version>-linux-x64.tar.gz   exécutable, icône, lanceur .desktop
#   osx-arm64, osx-x64   SonNuage-<version>-<rid>.dmg          image disque : glisser SonNuage dans Applications
#                        SonNuage-<version>-<rid>.zip          SonNuage.app seule
# Publication autonome (runtime .NET inclus) en un seul fichier compressé, à côté du dossier wwwroot.
# Outils : zip ; makensis et osslsigncode (Windows), dpkg-deb (Linux), xorriso et curl (macOS).
# Signature (Windows, macOS) seulement si la CI fournit les certificats : build/windows/sign.sh, build/macos/sign.sh.
# UPDATE_FEED : adresse de la dernière release, que l'appli consulte pour ses mises à jour (aucune si vide).
# Arguments de dotnet publish en plus après le dossier de sortie (par exemple -p:SonNuageRepo=...).
set -eu

RID="$1"
VERSION="$2"
OUT="$3"
shift 3
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"
NAME="SonNuage-$VERSION-$RID"
if [ -n "${UPDATE_FEED:-}" ]; then set -- "$@" -p:UpdateFeed="$UPDATE_FEED"; fi

dotnet publish "$ROOT/src/SonNuage.Desktop/SonNuage.Desktop.csproj" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -p:Version="$VERSION" -o "$WORK/app" "$@"
mkdir -p "$OUT"

case "$RID" in
  win-*)
    "$ROOT/build/windows/sign.sh" "$WORK/app/SonNuage.exe"
    (cd "$WORK/app" && zip -qr "$OUT/$NAME.zip" .)
    # Version de fichier Windows : quatre nombres (0.1.0-ci.42 → 0.1.0.0).
    NUMERIC="$(echo "$VERSION" | sed -E 's/^([0-9]+\.[0-9]+\.[0-9]+).*/\1/').0"
    makensis -V2 -DVERSION="$VERSION" -DNUMERIC="$NUMERIC" -DSOURCE="$WORK/app" -DOUTFILE="$OUT/$NAME-setup.exe" \
      "$ROOT/build/windows/installer.nsi"
    "$ROOT/build/windows/sign.sh" "$OUT/$NAME-setup.exe"
    ;;
  linux-*)
    mkdir "$WORK/$NAME"
    cp -r "$WORK/app/." "$WORK/$NAME/"
    cp "$ROOT/build/sonnuage.desktop" "$WORK/$NAME/"
    tar -C "$WORK" -czf "$OUT/$NAME.tar.gz" "$NAME"
    "$ROOT/build/linux/deb.sh" "$WORK/app" "$VERSION" "$OUT/sonnuage_${VERSION}_amd64.deb"
    ;;
  osx-*)
    APP="$WORK/SonNuage.app/Contents"
    mkdir -p "$APP/MacOS" "$APP/Resources"
    cp -r "$WORK/app/." "$APP/MacOS/"
    cp "$ROOT/build/sonnuage.icns" "$APP/Resources/"
    sed "s/__VERSION__/$VERSION/g" "$ROOT/build/Info.plist" > "$APP/Info.plist"
    chmod +x "$APP/MacOS/SonNuage"
    "$ROOT/build/macos/sign.sh" "$WORK/SonNuage.app"
    # zip garde le bit exécutable : l'appli se lance après décompression.
    (cd "$WORK" && zip -qry "$OUT/$NAME.zip" SonNuage.app)
    # Image disque sans Mac : ISO 9660 + Rock Ridge (droits, lien), que macOS monte comme un .dmg. L'appli et un
    # lien vers Applications, pour l'y glisser.
    mkdir "$WORK/dmg"
    cp -a "$WORK/SonNuage.app" "$WORK/dmg/"
    ln -s /Applications "$WORK/dmg/Applications"
    xorriso -as mkisofs -quiet -V SonNuage -D -l -r -no-pad -dir-mode 0755 -o "$OUT/$NAME.dmg" "$WORK/dmg"
    ;;
  *)
    echo "RID non géré : $RID" >&2
    exit 1
    ;;
esac

rm -rf "$WORK"
echo "$OUT/$NAME"
