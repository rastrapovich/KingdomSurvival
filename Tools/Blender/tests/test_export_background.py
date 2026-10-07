"""Фоновый тест KS Ground Renderer 2:
blender -b --factory-startup --python Tools/Blender/tests/test_export_background.py -- <папка вывода> [CYCLES|BLENDER_EEVEE]

Строит холмистую землю, эталон человека и наклонённую ортокамеру,
экспортирует карту несколькими участками и проверяет: размеры, высоту
против ray_cast по геометрии, нормали против нормали геометрии, швы."""
import math
import sys
import time
from pathlib import Path
from types import SimpleNamespace

import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import ks_ground_renderer  # noqa: E402
from ks_ground_renderer import export, scan  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from scene_builder import build_scene  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = Path(args[0] if args else ROOT / 'tests' / '_out')
ENGINE = args[1] if len(args) > 1 else 'CYCLES'
DENSITY = float(args[2]) if len(args) > 2 else 24.0
TILE = args[3] if len(args) > 3 else '256'
VERIFY = (args[4] if len(args) > 4 else '1') == '1'  # 0 — только экспорт и manifest (большие карты)




def load(path, data=True):
    image = bpy.data.images.load(str(path), check_existing=False)
    if data:
        # Числа, не цвет: без перевода sRGB → линейный при чтении.
        image.colorspace_settings.name = 'Non-Color'
    w, h = image.size
    buf = np.empty(w * h * image.channels, dtype=np.float32)
    image.pixels.foreach_get(buf)
    channels = image.channels
    bpy.data.images.remove(image)
    return buf.reshape(h, w, channels)[::-1]  # строки сверху вниз


def main():
    scene, ground, person, cam, terrain = build_scene(ENGINE)
    OUT.mkdir(parents=True, exist_ok=True)
    s = SimpleNamespace(camera=cam, ground=ground, character=person, bounds_object=None, density=DENSITY,
                        map_id='KS_Test', output=str(OUT), color_mode='NEUTRAL', tile_size=TILE, samples=8,
                        height_auto=True, height_min=0, height_max=1)
    plan = scan.plan(scene, s, bpy.context.evaluated_depsgraph_get())
    print('PLAN', scan.summary(plan))
    started = time.monotonic()
    job = export.ExportJob(scene, s, bpy.context)
    manifest = job.run_blocking()
    elapsed = time.monotonic() - started
    c = job.c
    import json
    m = json.loads(Path(manifest).read_text(encoding='utf8'))
    assert m['schema_version'] == 2 and m['status'] == 'complete'
    assert m['image']['width'] == c['nx'] * int(TILE) and m['image']['height'] == c['ny'] * int(TILE)
    assert c['nx'] >= 2 and c['ny'] >= 2, 'нужно несколько участков'
    assert not any(o.name.startswith('_KS_') for o in bpy.data.objects), 'временные объекты удалены'
    assert len(bpy.data.scenes) == 1, 'временная сцена удалена'
    folder = Path(manifest).parent
    if not VERIFY:
        sizes = {p.name: round(p.stat().st_size / 1048576, 1) for p in folder.glob('*.png')}
        print('BIG', c['width'], 'x', c['height'], 'tiles', c['nx'], 'x', c['ny'], 'elapsed', round(elapsed, 1), 's', 'MiB', sizes)
        print('KS_TEST_OK')
        return
    color = load(folder / m['files']['Color']['path'], data=False)
    normal = load(folder / m['files']['Normal']['path'])
    height = load(folder / m['files']['Height']['path'])
    H, W = c['height'], c['width']
    assert color.shape[:2] == (H, W) and normal.shape[:2] == (H, W) and height.shape[:2] == (H, W)
    lo, hi = c['height_min'], c['height_max']

    # Высота и нормаль против геометрии: луч из точки плоскости вдоль -b.
    ref = Vector(c['reference'])
    r, u, b = [Vector(v) for v in c['basis']]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    rng = np.random.default_rng(1)
    errors, normal_errors, checked = [], [], 0
    step = (hi - lo) / 65535
    for _ in range(400):
        px, py = int(rng.integers(2, W - 2)), int(rng.integers(2, H - 2))
        value, alpha = height[py, px, 0], height[py, px, -1]
        if alpha < 0.999:
            continue
        vx = c['origin'][0] + (px + 0.5) * c['q']
        vy = c['origin'][1] + (H - py - 0.5) * c['q']
        origin = ref + r * vx + u * vy + b * c['distance']
        hit, location, hit_normal, *_ = scene.ray_cast(depsgraph, origin, -b)
        if not hit:
            continue
        decoded = lo + value * (hi - lo)
        errors.append(abs(decoded - location.z))
        n = normal[py, px, :3] * 2 - 1
        expected = Vector((hit_normal.dot(r), hit_normal.dot(u), hit_normal.dot(b)))
        normal_errors.append(math.degrees(Vector(n).normalized().angle(expected)))
        checked += 1
    assert checked > 100, f'мало точек на поверхности: {checked}'
    slope_tolerance = 2.5 * c['q']  # пиксель — среднее по площади; на склоне ±полпикселя
    print('HEIGHT error max', max(errors), 'median', float(np.median(errors)), 'step', step, 'tolerance', slope_tolerance)
    print('NORMAL angle max', max(normal_errors), 'median', float(np.median(normal_errors)))
    assert float(np.median(errors)) < slope_tolerance, 'высота совпадает с геометрией'
    assert float(np.median(normal_errors)) < 4, 'нормали в базисе картинки'

    # Швы: на границах участков нет скачка сильнее обычного шага.
    seams = [k * int(TILE) for k in range(1, c['nx'])]
    covered = height[:, :, -1] > 0.999
    for name, img in (('Color', color[:, :, :3].mean(axis=2)), ('Height', height[:, :, 0])):
        diff = np.abs(np.diff(img, axis=1))
        ok = covered[:, 1:] & covered[:, :-1]
        normal_step = float(np.percentile(diff[ok], 99))
        for x in seams:
            seam = diff[:, x - 1][ok[:, x - 1]]
            if len(seam):
                print('SEAM', name, x, float(seam.max()), 'p99', normal_step)
                assert float(np.percentile(seam, 99)) <= normal_step * 1.5 + 1e-4, f'шов {name} на x={x}'
    print('CHARACTER px', c['character_px'], 'map', W, 'x', H, 'elapsed', round(elapsed, 1), 's')
    print('KS_TEST_OK')


try:
    main()
except Exception:
    import traceback
    traceback.print_exc()
    print('KS_TEST_FAILED')
    sys.exit(1)
