"""Downloads the 10 administrative districts of Kyiv (OSM relation 421866 subareas, ODbL) into
data/gazetteer/kyiv_districts.geojson. Needs only the standard library; polygons are stitched from the relation ways.
Usage: python scripts/gazetteer/download-kyiv.py"""
import json, os, sys, urllib.parse, urllib.request

OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'data', 'gazetteer', 'kyiv_districts.geojson')
QUERY = '''[out:json][timeout:180];
rel(421866); >>;
rel(r._:"subarea")["boundary"="administrative"];
out geom;'''


def fetch(query):
    req = urllib.request.Request('https://overpass-api.de/api/interpreter',
                                 data=urllib.parse.urlencode({'data': query}).encode(),
                                 headers={'User-Agent': 'puluj-gazetteer/0.1', 'Accept': 'application/json'})
    with urllib.request.urlopen(req, timeout=300) as r:
        return json.load(r)


def stitch(ways):
    """Joins way segments (lists of [lon, lat]) into closed rings by matching end points."""
    segments = [list(w) for w in ways if len(w) >= 2]
    rings = []
    while segments:
        ring = segments.pop(0)
        while ring[0] != ring[-1]:
            for i, seg in enumerate(segments):
                if seg[0] == ring[-1]:
                    ring.extend(seg[1:]); segments.pop(i); break
                if seg[-1] == ring[-1]:
                    ring.extend(reversed(seg[:-1])); segments.pop(i); break
            else:
                ring.append(ring[0])  # unclosed data: close it rather than drop the district
                break
        rings.append(ring)
    return rings


def area(ring):
    return abs(sum(ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1] for i in range(len(ring) - 1))) / 2


def main():
    data = fetch(QUERY)
    features = []
    for rel in data['elements']:
        if rel['type'] != 'relation':
            continue
        tags = rel.get('tags', {})
        outer = [[[p['lon'], p['lat']] for p in m['geometry']] for m in rel.get('members', [])
                 if m['type'] == 'way' and m.get('role', 'outer') in ('outer', '') and 'geometry' in m]
        rings = sorted(stitch(outer), key=area, reverse=True)
        if not rings:
            print('no outer ring for', tags.get('name'), file=sys.stderr)
            continue
        geometry = {'type': 'Polygon', 'coordinates': [rings[0]]} if len(rings) == 1 else \
            {'type': 'MultiPolygon', 'coordinates': [[r] for r in rings]}
        features.append({'type': 'Feature', 'geometry': geometry,
                         'properties': {'osmId': rel['id'], 'name': tags.get('name'), 'nameEn': tags.get('name:en'),
                                        'wikidata': tags.get('wikidata')}})
        print(tags.get('name'), len(rings), 'ring(s),', sum(len(r) for r in rings), 'points')
    features.sort(key=lambda f: f['properties']['name'])
    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump({'type': 'FeatureCollection', 'features': features}, f, ensure_ascii=False)
    print('wrote', os.path.abspath(OUT), len(features), 'districts')


if __name__ == '__main__':
    main()
