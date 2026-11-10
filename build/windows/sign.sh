#!/bin/sh
# Signature Authenticode d'un exécutable Windows (osslsigncode, sous Linux) : sign.sh <fichier.exe>
# Seulement si la CI fournit le certificat, sinon le fichier reste non signé :
#   WINDOWS_CERT_PFX       variable de type « File » : le certificat .pfx (PKCS#12)
#   WINDOWS_CERT_PASSWORD  son mot de passe (variable masquée)
#   WINDOWS_TIMESTAMP_URL  serveur d'horodatage, http://timestamp.digicert.com par défaut
# Aucune valeur secrète n'est affichée : le mot de passe passe par un fichier temporaire.
set -eu

FILE="$1"
if [ -z "${WINDOWS_CERT_PFX:-}" ]; then
  echo "Non signé (WINDOWS_CERT_PFX absent) : $(basename "$FILE")"
  exit 0
fi

umask 077
PASS="$(mktemp)"
trap 'rm -f "$PASS" "$FILE.signed"' EXIT
printf '%s' "${WINDOWS_CERT_PASSWORD:-}" > "$PASS"
osslsigncode sign -pkcs12 "$WINDOWS_CERT_PFX" -readpass "$PASS" -h sha256 \
  -n "SonNuage" -t "${WINDOWS_TIMESTAMP_URL:-http://timestamp.digicert.com}" \
  -in "$FILE" -out "$FILE.signed" > /dev/null
mv "$FILE.signed" "$FILE"
osslsigncode verify -in "$FILE" > /dev/null 2>&1 || echo "Attention : signature non vérifiable (certificat auto-signé ?)"
echo "Signé : $(basename "$FILE")"
