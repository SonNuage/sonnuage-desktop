#!/bin/sh
# Signature et notarisation de SonNuage.app sous Linux (rcodesign) : sign.sh <SonNuage.app>
# Chaque étape seulement si la CI fournit ce qu'il faut, sinon l'appli reste non signée (clic droit → Ouvrir) :
#   MACOS_CERT_P12        variable de type « File » : certificat « Developer ID Application » et sa clé (.p12)
#   MACOS_CERT_PASSWORD   son mot de passe (variable masquée)
#   APPLE_API_KEY_JSON    variable de type « File » : clé d'API App Store Connect au format de rcodesign
#                         (rcodesign encode-app-store-connect-api-key) ; sans elle, signée mais non notarisée
#   RCODESIGN_SHA256      facultatif : somme attendue de l'archive de rcodesign téléchargée
# Aucune valeur secrète n'est affichée : le mot de passe passe par un fichier temporaire.
set -eu

APP="$1"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
RCODESIGN_VERSION=0.27.0

if [ -z "${MACOS_CERT_P12:-}" ]; then
  echo "SonNuage.app non signée (MACOS_CERT_P12 absent)"
  exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
umask 077

# rcodesign (apple-platform-rs) : signature et notarisation Apple sans Mac.
NAME="apple-codesign-$RCODESIGN_VERSION-x86_64-unknown-linux-musl"
curl -fsSL -o "$WORK/rcodesign.tar.gz" \
  "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F$RCODESIGN_VERSION/$NAME.tar.gz"
if [ -n "${RCODESIGN_SHA256:-}" ]; then
  echo "$RCODESIGN_SHA256  $WORK/rcodesign.tar.gz" | sha256sum -c - > /dev/null
fi
tar -xzf "$WORK/rcodesign.tar.gz" -C "$WORK"
RCODESIGN="$WORK/$NAME/rcodesign"

printf '%s' "${MACOS_CERT_PASSWORD:-}" > "$WORK/password"
# Runtime renforcé (exigé par la notarisation) ; .NET a besoin du JIT et charge ses bibliothèques natives extraites.
"$RCODESIGN" sign --p12-file "$MACOS_CERT_P12" --p12-password-file "$WORK/password" \
  --code-signature-flags runtime --entitlements-xml-file "$ROOT/build/macos/entitlements.plist" "$APP"
echo "SonNuage.app signée"

if [ -z "${APPLE_API_KEY_JSON:-}" ]; then
  echo "SonNuage.app non notarisée (APPLE_API_KEY_JSON absent)"
  exit 0
fi
"$RCODESIGN" notary-submit --api-key-file "$APPLE_API_KEY_JSON" --staple "$APP"
echo "SonNuage.app notarisée"
