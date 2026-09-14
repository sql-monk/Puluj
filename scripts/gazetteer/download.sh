#!/usr/bin/env bash
# Bash twin of download.ps1 (for Linux/macOS/CI).
set -euo pipefail
out="$(cd "$(dirname "$0")/../../data/gazetteer" && pwd)"
gb() { url=$(curl -sf "https://www.geoboundaries.org/api/current/gbOpen/$1/$2/" | grep -o '"gjDownloadURL": *"[^"]*"' | sed 's/.*: *"//;s/"//'); echo "geoBoundaries $1 $2"; curl -sfL "$url" -o "$out/$3"; }
gb UKR ADM0 ukr_adm0.geojson
gb UKR ADM1 ukr_adm1.geojson
gb BLR ADM1 blr_adm1.geojson
gb RUS ADM1 rus_adm1.geojson
gb MDA ADM0 mda_adm0.geojson
tmp=$(mktemp -d)
curl -sfL https://download.geonames.org/export/dump/UA.zip -o "$tmp/UA.zip" && unzip -q -o "$tmp/UA.zip" -d "$tmp/ua" && mv "$tmp/ua/UA.txt" "$out/geonames_UA.txt"
curl -sfL https://download.geonames.org/export/dump/alternatenames/UA.zip -o "$tmp/alt.zip" && unzip -q -o "$tmp/alt.zip" -d "$tmp/alt" && mv "$tmp/alt/UA.txt" "$out/geonames_UA_alternatenames.txt"
curl -sfL https://data.humdata.org/dataset/cod-ab-ukr/resource/681beb86-391b-4a08-8140-ca52e80fcdce/download/ukr_admin_boundaries.geojson.zip -o "$tmp/cod.zip" && unzip -q -o "$tmp/cod.zip" -d "$tmp/cod" && mv "$tmp/cod/ukr_admin2.geojson" "$out/ukr_adm2_cod.geojson" && mv "$tmp/cod/ukr_admin3.geojson" "$out/ukr_adm3_cod.geojson"
rm -rf "$tmp"
ls -la "$out"
