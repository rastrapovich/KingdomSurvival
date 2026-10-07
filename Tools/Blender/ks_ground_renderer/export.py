"""Экспорт большой карты: рендер участками во временной сцене, сборка
полос и потоковая запись трёх больших файлов — Color, Normal, Height — и
manifest для «Базы локаций» Kingdom Survival (schema 2).

Порядок: проход за проходом (Color, затем Normal, затем Height), внутри —
полосы участков сверху вниз; полоса готова → её строки уходят в PNG. В
памяти — одна-две полосы, не вся карта. Исходная сцена не меняется."""
import datetime
import hashlib
import json
import os
import shutil
import tempfile
import time
import uuid
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

from . import pngwrite, scan

PASSES = ('Color', 'Normal', 'Height')
DATA_SAMPLES = 16


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def file_sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def data_material(kind, c):
    """Материал-переопределение: Normal — нормаль в базисе картинки, Height — (Z - min) / (max - min)."""
    mat = bpy.data.materials.new('_KS_' + kind)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    geo = nodes.new('ShaderNodeNewGeometry')
    emit = nodes.new('ShaderNodeEmission')
    out = nodes.new('ShaderNodeOutputMaterial')
    links.new(emit.outputs[0], out.inputs['Surface'])
    if kind == 'Height':
        sep = nodes.new('ShaderNodeSeparateXYZ')
        sub = nodes.new('ShaderNodeMath'); sub.operation = 'SUBTRACT'; sub.inputs[1].default_value = c['height_min']
        div = nodes.new('ShaderNodeMath'); div.operation = 'DIVIDE'; div.inputs[1].default_value = c['height_max'] - c['height_min']
        links.new(geo.outputs['Position'], sep.inputs[0])
        links.new(sep.outputs['Z'], sub.inputs[0])
        links.new(sub.outputs[0], div.inputs[0])
        links.new(div.outputs[0], emit.inputs['Color'])
    else:
        combine = nodes.new('ShaderNodeCombineXYZ')
        for i, axis in enumerate(c['basis']):
            dot = nodes.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'; dot.inputs[1].default_value = axis
            links.new(geo.outputs['Normal'], dot.inputs[0])
            links.new(dot.outputs['Value'], combine.inputs[i])
        # Кодировка в шейдере (·0,5 + 0,5): отрицательная эмиссия в рендере не гарантирована.
        scale = nodes.new('ShaderNodeVectorMath'); scale.operation = 'SCALE'; scale.inputs['Scale'].default_value = 0.5
        add = nodes.new('ShaderNodeVectorMath'); add.operation = 'ADD'; add.inputs[1].default_value = (0.5, 0.5, 0.5)
        links.new(combine.outputs[0], scale.inputs[0])
        links.new(scale.outputs[0], add.inputs[0])
        links.new(add.outputs[0], emit.inputs['Color'])
    return mat


