"""Проверка «Blender не зависает при экспорте» в настоящем окне:
blender --factory-startup --python Tools/Blender/tests/test_gui_responsive.py -- <папка вывода> <файл отчёта>

Регистрирует аддон, строит сцену, жмёт «Экспорт» (тот же modal-оператор,
что кнопка) и параллельно таймером каждые 50 мс отмечает отклик главного
потока. Самая длинная пауза между отметками — сколько окно не отвечало.
Отчёт пишется в файл, Blender закрывается сам."""
import json
import sys
import time
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import ks_ground_renderer  # noqa: E402
from ks_ground_renderer import ui  # noqa: E402
from scene_builder import build_scene  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:]
OUT, REPORT = args[0], Path(args[1])
state = {'last': None, 'gaps': [], 'started': None, 'phase': 'build', 'deadline': time.monotonic() + 900}


def context_override():
    window = bpy.context.window_manager.windows[0]
    area = next(a for a in window.screen.areas if a.type == 'VIEW_3D')
    region = next(r for r in area.regions if r.type == 'WINDOW')
    return bpy.context.temp_override(window=window, area=area, region=region)


def heartbeat():
    now = time.monotonic()
    if state['phase'] == 'export' and state['last'] is not None:
        state['gaps'].append(now - state['last'])
    state['last'] = now
    return 0.05


def driver():
    try:
        if state['phase'] == 'build':
            with context_override():
                ks_ground_renderer.register()
                scene, ground, person, cam, terrain = build_scene('CYCLES', reset=False)
                s = scene.ks_ground
                s.camera, s.ground, s.character = cam, ground, person
                s.density = 100.0
                s.tile_size = '1024'
                s.map_id = 'KS_Gui'
                s.output = OUT
                s.samples = 16
                state['phase'] = 'export'
                state['started'] = time.monotonic()
                result = bpy.ops.ks_ground.export()
                state['result'] = list(result)
            return 0.5
        if state['phase'] == 'export':
            if ui._running is None and time.monotonic() - state['started'] > 1:
                state['phase'] = 'done'
                gaps = sorted(state['gaps'])
                report = {
                    'status': bpy.context.scene.ks_ground.status,
                    'seconds': round(time.monotonic() - state['started'], 1),
                    'beats': len(gaps),
                    'max_gap': round(gaps[-1], 3) if gaps else None,
                    'p99_gap': round(gaps[int(len(gaps) * 0.99)], 3) if gaps else None,
                    'median_gap': round(gaps[len(gaps) // 2], 3) if gaps else None,
                    'files': sorted(p.name for p in (Path(OUT) / 'KS_Gui').glob('*')),
                }
                REPORT.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
                bpy.ops.wm.quit_blender()
                return None
            if time.monotonic() > state['deadline']:
                REPORT.write_text(json.dumps({'status': 'timeout'}), encoding='utf8')
                bpy.ops.wm.quit_blender()
                return None
            return 0.5
    except Exception as exc:
        import traceback
        REPORT.write_text(json.dumps({'status': 'error', 'error': traceback.format_exc()}, ensure_ascii=False), encoding='utf8')
        bpy.ops.wm.quit_blender()
        return None
    return None


bpy.app.timers.register(heartbeat, first_interval=0.05, persistent=True)
bpy.app.timers.register(driver, first_interval=2.0, persistent=True)
