"""Расчёт карты: границы в плоскости камеры, диапазон высоты, глубина —
по вершинам вычисленной геометрии, целиком в numpy (без цикла Python по
вершинам и без JSON-отпечатков: именно они вешали Blender в 1.0.x)."""
import math

import numpy as np
from mathutils import Vector

GEOMETRY = {'MESH', 'CURVE', 'SURFACE', 'FONT', 'META'}
OVERSCAN = 16          # поля рендера участка, px (обрезаются; убирают шов фильтра)
MAX_SIDE = 65535       # предел PNG-декодера Unity-импорта
EXPORTER_VERSION = '2.0.0'


def basis(camera, depsgraph):
    matrix = camera.evaluated_get(depsgraph).matrix_world.copy()
    rotation = matrix.to_quaternion().to_matrix()
    return matrix.translation.copy(), [rotation.col[i].normalized() for i in range(3)], rotation


def instances(collection, depsgraph):
    allowed = set(collection.all_objects)
    for instance in depsgraph.object_instances:
        obj = instance.object
        parent = instance.parent.original if instance.parent else None
        if obj.type not in GEOMETRY or obj.original.hide_render:
            continue
        if obj.original in allowed or (instance.is_instance and parent in allowed):
            yield obj, instance.matrix_world.copy()


def extents(collection, depsgraph, ref, axes):
    """min/max по осям r, u (плоскость камеры), мировой Z и глубине b."""
    directions = np.array([list(axes[0]), list(axes[1]), [0, 0, 1], list(axes[2])], dtype=np.float64)
    offset = np.array(list(ref), dtype=np.float64)
    low = np.full(4, np.inf)
    high = np.full(4, -np.inf)
    cache = {}
    found = False
    for obj, matrix in instances(collection, depsgraph):
        key = obj.data.name_full if obj.type == 'MESH' and obj.data else None
        coords = cache.get(key) if key else None
        if coords is None:
            converted = obj.type != 'MESH'
            mesh = obj.to_mesh() if converted else obj.data
            try:
                if not mesh or not mesh.vertices:
                    continue
                coords = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.vertices.foreach_get('co', coords)
                coords = coords.reshape(-1, 3)
            finally:
                if converted:
                    obj.to_mesh_clear()
            if key:
                cache[key] = coords
        transform = np.array([list(row) for row in matrix], dtype=np.float64)
        step = 1 << 20
        for start in range(0, len(coords), step):
            world = coords[start:start + step].astype(np.float64) @ transform[:3, :3].T + transform[:3, 3]
            projected = (world - offset) @ directions.T
            projected[:, 2] = world[:, 2]
            if not np.isfinite(projected).all():
                raise ValueError(f'«{obj.original.name}»: нечисловые координаты геометрии')
            low = np.minimum(low, projected.min(axis=0))
            high = np.maximum(high, projected.max(axis=0))
            found = True
    if not found:
        raise ValueError('В коллекции земли нет видимой для рендера геометрии')
    return low, high


def density_from_camera(scene, camera):
    frame = camera.data.view_frame(scene=scene)
    xs, ys = [v.x for v in frame], [v.y for v in frame]
    width = scene.render.resolution_x * scene.render.resolution_percentage / 100
    height = scene.render.resolution_y * scene.render.resolution_percentage / 100
    qx, qy = (max(xs) - min(xs)) / width, (max(ys) - min(ys)) / height
    if abs(qx - qy) > max(qx, qy) * 1e-4:
        raise ValueError('Пиксели кадра камеры не квадратные: проверьте разрешение')
    return 1 / qx


