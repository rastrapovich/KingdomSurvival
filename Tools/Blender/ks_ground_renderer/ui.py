"""Панель «KS Земля»: настройки, «Проверить», «Экспорт», «Отмена».
Экспорт — modal-оператор: рендер участка идёт штатным неблокирующим
рендером Blender, между участками короткими порциями пишутся PNG — окно
Blender остаётся живым, Esc или «Отмена» останавливают экспорт."""
import time

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty, StringProperty

from . import export, scan

_running = None


def camera_poll(self, obj):
    return obj.type == 'CAMERA'


class KSGroundSettings(bpy.types.PropertyGroup):
    camera: PointerProperty(name='Камера', type=bpy.types.Object, poll=camera_poll,
                            description='Ортографическая камера: её наклон — ракурс карты в игре. Пусто — камера сцены')
    ground: PointerProperty(name='Земля', type=bpy.types.Collection,
                            description='Коллекция с землёй карты (без света и эталона персонажа)')
    character: PointerProperty(name='Эталон персонажа', type=bpy.types.Object,
                               description='Необязательно: модель человека в масштабе — покажет его рост в пикселях')
    bounds_object: PointerProperty(name='Границы', type=bpy.types.Object,
                                   description='Необязательно: объект-рамка; пусто — вся земля')
    density: FloatProperty(name='Пикселей на метр', default=64, min=0.01, soft_max=1024,
                           description='Плотность рендера в плоскости камеры (на Blender Unit)')
    map_id: StringProperty(name='Имя карты', default='Map_01')
    output: StringProperty(name='Папка', subtype='DIR_PATH', default='//KS_Ground/')
    color_mode: EnumProperty(name='Color', items=[
        ('NEUTRAL', 'Ровный свет для обрисовки', 'Мягкий одинаковый свет без падающих теней'),
        ('ARTISTIC', 'Свет сцены', 'Свет и тени сцены останутся в Color')])
    advanced: BoolProperty(name='Дополнительно', default=False)
    tile_size: EnumProperty(name='Участок рендера', default='2048', items=[
        ('1024', '1024', 'Меньше памяти на полосу'), ('2048', '2048', 'Обычно'), ('4096', '4096', 'Меньше рендеров, больше памяти')])
    samples: IntProperty(name='Samples Color', default=32, min=1, max=4096)
    height_auto: BoolProperty(name='Диапазон высоты автоматически', default=True)
    height_min: FloatProperty(name='Мин. высота, BU', default=0)
    height_max: FloatProperty(name='Макс. высота, BU', default=10)
    status: StringProperty(name='Статус', default='Готов', options={'SKIP_SAVE'})
    summary: StringProperty(options={'SKIP_SAVE'})


class Idle:
    @classmethod
    def poll(cls, context):
        return _running is None and not bpy.app.is_job_running('RENDER')


class KS_GROUND_OT_check(Idle, bpy.types.Operator):
    bl_idname = 'ks_ground.check'
    bl_label = 'Проверить'
    bl_description = 'Размер карты, число рендеров, диапазон высоты и рост персонажа'

    def execute(self, context):
        s = context.scene.ks_ground
        try:
            c = scan.plan(context.scene, s, context.evaluated_depsgraph_get())
            s.summary = '\n'.join(scan.summary(c))
            s.status = 'Проверено'
            return {'FINISHED'}
        except Exception as exc:
            s.summary = ''
            s.status = 'Ошибка: ' + str(exc)
            self.report({'ERROR'}, str(exc))
            return {'CANCELLED'}


class KS_GROUND_OT_density(Idle, bpy.types.Operator):
    bl_idname = 'ks_ground.density_from_camera'
    bl_label = 'Взять из кадра камеры'
    bl_description = 'Плотность = как в обычном рендере камеры при её текущем разрешении'

    def execute(self, context):
        s = context.scene.ks_ground
        camera = s.camera or context.scene.camera
        try:
            if not camera or camera.data.type != 'ORTHO':
                raise ValueError('Нужна ортографическая камера')
            s.density = scan.density_from_camera(context.scene, camera)
            s.status = f'Плотность из камеры: {s.density:.3f} px/BU'
            return {'FINISHED'}
        except Exception as exc:
            self.report({'ERROR'}, str(exc))
            return {'CANCELLED'}


