"""Headless export of Art/Source/SF.blend into the Unity project.

Run from the repo root:
  /Applications/Blender.app/Contents/MacOS/Blender -b Art/Source/SF.blend -P Tools/export_fbx.py
  ... -P Tools/export_fbx.py -- --courier-only   # just re-rig + re-export Courier.fbx (no texture bake)

Writes (never touches the .blend on disk):
  RunAcross/Assets/_Project/Art/Models/Environment.fbx  - floor, water, arches, building, neon bar, camera, lights
  RunAcross/Assets/_Project/Art/Models/Courier.fbx      - the character, feet at origin
  RunAcross/Assets/_Project/Art/Textures/*.png|jpg      - unpacked character textures + baked floor maps
"""
import os
import sys

import bpy
import numpy as np

sys.dont_write_bytecode = True  # no __pycache__ in Tools/
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig_courier  # noqa: E402

COURIER_ONLY = "--courier-only" in (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
ART = os.path.join(REPO, "RunAcross", "Assets", "_Project", "Art")
MODELS = os.path.join(ART, "Models")
TEX = os.path.join(ART, "Textures")
os.makedirs(MODELS, exist_ok=True)
os.makedirs(TEX, exist_ok=True)

BAKE_RES = 2048
ENV_MESHES = ["Plane", "Plane.001", "Cylinder", "Cylinder.001",
              "Object_1", "Object_5", "Object_6", "Object_7", "Object_8", "Plane.002"]
RENAME = {"Plane": "Floor_Rock", "Plane.001": "Floor_Water", "Cylinder": "Arch_Large",
          "Cylinder.001": "Arch_Small", "Object_1": "Building_Body", "Object_5": "Building_Decor",
          "Object_6": "Building_Panel", "Object_7": "Building_GlowA", "Object_8": "Building_GlowB",
          "Plane.002": "NeonBar", "Camera": "LookDevCamera", "Area": "Sun_Blue", "Area.002": "Light_Red"}
CHAR_MESHES = ["joinLegUpL_002__0", "joinLegUpL_002__0_1"]

scene = bpy.context.scene
deps = bpy.context.evaluated_depsgraph_get()


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def detach(obj):
    """Clear parent but keep the world transform."""
    mw = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = mw


if not COURIER_ONLY:
    # ---------- unpack character textures ----------
    for img in bpy.data.images:
        if img.packed_file is None or img.name.startswith("08_-_Default"):  # 08_* belongs to an unused material
            continue
        name = img.name.replace(" ", "_")
        img.filepath_raw = os.path.join(TEX, name)
        img.save()
        print("TEX", name)

    # ---------- pack glTF metal + rough maps into Unity's MetallicSmoothness (R = metal, A = 1 - rough) ----------
    for prefix, metal_name, rough_name in [
        ("bpdy", "bpdy_metallicRoughness_metal.jpg", "bpdy_metallicRoughness_rough.jpg"),
        ("Scene_-_Root", "Scene_-_Root_metallicRoughness_metal.jpg", "Scene_-_Root_metallicRoughness_rough.jpg"),
    ]:
        metal, rough_img = bpy.data.images[metal_name], bpy.data.images[rough_name]
        w, h = metal.size
        mp = np.array(metal.pixels[:], dtype=np.float32).reshape(-1, 4)
        rp = np.array(rough_img.pixels[:], dtype=np.float32).reshape(-1, 4)
        out = np.zeros_like(mp)
        out[:, 0] = out[:, 1] = out[:, 2] = mp[:, 0]
        out[:, 3] = 1.0 - rp[:, 0]
        packed = bpy.data.images.new(prefix + "_MetallicSmooth", w, h, alpha=True)
        packed.colorspace_settings.name = "Non-Color"
        packed.pixels[:] = out.ravel()
        packed.filepath_raw = os.path.join(TEX, prefix + "_MetallicSmooth.png")
        packed.file_format = "PNG"
        packed.save()
        for img in (metal, rough_img):
            path = os.path.join(TEX, img.name)
            if os.path.exists(path):
                os.remove(path)
        print("PACKED", prefix)

    # ---------- bake floor normal + smoothness ----------
    floor = bpy.data.objects["Plane"]
    select_only([floor])
    uv = floor.data.uv_layers.new(name="BakeUV")
    floor.data.uv_layers.active = uv
    # top-down planar projection: the floor is a field of mostly flat slabs and its bump is a
    # 3D noise, so one continuous projection keeps texel density even (smart project shatters it)
    xs = [v.co.x for v in floor.data.vertices]
    ys = [v.co.y for v in floor.data.vertices]
    minx, miny, span = min(xs), min(ys), max(max(xs) - min(xs), max(ys) - min(ys))
    for loop in floor.data.loops:
        co = floor.data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = ((co.x - minx) / span, (co.y - miny) / span)
    uv.active_render = True

    scene.render.engine = "CYCLES"
    scene.cycles.samples = 16
    scene.cycles.device = "CPU"
    mat = floor.active_material
    nt = mat.node_tree


    def bake(kind, name, colorspace):
        img = bpy.data.images.new(name, BAKE_RES, BAKE_RES, alpha=True, float_buffer=False)
        img.colorspace_settings.name = colorspace
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        nt.nodes.active = node
        bpy.ops.object.bake(type=kind, normal_space="TANGENT", margin=8)
        nt.nodes.remove(node)
        return img


    nrm = bake("NORMAL", "Floor_Rock_Normal", "Non-Color")
    nrm.filepath_raw = os.path.join(TEX, "Floor_Rock_Normal.png")
    nrm.file_format = "PNG"
    nrm.save()

    rough = bake("ROUGHNESS", "Floor_Rock_Rough", "Non-Color")
    px = np.array(rough.pixels[:], dtype=np.float32).reshape(-1, 4)
    ms = np.zeros_like(px)
    ms[:, 3] = 1.0 - px[:, 0]  # Unity Lit: metallic in R, smoothness in A
    mimg = bpy.data.images.new("Floor_Rock_MetallicSmooth", BAKE_RES, BAKE_RES, alpha=True)
    mimg.colorspace_settings.name = "Non-Color"
    mimg.pixels[:] = ms.ravel()
    mimg.filepath_raw = os.path.join(TEX, "Floor_Rock_MetallicSmooth.png")
    mimg.file_format = "PNG"
    mimg.save()
    print("BAKED floor maps")

    # drop the original UV so the bake UV is the only one exported
    for layer in list(floor.data.uv_layers):
        if layer.name != "BakeUV":
            floor.data.uv_layers.remove(layer)

# ---------- character: flatten the Sketchfab hierarchy into one mesh ----------
char_objs = [bpy.data.objects[n] for n in CHAR_MESHES]
for o in char_objs:
    detach(o)
select_only(char_objs)
bpy.ops.object.convert(target="MESH")  # bake any modifiers
bpy.ops.object.join()
courier = bpy.context.view_layer.objects.active
courier.name = "Courier"
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
# origin at the feet
bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
lowest = min((courier.matrix_world @ v.co).z for v in courier.data.vertices)
cur = bpy.context.scene.cursor
cur.location = (courier.location.x, courier.location.y, lowest)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
print("COURIER world pos", tuple(round(v, 3) for v in courier.location), "height", round(courier.dimensions.z, 3))

common = dict(use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
              axis_forward="-Z", axis_up="Y", bake_space_transform=False, use_mesh_modifiers=True,
              mesh_smooth_type="FACE", use_tspace=True, path_mode="RELATIVE", add_leaf_bones=False)

# ---------- environment ----------
if not COURIER_ONLY:
    env = []
    for n in ENV_MESHES:
        o = bpy.data.objects[n]
        detach(o)
        select_only([o])
        bpy.ops.object.convert(target="MESH")  # applies the ocean modifier on the water
        env.append(o)
    for n in ["Camera", "Area", "Area.002"]:
        o = bpy.data.objects[n]
        detach(o)
        env.append(o)
    for o in env:
        o.name = RENAME.get(o.name, o.name)

    # keep the courier's spot in the environment as an empty so Unity knows where to spawn the player
    spawn = bpy.data.objects.new("PlayerSpawn", None)
    scene.collection.objects.link(spawn)
    spawn.matrix_world = courier.matrix_world.copy()
    env.append(spawn)

    select_only(env)
    bpy.ops.export_scene.fbx(filepath=os.path.join(MODELS, "Environment.fbx"),
                             object_types={"MESH", "CAMERA", "LIGHT", "EMPTY"}, **common)

# ---------- courier: rig for Unity Humanoid (see rig_courier.py), then export with its skeleton ----------
courier.location = (0, 0, 0)
rig = rig_courier.rig(courier)  # also recentres the body on the origin (the collider's centre in Unity)
select_only([rig, courier])
bpy.ops.export_scene.fbx(filepath=os.path.join(MODELS, "Courier.fbx"), object_types={"MESH", "ARMATURE"},
                         bake_anim=False, armature_nodetype="NULL", primary_bone_axis="Y",
                         secondary_bone_axis="X", **common)
print("EXPORT DONE")
