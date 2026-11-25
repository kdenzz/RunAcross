using System.Collections.Generic;
using RunAcross.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunAcross.EditorTools
{
    /// <summary>
    /// Level 01 "The Arches": the start island is boxed in except for a laser gate in front of the arch;
    /// beyond it a line of floating slabs runs +Z into the fog, with a bridge that rises when its target
    /// is shot, a second gate, stepping pads and the goal beacon. All placement numbers live here.
    /// </summary>
    public static class CourseBuilder
    {
        const int IgnoreRaycast = 2;

        public struct Result
        {
            public PathOpener[] path;
            public Transform playerStart;
        }

        static Dictionary<string, Material> _m;
        static Transform _root;

        public static Result Build(Transform env, Dictionary<string, Material> mats)
        {
            _m = mats;
            _root = new GameObject("Course").transform;

            var start = new GameObject("PlayerStart").transform;
            start.SetParent(_root);
            var spawn = env.Find("PlayerSpawn").position;
            start.SetPositionAndRotation(spawn, Quaternion.identity); // faces +Z, through the arch

            Bounds();

            // ---- 1. gate off the start island, two targets either side of the arch
            var t1 = Target("Target_1A", new Vector3(4.6f, 1.3f, 6.6f), spawn);
            var t2 = Target("Target_1B", new Vector3(-4.9f, 2.4f, 5.8f), spawn);
            var gate1 = Gate("Gate_1", 0f, 8.35f, -0.35f, 3.5f, t1, t2);
            gate1.objective = "Shoot both orange targets to drop the laser gate";
            gate1.openedMessage = "Gate open";

            // ---- 2. slabs into the fog
            Platform("Slab_1", 0f, -0.35f, 10.3f, 13.4f, 3.2f, _m["TrimCyan"]);
            Platform("Slab_2", 0f, 0.3f, 15.4f, 18.4f, 3.0f, _m["TrimCyan"]);
            var p3 = Platform("Slab_3", 2.2f, 0.95f, 20.4f, 24.4f, 3.4f, _m["TrimCyan"]);
            Checkpoint("Checkpoint_1", p3, 2.2f, 0.95f, 22.0f, 3.4f, 3.2f);

            // ---- 3. bridge over the long gap rises when the target on the far side is hit
            var t3 = Target("Target_2", new Vector3(2.2f, 4.2f, 35.6f), new Vector3(2.2f, 1f, 22f));
            var bridge = Bridge("Bridge", 2.2f, 0.95f, 24.4f, 32.4f, 1.8f, t3);
            bridge.objective = "The gap is too wide. Shoot the target across it to raise the bridge";
            bridge.openedMessage = "Bridge raised";

            var p4 = Platform("Slab_4", 2.2f, 0.95f, 32.4f, 38.6f, 4.2f, _m["TrimCyan"]);
            Checkpoint("Checkpoint_2", p4, 2.2f, 0.95f, 34.0f, 4.2f, 3f);

            // ---- 4. second gate; its targets hang out past it, one on each side
            var t4 = Target("Target_3A", new Vector3(-2.6f, 3.3f, 44.5f), new Vector3(2.2f, 1f, 35f));
            var t5 = Target("Target_3B", new Vector3(7.4f, 2.6f, 46.5f), new Vector3(2.2f, 1f, 35f));
            var gate2 = Gate("Gate_2", 2.2f, 38.2f, 0.95f, 4.3f, t4, t5);
            gate2.objective = "Two more targets beyond the gate. Shoot through the lasers";
            gate2.openedMessage = "Gate open";

            Platform("Slab_5", 2.2f, 0.95f, 40.6f, 43.6f, 3.0f, _m["TrimCyan"]);

            // ---- 5. stepping pads up to the goal (sprint-jump between them)
            Platform("Pad_1", 0.4f, 1.5f, 45.0f, 47.2f, 2.2f, _m["TrimOrange"]);
            Platform("Pad_2", 3.2f, 2.05f, 48.4f, 50.6f, 2.2f, _m["TrimOrange"]);
            Platform("Pad_3", 1.0f, 2.6f, 51.8f, 54.0f, 2.2f, _m["TrimOrange"]);

            var goalSlab = Platform("Slab_Goal", 1.5f, 2.6f, 55.2f, 61.5f, 5f, _m["TrimOrange"]);
            Goal(env, goalSlab, new Vector3(1.5f, 2.6f, 58.6f));

            return new Result { path = new[] { gate1, bridge, gate2 }, playerStart = start };
        }

        // ------------------------------------------------------------------ pieces

        static void Bounds()
        {
            // Invisible walls on Ignore Raycast so neither shots nor the camera deoccluder hit them.
            var b = new GameObject("IslandBounds").transform;
            b.SetParent(_root);
            Wall(b, new Vector3(-8.6f, 2f, 0f), new Vector3(0.4f, 7f, 18f));
            Wall(b, new Vector3(8.6f, 2f, 0f), new Vector3(0.4f, 7f, 18f));
            Wall(b, new Vector3(0f, 2f, -8.7f), new Vector3(18f, 7f, 0.4f));
            Wall(b, new Vector3(-5.35f, 2f, 8.6f), new Vector3(7.3f, 7f, 0.4f)); // north side, left of the gate
            Wall(b, new Vector3(5.35f, 2f, 8.6f), new Vector3(7.3f, 7f, 0.4f));  // north side, right of the gate
        }

        static void Wall(Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Wall") { layer = IgnoreRaycast, isStatic = true };
            go.transform.SetParent(parent);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        static Transform Platform(string name, float x, float topY, float z0, float z1, float width, Material trim)
        {
            var p = new GameObject(name) { isStatic = true }.transform;
            p.SetParent(_root);
            float len = z1 - z0, zc = (z0 + z1) * 0.5f;
            p.position = new Vector3(x, topY, zc);

            Box(p, "Slab", new Vector3(0f, -0.3f, 0f), new Vector3(width, 0.6f, len), _m["Body"], collider: true);
            // neon edges along both sides and across the front
            Box(p, "TrimL", new Vector3(-width * 0.5f, -0.04f, 0f), new Vector3(0.07f, 0.07f, len), trim, glow: true);
            Box(p, "TrimR", new Vector3(width * 0.5f, -0.04f, 0f), new Vector3(0.07f, 0.07f, len), trim, glow: true);
            Box(p, "TrimFront", new Vector3(0f, -0.04f, -len * 0.5f), new Vector3(width, 0.07f, 0.07f), trim, glow: true);
            // support pillar disappearing into the fog
            Box(p, "Pillar", new Vector3(0f, -12.5f, 0f), new Vector3(Mathf.Min(1f, width * 0.35f), 24f, Mathf.Min(1f, len * 0.3f)), _m["Body"]);
            return p;
        }

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider = false, bool glow = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.isStatic = parent.gameObject.isStatic;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (glow) r.shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Disc(Transform parent, string name, Vector3 localPos, float diameter, float thickness, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis -> local Z (faces forward)
            go.transform.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static ShootTarget Target(string name, Vector3 position, Vector3 faceTowards)
        {
            var root = new GameObject(name).transform;
            root.SetParent(_root);
            Vector3 look = faceTowards - position;
            look.y = 0f;
            root.SetPositionAndRotation(position, Quaternion.LookRotation(-look.normalized));

            Disc(root, "Rim", new Vector3(0f, 0f, 0.03f), 0.95f, 0.05f, _m["Body"]).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var face = Disc(root, "Face", Vector3.zero, 0.74f, 0.07f, _m["TargetIdle"]);
            face.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var core = Disc(root, "Core", new Vector3(0f, 0f, -0.045f), 0.22f, 0.02f, _m["Body"]);
            // cross bars so the spin reads
            var barA = Box(root, "BarA", new Vector3(0f, 0f, -0.04f), new Vector3(0.76f, 0.06f, 0.02f), _m["Body"]);
            var barB = Box(root, "BarB", new Vector3(0f, 0f, -0.04f), new Vector3(0.06f, 0.76f, 0.02f), _m["Body"]);
            foreach (var g in new[] { core, barA, barB }) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var col = root.gameObject.AddComponent<SphereCollider>();
            col.radius = 0.6f;

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = new Vector3(0f, 0f, -0.4f);
            light.type = LightType.Point;
            light.range = 3.5f;
            light.intensity = 2f;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            var t = root.gameObject.AddComponent<ShootTarget>();
            t.glowRenderers = new[] { face.GetComponent<Renderer>() };
            t.idleMaterial = _m["TargetIdle"];
            t.hitMaterial = _m["TargetHit"];
            t.glowLight = light;
            return t;
        }

        static PathOpener Gate(string name, float x, float z, float floorY, float width, params ShootTarget[] targets)
        {
            var gate = new GameObject(name) { isStatic = true }.transform;
            gate.SetParent(_root);
            gate.position = new Vector3(x, floorY, z);
            const float height = 2.7f;

            foreach (float side in new[] { -1f, 1f })
            {
                var post = Box(gate, side < 0 ? "PostL" : "PostR", new Vector3(side * width * 0.5f, height * 0.5f, 0f),
                    new Vector3(0.28f, height + 0.3f, 0.28f), _m["Body"], collider: true);
                post.layer = IgnoreRaycast;
                Box(gate, "PostGlow", new Vector3(side * (width * 0.5f - 0.15f), height * 0.5f, 0f),
                    new Vector3(0.03f, height, 0.1f), _m["TrimOrange"], glow: true);
            }
            Box(gate, "Lintel", new Vector3(0f, height + 0.2f, 0f), new Vector3(width + 0.28f, 0.16f, 0.28f), _m["Body"]);
            Box(gate, "LintelGlow", new Vector3(0f, height + 0.1f, 0f), new Vector3(width - 0.2f, 0.03f, 0.1f), _m["TrimOrange"], glow: true);

            var beams = new GameObject("Beams").transform;
            beams.SetParent(gate, false);
            beams.gameObject.layer = IgnoreRaycast; // shots pass between the lasers
            for (int i = 0; i < 6; i++)
                Box(beams, "Laser" + i, new Vector3(0f, 0.25f + i * 0.43f, 0f), new Vector3(width - 0.3f, 0.035f, 0.035f), _m["Laser"], glow: true);
            var block = beams.gameObject.AddComponent<BoxCollider>();
            block.center = new Vector3(0f, height * 0.5f, 0f);
            block.size = new Vector3(width - 0.28f, height, 0.3f);

            var light = new GameObject("LaserGlow").AddComponent<Light>();
            light.transform.SetParent(beams, false);
            light.transform.localPosition = new Vector3(0f, 1.2f, -0.6f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.05f);
            light.intensity = 1.6f;
            light.range = 4f;

            var opener = beams.gameObject.AddComponent<PathOpener>();
            opener.targets = targets;
            opener.openOffset = new Vector3(0f, -height - 0.4f, 0f);
            opener.duration = 0.9f;
            opener.disableOnOpen = new Collider[] { block };
            opener.hideWhenOpen = true;
            return opener;
        }

        static PathOpener Bridge(string name, float x, float topY, float z0, float z1, float width, params ShootTarget[] targets)
        {
            var b = new GameObject(name).transform;
            b.SetParent(_root);
            float len = z1 - z0;
            const float drop = 9f;
            b.position = new Vector3(x, topY - drop, (z0 + z1) * 0.5f);
            Box(b, "Deck", new Vector3(0f, -0.15f, 0f), new Vector3(width, 0.3f, len), _m["Body"], collider: true);
            Box(b, "TrimL", new Vector3(-width * 0.5f, -0.03f, 0f), new Vector3(0.06f, 0.06f, len), _m["TrimOrange"], glow: true);
            Box(b, "TrimR", new Vector3(width * 0.5f, -0.03f, 0f), new Vector3(0.06f, 0.06f, len), _m["TrimOrange"], glow: true);
            for (int i = 0; i < 4; i++) // cross strips so the run across reads as a bridge
                Box(b, "Rung" + i, new Vector3(0f, 0.005f, -len * 0.5f + (i + 0.5f) * len / 4f), new Vector3(width - 0.2f, 0.01f, 0.05f), _m["TrimCyan"], glow: true);

            var opener = b.gameObject.AddComponent<PathOpener>();
            opener.targets = targets;
            opener.openOffset = new Vector3(0f, drop, 0f);
            opener.duration = 1.8f;
            opener.disableOnOpen = new Collider[0];
            return opener;
        }

        static void Checkpoint(string name, Transform platform, float x, float topY, float z, float width, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(platform);
            go.transform.position = new Vector3(x, topY + 1f, z);
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(width, 2f, depth);

            var pad = Disc(go.transform, "Pad", Vector3.zero, 1.3f, 0.02f, _m["CheckpointIdle"]);
            pad.transform.position = new Vector3(x, topY + 0.01f, z);
            pad.transform.rotation = Quaternion.identity; // flat on the slab
            pad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(go.transform);
            spawn.SetPositionAndRotation(new Vector3(x, topY + 0.05f, z), Quaternion.identity);

            var cp = go.AddComponent<Gameplay.Checkpoint>();
            cp.spawnPoint = spawn;
            cp.pad = pad.GetComponent<Renderer>();
            cp.activeMaterial = _m["TargetHit"];
        }

        static void Goal(Transform env, Transform slab, Vector3 floorPoint)
        {
            var goal = new GameObject("Goal").transform;
            goal.SetParent(slab);
            goal.position = floorPoint;

            // the small arch from the hero shot, reused as the finish ring
            var src = env.Find("Arch_Small").gameObject;
            var ring = Object.Instantiate(src, goal);
            ring.name = "GoalRing";
            ring.transform.localScale = Vector3.one * 0.85f;
            var b = ring.GetComponent<Renderer>().bounds;
            ring.transform.position += floorPoint + Vector3.up * (b.extents.y + 0.05f) - b.center;
            ring.isStatic = false;
            Vector3 ringCenter = ring.GetComponent<Renderer>().bounds.center;

            var inner = Object.Instantiate(src, goal);
            inner.name = "GoalSpinner";
            Object.DestroyImmediate(inner.GetComponent<Collider>());
            inner.transform.localScale = Vector3.one * 0.45f;
            var ib = inner.GetComponent<Renderer>().bounds;
            inner.transform.position += ringCenter - ib.center;
            var ir = inner.GetComponent<Renderer>();
            var cyan = new Material[ir.sharedMaterials.Length];
            for (int i = 0; i < cyan.Length; i++) cyan[i] = _m["TrimCyan"];
            ir.sharedMaterials = cyan;
            ir.shadowCastingMode = ShadowCastingMode.Off;
            inner.isStatic = false;
            // spin around the ring's own centre: wrap it in a pivot at that centre
            var pivot = new GameObject("SpinPivot").transform;
            pivot.SetParent(goal);
            pivot.position = ringCenter;
            inner.transform.SetParent(pivot, true);

            // a light pillar into the sky so the goal reads through the fog from the start
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "BeaconBeam";
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(goal);
            beam.transform.position = ringCenter + Vector3.up * 16f;
            beam.transform.localScale = new Vector3(0.12f, 14f, 0.12f);
            var br = beam.GetComponent<Renderer>();
            br.sharedMaterial = _m["Beacon"];
            br.shadowCastingMode = ShadowCastingMode.Off;

            var light = new GameObject("GoalLight").AddComponent<Light>();
            light.transform.SetParent(goal);
            light.transform.position = ringCenter;
            light.type = LightType.Point;
            light.color = new Color(0.1f, 0.65f, 1f);
            light.intensity = 4f;
            light.range = 8f;

            var trigger = new GameObject("GoalTrigger");
            trigger.transform.SetParent(goal);
            trigger.transform.position = ringCenter;
            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.2f, 2.6f, 1.2f);
            var zone = trigger.AddComponent<GoalZone>();
            zone.spinner = pivot;
            zone.spinSpeed = 45f;
        }
    }
}
