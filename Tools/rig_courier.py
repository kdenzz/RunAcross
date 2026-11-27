"""Rig the courier for Unity Humanoid. Called from export_fbx.py on the joined "Courier" mesh.

The Sketchfab model arrives as one static mesh (no armature, no weights) standing arms-down and
facing +Y, feet at the object origin. It is a robot made of ~3,100 separate mesh islands, so each
island is skinned rigidly (weight 1.0) to the bone nearest its centre: plates move as solid parts
and joints never stretch.

Steps: recentre the body on its origin, build a Mixamo-named skeleton from the joint table below,
weight islands, then raise the arms into a T-pose and make that the rest pose (what Unity's
Humanoid avatar expects). Returns the armature object (the mesh is parented to it).
"""
import bmesh
import bpy
import numpy as np
from mathutils import Quaternion, Vector

# Joint table, metres, in the courier's own frame: feet at z=0, body centred on x/y, facing +Y.
# The character's LEFT is -X (it faces +Y). Read from front/side vertex plots of the mesh:
# round joint discs at shoulders 1.04, elbows 0.82, wrists 0.65, hips 0.65, knees 0.40, ankles 0.12.
CENTRE = [
    # name, head, tail, parent
    ("Hips",   (0.0, 0.00, 0.68), (0.0, -0.01, 0.78), None),
    ("Spine",  (0.0, -0.01, 0.78), (0.0, -0.02, 0.90), "Hips"),
    ("Spine1", (0.0, -0.02, 0.90), (0.0, -0.02, 1.00), "Spine"),
    ("Spine2", (0.0, -0.02, 1.00), (0.0, -0.01, 1.10), "Spine1"),
    ("Neck",   (0.0, -0.01, 1.10), (0.0, 0.00, 1.17), "Spine2"),
    ("Head",   (0.0, 0.00, 1.17), (0.0, 0.00, 1.31), "Neck"),
]
# (name, head, tail, parent) for the character's left side; the right side mirrors x.
SIDE = [
    ("Shoulder", (0.05, -0.02, 1.04), (0.18, -0.02, 1.04), "Spine2"),
    ("Arm",      (0.18, -0.02, 1.04), (0.198, -0.01, 0.82), "Shoulder"),
    ("ForeArm",  (0.198, -0.01, 0.82), (0.20, 0.00, 0.65), "Arm"),
    ("Hand",     (0.20, 0.00, 0.65), (0.20, 0.00, 0.50), "ForeArm"),
    ("UpLeg",    (0.095, 0.00, 0.65), (0.095, 0.00, 0.40), "Hips"),
    ("Leg",      (0.095, 0.00, 0.40), (0.095, -0.03, 0.12), "UpLeg"),
    ("Foot",     (0.095, -0.03, 0.12), (0.095, 0.08, 0.03), "Leg"),
    ("ToeBase",  (0.095, 0.08, 0.03), (0.095, 0.15, 0.03), "Foot"),
]
ARM_BONES = {"Shoulder", "Arm", "ForeArm", "Hand"}
# Islands whose centre lies outside |x| (and above ARM_MIN_Z) belong to an arm, inside to torso/legs.
# Measured empty gap between limb and body: |x| 0.141-0.154 from hips to elbows, ~0.155 at the chest.
ARM_MIN_ABS_X_LOW, ARM_MIN_ABS_X_HIGH, ARM_SPLIT_Z = 0.147, 0.155, 0.90
ARM_MIN_Z = 0.45
SKIN_ONLY = {"Shoulder"}  # bones that exist for the hierarchy but take no geometry


def _bone_table():
    bones = [(n, Vector(h), Vector(t), p) for n, h, t, p in CENTRE]
    for side, sx in (("Left", -1.0), ("Right", 1.0)):
        for n, h, t, p in SIDE:
            parent = p if p in ("Spine2", "Hips") else side + p
            bones.append((side + n, Vector((h[0] * sx, h[1], h[2])), Vector((t[0] * sx, t[1], t[2])), parent))
    return bones


def _seg_dist(p, a, b):
    """Distance from points p (N,3) to segment a-b."""
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(ab @ ab, 1e-9), 0.0, 1.0)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1)