class ExportScene:
    """Временная сцена: только земля (и свет сцены для художественного Color)."""

    def __init__(self, source, s, c):
        self.source = source
        self.scene = None
        self.created = []
        self.materials = []
        try:
            scene = source.copy()
            self.scene = scene
            scene.name = '_KS_EXPORT_' + uuid.uuid4().hex[:6]
            scene.animation_data_clear()
            for child in list(scene.collection.children):
                scene.collection.children.unlink(child)
            for obj in list(scene.collection.objects):
                scene.collection.objects.unlink(obj)
            scene.collection.children.link(s.ground)
            while len(scene.view_layers) > 1:
                scene.view_layers.remove(scene.view_layers[-1])
            layer = scene.view_layers[0]
            layer.material_override = None
            for attr in ('use_compositing', 'use_sequencer', 'use_border', 'use_crop_to_border', 'use_motion_blur'):
                if hasattr(scene.render, attr):
                    setattr(scene.render, attr, False)
            if hasattr(scene, 'compositing_node_group'):
                scene.compositing_node_group = None
            scene.render.resolution_percentage = 100
            scene.render.pixel_aspect_x = scene.render.pixel_aspect_y = 1
            scene.render.film_transparent = True
            # BVH и данные сцены переживают смену камеры между участками.
            scene.render.use_persistent_data = True
            side = c['tile'] + 2 * c['overscan']
            scene.render.resolution_x = scene.render.resolution_y = side
            if hasattr(scene, 'cycles'):
                scene.cycles.use_denoising = False
                scene.cycles.use_animated_seed = False
            data = bpy.data.cameras.new('_KS_CAMERA')
            self.created.append(data)
            data.type = 'ORTHO'
            data.ortho_scale = side * c['q']
            data.sensor_fit = 'AUTO'
            data.clip_start = 0.01
            data.clip_end = c['clip_end']
            data.dof.use_dof = False
            camera = bpy.data.objects.new('_KS_CAMERA', data)
            self.created.append(camera)
            scene.collection.objects.link(camera)
            scene.camera = camera
            self.camera = camera
            if s.color_mode == 'NEUTRAL':
                world = bpy.data.worlds.new('_KS_WORLD')
                self.created.append(world)
                world.use_nodes = True
                background = world.node_tree.nodes.get('Background')
                background.inputs['Color'].default_value = (0.55, 0.55, 0.55, 1)
                background.inputs['Strength'].default_value = 0.8
                scene.world = world
                light = bpy.data.lights.new('_KS_FILL', 'SUN')
                self.created.append(light)
                light.energy = 1
                light.use_shadow = False
                sun = bpy.data.objects.new('_KS_FILL', light)
                self.created.append(sun)
                sun.rotation_euler = (0.15, -0.2, 0)
                scene.collection.objects.link(sun)
                self.color_view = {'view_transform': 'Standard', 'look': 'None', 'exposure': 0, 'gamma': 1}
            else:
                for obj in source.objects:
                    if obj.type == 'LIGHT' and not obj.hide_render and obj.name not in scene.collection.objects:
                        scene.collection.objects.link(obj)
                self.color_view = {k: getattr(source.view_settings, k) for k in ('view_transform', 'look', 'exposure', 'gamma')}
            self.materials = {kind: data_material(kind, c) for kind in ('Normal', 'Height')}
            self.color_engine = source.render.engine
            self.color_samples = s.samples
            self.c = c
        except Exception:
            self.close()
            raise

    def setup(self, kind, x, y, path):
        """Камера на участок (x, y) — снизу слева; вывод прохода в path."""
        scene, c = self.scene, self.c
        r, u, b = [Vector(v) for v in c['basis']]
        cx = c['origin'][0] + (x + 0.5) * c['tile'] * c['q']
        cy = c['origin'][1] + (y + 0.5) * c['tile'] * c['q']
        center = Vector(c['reference']) + r * cx + u * cy + b * c['distance']
        matrix = Matrix(c['rotation']).to_4x4()
        matrix.translation = center
        self.camera.matrix_world = matrix
        image = scene.render.image_settings
        image.color_mode = 'RGBA'
        if kind == 'Color':
            scene.render.engine = self.color_engine
            scene.view_layers[0].material_override = None
            image.file_format = 'PNG'
            image.color_depth = '8'
            for key, value in self.color_view.items():
                setattr(scene.view_settings, key, value)
            if self.color_engine == 'CYCLES':
                scene.cycles.samples = self.color_samples
            elif hasattr(scene.eevee, 'taa_render_samples'):
                scene.eevee.taa_render_samples = self.color_samples
        else:
            scene.render.engine = 'CYCLES'
            scene.cycles.samples = DATA_SAMPLES
            scene.view_layers[0].material_override = self.materials[kind]
            image.file_format = 'OPEN_EXR'
            image.color_depth = '32'
            image.exr_codec = 'ZIP'
            try:
                scene.view_settings.view_transform = 'Raw'
            except TypeError:
                scene.view_settings.view_transform = 'Standard'
            scene.view_settings.look = 'None'
            scene.view_settings.exposure = 0
            scene.view_settings.gamma = 1
        scene.render.filepath = str(path)
        return scene

    def close(self):
        if self.scene:
            for window in bpy.context.window_manager.windows:
                if window.scene == self.scene:
                    window.scene = self.source
            bpy.data.scenes.remove(self.scene)
            self.scene = None
        for item in reversed(self.created):
            try:
                if isinstance(item, bpy.types.Object):
                    bpy.data.objects.remove(item, do_unlink=True)
                elif isinstance(item, bpy.types.Camera):
                    bpy.data.cameras.remove(item)
                elif isinstance(item, bpy.types.Light):
                    bpy.data.lights.remove(item)
                elif isinstance(item, bpy.types.World):
                    bpy.data.worlds.remove(item)
            except ReferenceError:
                pass
        self.created = []
        for mat in (self.materials.values() if isinstance(self.materials, dict) else []):
            try:
                bpy.data.materials.remove(mat)
            except ReferenceError:
                pass
        self.materials = {}


