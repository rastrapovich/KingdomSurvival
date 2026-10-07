"""Тестовая сцена: холмистая земля 40×40 м, эталон человека 1,8 м, ортокамера под 55°."""
import math

import bpy
import numpy as np
from mathutils import Vector


def surface(x, y):
    return 1.2 * math.sin(x * 0.35) * math.cos(y * 0.28) + 0.04 * x


def build_scene(ENGINE="CYCLES", reset=True):
    if reset:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = ENGINE
    ground = bpy.data.collections.new('GROUND')
    scene.collection.children.link(ground)
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=160, y_subdivisions=160, size=40)
    terrain = bpy.context.active_object
    terrain.name = 'terrain'
    for collection in terrain.users_collection:
        collection.objects.unlink(terrain)
    ground.objects.link(terrain)
    co = np.empty(len(terrain.data.vertices) * 3, dtype=np.float32)
    terrain.data.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    co[:, 2] = [surface(x, y) for x, y in co[:, :2]]
    terrain.data.vertices.foreach_set('co', co.ravel())
    terrain.data.update()
    terrain.data.shade_smooth()
    mat = bpy.data.materials.new('ground')
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    checker = mat.node_tree.nodes.new('ShaderNodeTexChecker')
    checker.inputs['Scale'].default_value = 20
    mat.node_tree.links.new(checker.outputs['Color'], bsdf.inputs['Base Color'])
    terrain.data.materials.append(mat)
    # Эталон человека 1,8 м — вне коллекции земли.
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0.9))
    person = bpy.context.active_object
    person.scale = (0.4, 0.3, 1.8)
    person.hide_render = True
    cam_data = bpy.data.cameras.new('cam')
    cam_data.type = 'ORTHO'
    cam = bpy.data.objects.new('cam', cam_data)
    scene.collection.objects.link(cam)
    cam.rotation_euler = (math.radians(55), 0, math.radians(20))
    cam.location = Vector((0, 0, 0)) + cam.matrix_world.to_3x3() @ Vector((0, 0, 30))
    scene.camera = cam
    return scene, ground, person, cam, terrain