def _islands(mesh):
    """Vertex index arrays, one per connected island."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    seen = np.zeros(len(bm.verts), bool)
    out = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack, idx = [v], []
        seen[v.index] = True
        while stack:
            x = stack.pop()
            idx.append(x.index)
            for e in x.link_edges:
                w = e.other_vert(x)
                if not seen[w.index]:
                    seen[w.index] = True
                    stack.append(w)
        out.append(np.array(idx))
    bm.free()
    return out


def recentre(courier):
    """Put the body on the origin: x on the bounding-box centre (the model is left/right symmetric),
    y on the vertex median (a stray island 45 cm behind the body skews the y bounds), feet at z=0."""
    co = np.empty(len(courier.data.vertices) * 3)
    courier.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    shift = np.array([(co[:, 0].min() + co[:, 0].max()) / 2, np.median(co[:, 1]), co[:, 2].min()])
    co -= shift
    courier.data.vertices.foreach_set("co", co.ravel())
    courier.data.update()
    print("RIG recentred by", shift.round(3))


def rig(courier):
    recentre(courier)
    scene = bpy.context.scene
    bones = _bone_table()

    # ---- armature
    arm_data = bpy.data.armatures.new("CourierRig")
    arm = bpy.data.objects.new("CourierRig", arm_data)
    scene.collection.objects.link(arm)
    arm.matrix_world = courier.matrix_world.copy()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    for name, head, tail, parent in bones:
        b = eb.new(name)
        b.head, b.tail = head, tail
        b.roll = 0.0
        if parent:
            b.parent = eb[parent]
            b.use_connect = (b.parent.tail - b.head).length < 1e-4
    bpy.ops.object.mode_set(mode="OBJECT")

    # ---- rigid island weights
    co = np.empty(len(courier.data.vertices) * 3)
    courier.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    names = [b[0] for b in bones]
    heads = [np.array(b[1]) for b in bones]
    tails = [np.array(b[2]) for b in bones]
    is_arm = np.array([any(n.endswith(a) for a in ARM_BONES) for n in names])
    takes = np.array([not any(n.endswith(s) for s in SKIN_ONLY) for n in names])
    groups = {n: courier.vertex_groups.new(name=n) for n in names}
    counts = dict.fromkeys(names, 0)
    islands = _islands(courier.data)
    centres = np.array([co[i].mean(0) for i in islands])
    dist = np.stack([_seg_dist(centres, h, t) for h, t in zip(heads, tails)], axis=1)  # (islands, bones)
    cut = np.where(centres[:, 2] < ARM_SPLIT_Z, ARM_MIN_ABS_X_LOW, ARM_MIN_ABS_X_HIGH)
    arm_side = (np.abs(centres[:, 0]) >= cut) & (centres[:, 2] >= ARM_MIN_Z)
    for k, idx in enumerate(islands):
        allowed = takes & (is_arm if arm_side[k] else ~is_arm)
        d = np.where(allowed, dist[k], np.inf)
        name = names[int(np.argmin(d))]
        groups[name].add(idx.tolist(), 1.0, "REPLACE")
        counts[name] += len(idx)
    print("RIG islands", len(islands), "verts per bone", {n: c for n, c in counts.items() if c})

    mod = courier.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    courier.parent = arm

    _apply_t_pose(arm, courier)
    return arm


def _apply_t_pose(arm, courier):
    """Raise both arms to horizontal, bake that into the mesh and make it the rest pose."""
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    for side, sx in (("Left", -1.0), ("Right", 1.0)):
        pb = arm.pose.bones[side + "Arm"]
        rest = pb.bone.matrix_local.to_quaternion()
        down = (pb.bone.tail_local - pb.bone.head_local).normalized()
        out = Vector((sx, 0.0, 0.0))
        world_rot = down.rotation_difference(out)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = rest.inverted() @ world_rot @ rest
        # straighten the slight elbow bend so the whole arm is level
        for child in ("ForeArm", "Hand"):
            cb = arm.pose.bones[side + child]
            cb.rotation_mode = "QUATERNION"
            parent_dir = (pb.bone.tail_local - pb.bone.head_local).normalized()
            child_dir = (cb.bone.tail_local - cb.bone.head_local).normalized()
            fix = child_dir.rotation_difference(parent_dir)
            crest = cb.bone.matrix_local.to_quaternion()
            cb.rotation_quaternion = crest.inverted() @ fix @ crest
            pb = cb
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")

    # bake the posed mesh, then make the pose the new rest pose
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = courier
    courier.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Armature")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    mod = courier.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    print("RIG T-pose applied")