def read_render(path, kind, c):
    """Готовый рендер участка → строки сверху вниз без полей.
    Color — uint8 RGBA; Normal — uint8 RGB; Height — uint16 (значение, покрытие)."""
    image = bpy.data.images.load(str(path), check_existing=False)
    try:
        side = c['tile'] + 2 * c['overscan']
        if tuple(image.size) != (side, side):
            raise ValueError(f'Размер рендера {tuple(image.size)} вместо {side}×{side}')
        buf = np.empty(side * side * 4, dtype=np.float32)
        image.pixels.foreach_get(buf)
        premultiplied = image.alpha_mode == 'PREMUL'
    finally:
        bpy.data.images.remove(image)
    p, t = c['overscan'], c['tile']
    # Blender хранит строки снизу вверх; обрезаем поля и переворачиваем один раз.
    rgba = buf.reshape(side, side, 4)[p:p + t, p:p + t][::-1]
    if kind == 'Color':
        return np.rint(np.clip(rgba, 0, 1) * 255).astype(np.uint8)
    alpha = rgba[:, :, 3]
    valid = alpha > 1e-6
    rgb = rgba[:, :, :3].copy()
    if premultiplied:
        rgb[valid] /= alpha[valid, None]
    if kind == 'Normal':
        n = rgb * 2 - 1
        length = np.linalg.norm(n, axis=2)
        good = valid & (length > 1e-6)
        n[good] /= length[good, None]
        n[~good] = (0, 0, 1)
        return np.rint((n * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8)
    value = np.clip(rgb[:, :, 0], 0, 1)
    out = np.empty((t, t, 2), dtype=np.uint16)
    out[:, :, 0] = np.where(valid, np.rint(value * 65535), 0).astype(np.uint16)
    out[:, :, 1] = np.where(valid, np.maximum(1, np.rint(np.clip(alpha, 0, 1) * 65535)), 0).astype(np.uint16)
    return out


FORMATS = {'Color': (8, 'RGBA', True), 'Normal': (8, 'RGB', False), 'Height': (16, 'GA', False)}


class ExportJob:
    """Один экспорт. UI ведёт его modal-оператором (рендер не блокирует
    Blender); тесты — run_blocking(). Шаги: prepare → рендер → collect → pump."""

    def __init__(self, scene, s, context):
        self.source = scene
        self.started = time.monotonic()
        self.export_scene = None
        self.writer = None
        self.closed = False
        depsgraph = context.evaluated_depsgraph_get()
        self.c = scan.plan(scene, s, depsgraph)
        self.map_id = safe_name(s.map_id)
        base = Path(bpy.path.abspath(s.output))
        if not s.output.strip():
            raise ValueError('Выберите папку экспорта')
        if s.output.startswith('//') and not bpy.data.filepath:
            raise ValueError('Сохраните .blend или выберите абсолютную папку экспорта')
        self.folder = base / self.map_id
        self.folder.mkdir(parents=True, exist_ok=True)
        self.temp = Path(tempfile.mkdtemp(prefix='.ks_render_', dir=self.folder))
        self.files = {kind: self.folder / f'{self.map_id}_{kind.lower()}.png' for kind in PASSES}
        self.staged = {kind: self.temp / path.name for kind, path in self.files.items()}
        c = self.c
        self.queue = [(kind, x, y) for kind in PASSES for y in reversed(range(c['ny'])) for x in range(c['nx'])]
        self.index = 0
        self.band = None
        self.pending = []     # полосы, ждущие записи: (kind, numpy-строки)
        self.color_mode = s.color_mode
        try:
            self.export_scene = ExportScene(scene, s, c)
        except Exception:
            self.close(error=True)
            raise

    @property
    def total(self):
        return len(self.queue)

    def progress_text(self):
        if self.index >= self.total:
            return 'Запись файлов…'
        kind, x, y = self.queue[self.index]
        return f'{kind}: участок X{x:03d} Y{y:03d} ({self.index + 1}/{self.total}), {time.monotonic() - self.started:.0f} с'

    def prepare(self):
        kind, x, y = self.queue[self.index]
        self.current = self.temp / ('render.png' if kind == 'Color' else 'render.exr')
        if self.current.exists():
            self.current.unlink()
        return self.export_scene.setup(kind, x, y, self.current)

    def collect(self):
        """Рендер участка готов: в полосу; полоса собрана — в очередь записи."""
        kind, x, y = self.queue[self.index]
        c = self.c
        if not self.current.exists():
            raise ValueError('Blender не сохранил рендер участка')
        tile = read_render(self.current, kind, c)
        if self.band is None:
            self.band = np.zeros((c['tile'], c['width'], tile.shape[2]), dtype=tile.dtype)
        self.band[:, x * c['tile']:(x + 1) * c['tile']] = tile
        if x == c['nx'] - 1:
            self.pending.append((kind, self.band))
            self.band = None
        self.index += 1

    def pump(self, budget=0.05):
        """Записать часть ожидающих строк; True — очередь записи пуста."""
        deadline = time.monotonic() + budget
        c = self.c
        while self.pending and time.monotonic() < deadline:
            kind, band = self.pending[0]
            if self.writer is None or self.writer_kind != kind:
                bits, mode, srgb = FORMATS[kind]
                self.writer = pngwrite.PngStream(self.staged[kind], c['width'], c['height'], bits, mode, srgb)
                self.writer_kind = kind
            rows = 64
            self.writer.write_rows(band[:rows])
            if len(band) > rows:
                self.pending[0] = (kind, band[rows:])
            else:
                self.pending.pop(0)
            if self.writer.rows == c['height']:
                self.writer.finish()
                self.writer = None
        return not self.pending

    def run_blocking(self):
        """Для фоновых тестов Blender: рендер синхронно."""
        try:
            while self.index < self.total:
                scene = self.prepare()
                bpy.ops.render.render(write_still=True, scene=scene.name)
                self.collect()
                self.pump(budget=1e9)
            return self.finish()
        except Exception:
            self.close(error=True)
            raise

    def finish(self):
        while not self.pump(budget=1e9):
            pass
        c = self.c
        backup = None
        manifest_path = self.folder / f'{self.map_id}_manifest.json'
        # Обрисованный в той же папке Color не затирается: переименовывается.
        old = None
        if manifest_path.exists():
            try:
                old = json.loads(manifest_path.read_text(encoding='utf8'))
            except (OSError, ValueError):
                old = None
        color = self.files['Color']
        if color.exists():
            recorded = (old or {}).get('files', {}).get('Color', {}).get('sha256')
            if recorded is None or file_sha256(color) != recorded:
                stamp = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
                backup = color.with_name(f'{self.map_id}_color_обрисовка_{stamp}.png')
                os.replace(color, backup)
        for kind in PASSES:
            os.replace(self.staged[kind], self.files[kind])
        manifest = {
            'format': 'ks_ground_map', 'schema_version': 2, 'exporter_version': scan.EXPORTER_VERSION,
            'blender_version': bpy.app.version_string, 'map_id': self.map_id, 'revision_id': uuid.uuid4().hex,
            'created_utc': utc(), 'status': 'complete', 'elapsed_seconds': round(time.monotonic() - self.started, 1),
            'image': {'width': c['width'], 'height': c['height'], 'rows': 'top_to_bottom',
                      'tile': [c['tile'], c['tile']], 'grid': [c['nx'], c['ny']], 'tile_indices': 'bottom_left, X right, Y up'},
            'projection': {'q': c['q'], 'origin': c['origin'], 'requested_bounds': c['requested_bounds'],
                           'reference': c['reference'], 'basis': c['basis']},
            'units': {'meters_per_blender_unit': c['meters_per_unit'], 'pixels_per_blender_unit': 1 / c['q']},
            'character': {'height_px': c['character_px']} if c['character_px'] else None,
            'files': {
                'Color': {'path': self.files['Color'].name, 'sha256': file_sha256(self.files['Color']), 'bit_depth': 8,
                          'channels': 'RGBA', 'color_space': 'sRGB', 'profile': self.color_mode},
                'Normal': {'path': self.files['Normal'].name, 'sha256': file_sha256(self.files['Normal']), 'bit_depth': 8,
                           'channels': 'RGB', 'space': 'sprite_projection', 'invert_green': False,
                           'encoding': 'rgb = normal_in_camera_basis * 0.5 + 0.5 (R вправо, G вверх, B к зрителю)'},
                'Height': {'path': self.files['Height'].name, 'sha256': file_sha256(self.files['Height']), 'bit_depth': 16,
                           'channels': 'GA', 'min': c['height_min'], 'max': c['height_max'], 'source': 'world_position_z',
                           'units': 'Blender Unit', 'alpha': 'coverage; alpha=0 invalid'},
            },
            'warnings': c['warnings'],
        }
        tmp = manifest_path.with_name('.' + manifest_path.name + '.part')
        tmp.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf8')
        os.replace(tmp, manifest_path)
        self.close()
        self.manifest_path = manifest_path
        self.backup = backup
        return manifest_path

    def close(self, error=False):
        if self.closed:
            return
        self.closed = True
        if self.writer is not None:
            self.writer.abort()
            self.writer = None
        self.pending = []
        self.band = None
        if self.export_scene:
            self.export_scene.close()
            self.export_scene = None
        if self.temp and self.temp.exists():
            shutil.rmtree(self.temp, ignore_errors=True)


def safe_name(name):
    import re
    name = (name or '').strip()
    if not name or re.search(r'[<>:"/\\|?*\x00-\x1f]', name) or name.endswith(('.', ' ')):
        raise ValueError('Недопустимое имя карты (без <>:"/\\|?* и точки в конце)')
    return name