class KS_GROUND_OT_export(Idle, bpy.types.Operator):
    bl_idname = 'ks_ground.export'
    bl_label = 'Экспорт'
    bl_description = 'Рендер участками и три больших файла: Color, Normal, Height + manifest'

    def execute(self, context):
        global _running
        s = context.scene.ks_ground
        self.job = None
        try:
            self.job = export.ExportJob(context.scene, s, context)
        except Exception as exc:
            s.status = 'Ошибка: ' + str(exc)
            self.report({'ERROR'}, str(exc))
            return {'CANCELLED'}
        self.settings = s
        self.wm = context.window_manager
        self.waiting = False
        self.done = False
        self.cancelled = False
        self.stop = False
        self.error = None
        self.render_display = context.preferences.view.render_display_type
        # Окно рендера на каждый участок не открываем.
        context.preferences.view.render_display_type = 'NONE'
        self.on_complete = lambda scene, *a: self.rendered(scene, False)
        self.on_cancel = lambda scene, *a: self.rendered(scene, True)
        bpy.app.handlers.render_complete.append(self.on_complete)
        bpy.app.handlers.render_cancel.append(self.on_cancel)
        self.timer = self.wm.event_timer_add(0.1, window=context.window)
        self.wm.progress_begin(0, self.job.total)
        self.wm.modal_handler_add(self)
        _running = self
        s.status = 'Экспорт начат'
        return {'RUNNING_MODAL'}

    def rendered(self, scene, cancelled):
        if self.job and self.job.export_scene and scene == self.job.export_scene.scene:
            self.done = True
            self.cancelled = cancelled

    def redraw(self):
        for window in self.wm.windows:
            for area in window.screen.areas:
                area.tag_redraw()

    def modal(self, context, event):
        if event.type == 'ESC' and event.value == 'PRESS':
            self.stop = True
        if event.type != 'TIMER':
            return {'PASS_THROUGH'}
        try:
            if self.waiting:
                if not self.done or bpy.app.is_job_running('RENDER'):
                    self.job.pump()
                    return {'PASS_THROUGH'}
                self.waiting = False
                if self.cancelled:
                    self.stop = True
                else:
                    self.job.collect()
                    self.wm.progress_update(self.job.index)
            if self.stop:
                return self.finish(context)
            if self.job.index >= self.job.total:
                if not self.job.pump():
                    self.settings.status = 'Запись файлов…'
                    self.redraw()
                    return {'PASS_THROUGH'}
                return self.finish(context)
            scene = self.job.prepare()
            self.settings.status = self.job.progress_text()
            self.done = self.cancelled = False
            self.waiting = True
            result = bpy.ops.render.render('INVOKE_DEFAULT', write_still=True, scene=scene.name)
            if 'CANCELLED' in result:
                raise ValueError('Blender не начал рендер участка')
            self.job.pump()
            self.redraw()
        except Exception as exc:
            self.error = exc
            self.stop = True
            if not bpy.app.is_job_running('RENDER'):
                return self.finish(context)
        return {'PASS_THROUGH'}

    def finish(self, context):
        global _running
        for handlers, callback in ((bpy.app.handlers.render_complete, self.on_complete),
                                   (bpy.app.handlers.render_cancel, self.on_cancel)):
            if callback in handlers:
                handlers.remove(callback)
        self.wm.event_timer_remove(self.timer)
        self.wm.progress_end()
        context.preferences.view.render_display_type = self.render_display
        s = self.settings
        try:
            if self.error or self.stop:
                self.job.close(error=True)
                s.status = ('Ошибка: ' + str(self.error)) if self.error else 'Экспорт остановлен; прежние файлы не изменены'
            else:
                path = self.job.finish()
                s.status = f'Готово за {time.monotonic() - self.job.started:.0f} с: {path.parent}'
                if self.job.backup:
                    s.status += f' · прежний изменённый Color сохранён как {self.job.backup.name}'
        except Exception as exc:
            self.job.close(error=True)
            self.error = exc
            s.status = 'Ошибка записи: ' + str(exc)
        finally:
            _running = None
        self.redraw()
        self.report({'ERROR'} if self.error else {'INFO'}, s.status)
        return {'CANCELLED'} if self.error or self.stop else {'FINISHED'}

    def cancel(self, context):
        if _running is self:
            self.stop = True
            self.finish(context)


class KS_GROUND_OT_cancel(bpy.types.Operator):
    bl_idname = 'ks_ground.cancel'
    bl_label = 'Отмена'

    @classmethod
    def poll(cls, context):
        return _running is not None

    def execute(self, context):
        _running.stop = True
        return {'FINISHED'}


class KS_GROUND_PT_panel(bpy.types.Panel):
    bl_label = 'KS Земля'
    bl_idname = 'KS_GROUND_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'KS Земля'

    def draw(self, context):
        s = context.scene.ks_ground
        layout = self.layout
        col = layout.column()
        col.enabled = _running is None
        col.prop(s, 'camera')
        col.prop(s, 'ground')
        col.prop(s, 'bounds_object')
        col.prop(s, 'character')
        row = col.row(align=True)
        row.prop(s, 'density')
        row.operator('ks_ground.density_from_camera', text='', icon='CAMERA_DATA')
        col.prop(s, 'color_mode')
        col.prop(s, 'map_id')
        col.prop(s, 'output')
        col.prop(s, 'advanced')
        if s.advanced:
            box = col.box()
            box.prop(s, 'tile_size')
            box.prop(s, 'samples')
            box.prop(s, 'height_auto')
            if not s.height_auto:
                box.prop(s, 'height_min')
                box.prop(s, 'height_max')
        col.operator('ks_ground.check')
        if s.summary:
            box = col.box()
            for line in s.summary.split('\n'):
                box.label(text=line)
        layout.operator('ks_ground.export', icon='RENDER_STILL')
        if _running:
            layout.operator('ks_ground.cancel', icon='CANCEL')
        box = layout.box()
        box.label(text=s.status)


CLASSES = (KSGroundSettings, KS_GROUND_OT_check, KS_GROUND_OT_density, KS_GROUND_OT_export, KS_GROUND_OT_cancel, KS_GROUND_PT_panel)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.ks_ground = PointerProperty(type=KSGroundSettings)


def unregister():
    if _running:
        raise RuntimeError('Остановите экспорт перед отключением аддона')
    del bpy.types.Scene.ks_ground
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
