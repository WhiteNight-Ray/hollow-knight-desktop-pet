"""Optional asset preparation; not needed to build or run the desktop pet.

Usage: python extract-ability-assets.py knight-original.zip knight.bundle src/Assets
Requires Pillow and UnityPy. Sources and hashes: ../素材与参数来源.md.
Only the selected original PNGs and spell atlas regions are exported.
"""
from pathlib import Path
from zipfile import ZipFile
import argparse
import json
import UnityPy
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('sprite_zip', type=Path)
parser.add_argument('knight_bundle', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
mapping = {'shadow_dash': 'shadow_dash', 'quake': 'quake',
           'quake_cast_level_02_shadow': 'dark_dive', '_ effects/shadow_dash_trail': 'shadow_trail'}
with ZipFile(args.sprite_zip) as archive:
    for source, target in mapping.items():
        folder = args.output / target
        folder.mkdir(exist_ok=True)
        for name in sorted(n for n in archive.namelist()
                           if n.startswith('the_knight/' + source + '/') and n.endswith('.png')):
            (folder / Path(name).name).write_bytes(archive.read(name))

environment = UnityPy.load(str(args.knight_bundle))
objects = {obj.path_id: obj for obj in environment.objects}
collection = objects[8982384931531801455].read_typetree()
atlas = objects[collection['textures'][0]['m_PathID']].read().image.convert('RGBA')
prefixes = {'quake_lvl2_blast_02': 'dark_blast', 'quake_lvl_02_blast_01': 'dark_wave',
            'quake_lvl2_dash_effect': 'dark_streak', 'quake_impact_02': 'dark_impact'}
manifest = {}
for prefix, clip in prefixes.items():
    folder = args.output / clip
    folder.mkdir(exist_ok=True)
    frames = []
    definitions = sorted((d for d in collection['spriteDefinitions'] if d['name'].startswith(prefix)),
                         key=lambda definition: definition['name'])
    for definition in definitions:
        uv = definition['uvs']
        width, height = atlas.size
        x0 = round(min(point['x'] for point in uv) * width)
        x1 = round(max(point['x'] for point in uv) * width)
        y0 = round((1 - max(point['y'] for point in uv)) * height)
        y1 = round((1 - min(point['y'] for point in uv)) * height)
        image = atlas.crop((x0, y0, x1, y1))
        if abs(uv[1]['y'] - uv[0]['y']) > abs(uv[1]['x'] - uv[0]['x']):
            rotation = Image.Transpose.ROTATE_90 if uv[1]['y'] > uv[0]['y'] else Image.Transpose.ROTATE_270
            image = image.transpose(rotation)
        positions = definition['positions']
        unit = definition['texelSize']['x']
        left = round(min(point['x'] for point in positions) / unit)
        top = round(-max(point['y'] for point in positions) / unit)
        filename = definition['name'] + '.png'
        image.save(folder / filename)
        frames.append({'file': filename, 'left': left, 'top': top, 'width': image.width, 'height': image.height})
    manifest[clip] = frames
(args.output / 'ability-effects.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print('Exported', sum(len(frames) for frames in manifest.values()), 'registered spell effect frames.')
