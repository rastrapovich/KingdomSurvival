bl_info = {
    'name': 'KS Ground Renderer',
    'author': 'Kingdom Survival',
    'version': (2, 0, 0),
    'blender': (5, 2, 0),
    'location': '3D-вид > N > KS Земля',
    'description': 'Большая карта земли: рендер участками, на выходе Color, Normal, Height для Базы локаций',
    'category': 'Render',
}

from .ui import register, unregister  # noqa: E402,F401
