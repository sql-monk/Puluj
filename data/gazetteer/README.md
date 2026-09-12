# Gazetteer data

Run `scripts/gazetteer/download.ps1` (Windows) or `scripts/gazetteer/download.sh` to fetch:

| File | Source | Content |
|---|---|---|
| `ukr_adm1.geojson` | geoBoundaries UKR ADM1 | 27 oblasts + Kyiv + Sevastopol + Crimea (polygons) |
| `blr_adm1.geojson` | geoBoundaries BLR ADM1 | Belarus regions |
| `rus_adm1.geojson` | geoBoundaries RUS ADM1 | only border regions are imported (see `regions.json`) |
| `mda_adm0.geojson` | geoBoundaries MDA ADM0 | Moldova outline |
| `geonames_UA.txt` | GeoNames UA dump | populated places (imported when population >= 500 or admin seat) |
| `geonames_UA_alternatenames.txt` | GeoNames | Ukrainian/Russian names for the places above |

`regions.json` (committed) maps ISO codes to Ukrainian names and the stems used by the parser, and defines
extra named areas (Black Sea, Sea of Azov) as coarse polygons. The importer (`GazetteerSeeder`) assigns
every settlement to the region polygon that contains it.

Raion (ADM2, 2020 reform) polygons are not part of MVP; when needed, export `admin_level=6` relations from
OSM via Overpass to `ukr_adm2_osm.geojson` and the importer will pick them up.

Licenses: geoBoundaries data is ODbL/CC-BY (OSM-derived), GeoNames is CC-BY 4.0. Attribution belongs in the UI "About" panel.