def plan(scene, s, depsgraph):
    camera = s.camera or scene.camera
    if not camera or camera.type != 'CAMERA' or camera.data.type != 'ORTHO':
        raise ValueError('Нужна ортографическая камера (она задаёт ракурс карты)')
    if not s.ground:
        raise ValueError('Выберите коллекцию «Земля»')
    if s.ground.hide_render:
        raise ValueError('Коллекция земли выключена для рендера')
    if any(o.type == 'LIGHT' for o in s.ground.all_objects):
        raise ValueError('Перенесите свет из коллекции земли в отдельную коллекцию')
    if scene.render.engine not in {'CYCLES', 'BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'}:
        raise ValueError('Поддержаны Cycles и EEVEE')
    if s.density <= 0:
        raise ValueError('Плотность должна быть больше нуля')
    ref, axes, rotation = basis(camera, depsgraph)
    r, u, b = axes
    q = 1 / s.density
    low, high = extents(s.ground, depsgraph, ref, axes)
    if s.bounds_object:
        obj = s.bounds_object.evaluated_get(depsgraph)
        corners = [obj.matrix_world @ Vector(p) for p in obj.bound_box]
        xy = [((p - ref).dot(r), (p - ref).dot(u)) for p in corners]
        bounds = [min(p[0] for p in xy), min(p[1] for p in xy), max(p[0] for p in xy), max(p[1] for p in xy)]
    else:
        bounds = [float(low[0]), float(low[1]), float(high[0]), float(high[1])]
    if bounds[2] - bounds[0] <= 0 or bounds[3] - bounds[1] <= 0:
        raise ValueError('Область карты пустая')
    tile = int(s.tile_size)
    nx = max(1, math.ceil((bounds[2] - bounds[0]) / (tile * q) - 1e-9))
    ny = max(1, math.ceil((bounds[3] - bounds[1]) / (tile * q) - 1e-9))
    # Добивка до целых участков — поровну с обеих сторон, по целым пикселям.
    x0 = math.floor(((bounds[0] + bounds[2]) / 2 - nx * tile * q / 2) / q) * q
    y0 = math.floor(((bounds[1] + bounds[3]) / 2 - ny * tile * q / 2) / q) * q
    if x0 + nx * tile * q < bounds[2] - 1e-9:
        nx += 1
    if y0 + ny * tile * q < bounds[3] - 1e-9:
        ny += 1
    width, height = nx * tile, ny * tile
    if max(width, height) > MAX_SIDE:
        raise ValueError(f'Карта {width}×{height} px больше {MAX_SIDE}: уменьшите плотность или область')
    if s.height_auto:
        lo, hi = float(low[2]), float(high[2])
    else:
        lo, hi = s.height_min, s.height_max
    warnings = []
    if s.height_auto and hi - lo < 1e-6:
        lo, hi = lo - 0.0005, hi + 0.0005
        warnings.append('Плоская земля: диапазон высоты расширен на ±0.0005')
    if hi <= lo:
        raise ValueError('Максимум высоты должен быть больше минимума')
    if not s.height_auto and (low[2] < lo or high[2] > hi):
        warnings.append('Земля выходит за ручной диапазон высоты: значения будут обрезаны')
    depth_low, depth_high = float(low[3]), float(high[3])
    margin = max(1.0, (depth_high - depth_low) * 0.05)
    distance = max(0.0, depth_high) + margin
    character = None
    if s.character:
        obj = s.character.evaluated_get(depsgraph)
        ys = [((obj.matrix_world @ Vector(p)) - ref).dot(u) for p in obj.bound_box]
        character = (max(ys) - min(ys)) / q
    return {
        'q': q, 'tile': tile, 'overscan': OVERSCAN, 'nx': nx, 'ny': ny, 'width': width, 'height': height,
        'origin': [x0, y0], 'requested_bounds': bounds,
        'reference': list(ref), 'basis': [list(a) for a in axes], 'rotation': [list(row) for row in rotation],
        'distance': distance, 'clip_end': distance - depth_low + margin,
        'height_min': lo, 'height_max': hi, 'height_geometry': [float(low[2]), float(high[2])],
        'meters_per_unit': scene.unit_settings.scale_length, 'engine': scene.render.engine,
        'character_px': character, 'warnings': warnings,
    }


def summary(c):
    renders = 3 * c['nx'] * c['ny']
    band = c['width'] * c['tile'] * 4 * 2 / 1024 ** 2
    text = [f'Карта {c["width"]} × {c["height"]} px ({c["nx"]} × {c["ny"]} участков по {c["tile"]})',
            f'Рендеров: {renders} (Color, Normal, Height)',
            f'Высота: {c["height_min"]:.4g} … {c["height_max"]:.4g} BU',
            f'Память на полосу ≈ {band:.0f} МиБ']
    if c['character_px']:
        text.append(f'Рост персонажа ≈ {c["character_px"]:.0f} px; карта ≈ {c["height"] / c["character_px"]:.0f} его роста')
    return text + c['warnings']
