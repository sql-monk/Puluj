<#
.SYNOPSIS
  Downloads open geodata for the Puluj gazetteer into data/gazetteer/.
  - geoBoundaries (ODbL/CC-BY, OSM-derived): ADM1 polygons for UKR, BLR, RUS; ADM0 for MDA
  - GeoNames (CC-BY): populated places of Ukraine with Ukrainian alternate names
  Files are gitignored; run once per environment (or bake into the Docker image).
#>
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot "..\..\data\gazetteer"
New-Item -ItemType Directory -Force $out | Out-Null

function Get-GeoBoundaries([string]$iso, [string]$adm, [string]$file) {
    $meta = Invoke-RestMethod "https://www.geoboundaries.org/api/current/gbOpen/$iso/$adm/"
    Write-Host "geoBoundaries $iso $adm ($($meta.admUnitCount) units, $($meta.boundaryYearRepresented))"
    Invoke-WebRequest -Uri $meta.gjDownloadURL -OutFile (Join-Path $out $file)
}

Get-GeoBoundaries UKR ADM0 "ukr_adm0.geojson"
Get-GeoBoundaries UKR ADM1 "ukr_adm1.geojson"
Get-GeoBoundaries BLR ADM1 "blr_adm1.geojson"
Get-GeoBoundaries RUS ADM1 "rus_adm1.geojson"
Get-GeoBoundaries MDA ADM0 "mda_adm0.geojson"

Write-Host "GeoNames UA"
$tmp = Join-Path $out "_tmp"
New-Item -ItemType Directory -Force $tmp | Out-Null
Invoke-WebRequest "https://download.geonames.org/export/dump/UA.zip" -OutFile "$tmp/UA.zip"
Expand-Archive "$tmp/UA.zip" -DestinationPath "$tmp/ua" -Force
Move-Item "$tmp/ua/UA.txt" (Join-Path $out "geonames_UA.txt") -Force
Invoke-WebRequest "https://download.geonames.org/export/dump/alternatenames/UA.zip" -OutFile "$tmp/UA_alt.zip"
Expand-Archive "$tmp/UA_alt.zip" -DestinationPath "$tmp/ua_alt" -Force
Move-Item "$tmp/ua_alt/UA.txt" (Join-Path $out "geonames_UA_alternatenames.txt") -Force
Remove-Item $tmp -Recurse -Force

Write-Host "Done. Files in $out"
Get-ChildItem $out | Select-Object Name, Length
