#!/bin/sh
# Paquet de l'appli de bureau pour un système : package.sh <rid> <version> <dossier de sortie>
#   win-x64              SonNuage-<version>-win-x64.zip     (SonNuage.exe autonome)
#   linux-x64            SonNuage-<version>-linux-x64.tar.gz (exécutable, icône, lanceur .desktop)
#   osx-arm64, osx-x64   SonNuage-<version>-<rid>.zip       (SonNuage.app, non signée)
# Publication autonome (runtime .NET inclus) en un seul fichier compressé, à côté du dossier wwwroot.
# Arguments de dotnet publish en plus après le dossier de sortie (par exemple -p:SonNuageRepo=...).
set -eu

RID="$1"
VERSION="$2"
OUT="$3"
shift 3
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"
NAME="SonNuage-$VERSION-$RID"

dotnet publish "$ROOT/src/SonNuage.Desktop/SonNuage.Desktop.csproj" -c Release -r "$RID" --self-contained \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -p:Version="$VERSION" -o "$WORK/app" "$@"
mkdir -p "$OUT"

case "$RID" in
  win-*)
    (cd "$WORK/app" && zip -qr "$OUT/$NAME.zip" .)
    ;;
  linux-*)
    mkdir "$WORK/$NAME"
    cp -r "$WORK/app/." "$WORK/$NAME/"
    cp "$ROOT/build/sonnuage.desktop" "$WORK/$NAME/"
    tar -C "$WORK" -czf "$OUT/$NAME.tar.gz" "$NAME"
    ;;
  osx-*)
    APP="$WORK/SonNuage.app/Contents"
    mkdir -p "$APP/MacOS" "$APP/Resources"
    cp -r "$WORK/app/." "$APP/MacOS/"
    cp "$ROOT/build/sonnuage.icns" "$APP/Resources/"
    sed "s/__VERSION__/$VERSION/g" "$ROOT/build/Info.plist" > "$APP/Info.plist"
    chmod +x "$APP/MacOS/SonNuage"
    # zip garde le bit exécutable : l'appli se lance après décompression.
    (cd "$WORK" && zip -qry "$OUT/$NAME.zip" SonNuage.app)
    ;;
  *)
    echo "RID non géré : $RID" >&2
    exit 1
    ;;
esac

rm -rf "$WORK"
echo "$OUT/$NAME"
