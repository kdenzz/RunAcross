using System.Collections.Generic;
using RunAcross.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunAcross.EditorTools
{
    /// <summary>
    /// Builds each level's course. A level is laid out in its own frame where +Z runs away from the
    /// start island, and the frame is then turned to face the island's exit. Level 01 runs north
    /// through the arches; Level 02 runs east. All placement numbers live here.
    /// </summary>
    public static class CourseBuilder
    {
        const int IgnoreRaycast = 2;
        /// <summary>Targets get their own layer: shots hit it, but the camera deoccluder (Default only)
        /// and the courier's CharacterController don't, so drifting targets never shove the camera.</summary>
        public const int TargetLayer = 8;

        public struct Result
        {
            public PathOpener[] path;
            public Transform playerStart;
        }

        static Dictionary<string, Material> _m;
        static Transform _root;

        public static Result Build(int levelIndex, Transform env, Dictionary<string, Material> mats)
        {
            _m = mats;
            EnsureTargetLayer();
            return levelIndex == 0 ? Level01(env) : Level02(env);
        }

        static void EnsureTargetLayer()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            layers.GetArrayElementAtIndex(TargetLayer).stringValue = "Target";
            tags.ApplyModifiedPropertiesWithoutUndo();
            Physics.IgnoreLayerCollision(IgnoreRaycast, TargetLayer, true); // courier walks/jumps through targets
        }

        static Transform Begin(Transform env, float yaw, out Vector3 spawnLocal)
        {
            _root = new GameObject("Course").transform;
            _root.rotation = Quaternion.Euler(0f, yaw, 0f);
            var start = new GameObject("PlayerStart").transform;
            start.SetParent(_root, false);
            start.position = env.Find("PlayerSpawn").position;
            spawnLocal = start.localPosition; // faces the course's +Z
            return start;
        }

        // ------------------------------------------------------------------ Level 01: The Arches

        static Result Level01(Transform env)
        {
            var start = Begin(env, 0f, out var spawn);
            Bounds(north: true);

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

        // ------------------------------------------------------------------ Level 02: The Drift

        static Result Level02(Transform env)
        {
            // Frame turned 90 deg: local +Z = world +X (east exit), local +X = world -Z.
            var start = Begin(env, 90f, out var spawn);
            Bounds(north: false);
            var islandCentre = new Vector3(0f, 1f, 2f);

            // ---- 1. timed trio on the island: all three within 8 s or they reset
            var a = Target("Target_T1", new Vector3(-5.0f, 1.6f, 5.5f), islandCentre);
            var b = Target("Target_T2", new Vector3(4.8f, 2.2f, 3.0f), islandCentre);
            var c = Target("Target_T3", new Vector3(0.5f, 3.4f, -3.5f), islandCentre);
            var gate1 = Gate("Gate_1", 0f, 8.35f, -0.35f, 3.5f, a, b, c);
            gate1.timeLimit = 8f;
            gate1.objective = "Timed targets: hit all three within 8 seconds of the first";
            gate1.openedMessage = "Gate open";

            // ---- 2. a ferry across the first wide gap
            Platform("Slab_1", 0f, -0.35f, 10.3f, 13.3f, 3.2f, _m["TrimCyan"]);
            Mover("Ferry", new Vector3(0f, -0.35f, 15.1f), new Vector2(2.6f, 2.6f), new Vector3(0f, 0f, 7.6f), 7f, 0f);
            var s2 = Platform("Slab_2", 0f, -0.35f, 24.2f, 28.2f, 3.4f, _m["TrimCyan"]);
            Checkpoint("Checkpoint_1", s2, 0f, -0.35f, 25.6f, 3.4f, 2.6f);

            // ---- 3. a lift up to the high deck
            Mover("Lift", new Vector3(0f, -0.35f, 30.0f), new Vector2(2.4f, 2.4f), new Vector3(0f, 3.4f, 0f), 6f, 0f);
            var s3 = Platform("Slab_3", 0f, 3.05f, 32.2f, 37.4f, 3.6f, _m["TrimCyan"]);
            Checkpoint("Checkpoint_2", s3, 0f, 3.05f, 33.6f, 3.6f, 2.4f);

            // ---- 4. gate with two targets drifting on rails beyond it
            var d1 = Target("Target_D1", new Vector3(-5f, 5.2f, 43f), new Vector3(0f, 4f, 34f), new Vector3(10f, 0f, 0f), 5f, 0f);
            var d2 = Target("Target_D2", new Vector3(5f, 6.2f, 46f), new Vector3(0f, 4f, 34f), new Vector3(-10f, 0f, 0f), 6.5f, 0.3f);
            var gate2 = Gate("Gate_2", 0f, 37.0f, 3.05f, 3.9f, d1, d2);
            gate2.objective = "The targets drift. Lead your shots through the lasers";
            gate2.openedMessage = "Gate open";

            Platform("Slab_4", 0f, 3.05f, 38.8f, 41.8f, 3.2f, _m["TrimCyan"]);

            // ---- 5. a sideways shuttle over to the offset goal line
            Mover("Shuttle", new Vector3(0f, 3.05f, 43.4f), new Vector2(2.4f, 2.4f), new Vector3(6f, 0f, 0f), 6f, 0f);
            Platform("Slab_5", 6f, 3.05f, 45.8f, 49.0f, 3.0f, _m["TrimOrange"]);
            Platform("Pad_1", 3.6f, 3.6f, 50.4f, 52.4f, 2.0f, _m["TrimOrange"]);

            var goalSlab = Platform("Slab_Goal", 3.6f, 4.15f, 53.8f, 60.5f, 5f, _m["TrimOrange"]);
            Goal(env, goalSlab, new Vector3(3.6f, 4.15f, 57.6f));

            return new Result { path = new[] { gate1, gate2 }, playerStart = start };
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>Walls round the start island, open only where the gate is (north or east side).</summary>
        static void Bounds(bool north)
        {
            // Invisible walls on Ignore Raycast so neither shots nor the camera deoccluder hit them.
            var b = new GameObject("IslandBounds").transform; // world space, not under the turned course
            Wall(b, new Vector3(-8.6f, 2f, 0f), new Vector3(0.4f, 7f, 18f));
            Wall(b, new Vector3(0f, 2f, -8.7f), new Vector3(18f, 7f, 0.4f));
            if (north)
            {
                Wall(b, new Vector3(8.6f, 2f, 0f), new Vector3(0.4f, 7f, 18f));
                Wall(b, new Vector3(-5.35f, 2f, 8.6f), new Vector3(7.3f, 7f, 0.4f));
                Wall(b, new Vector3(5.35f, 2f, 8.6f), new Vector3(7.3f, 7f, 0.4f));
            }
            else
            {
                Wall(b, new Vector3(0f, 2f, 8.6f), new Vector3(18f, 7f, 0.4f));
                Wall(b, new Vector3(8.6f, 2f, -5.35f), new Vector3(0.4f, 7f, 7.3f));
                Wall(b, new Vector3(8.6f, 2f, 5.35f), new Vector3(0.4f, 7f, 7.3f));
            }
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
            p.SetParent(_root, false);
            float len = z1 - z0, zc = (z0 + z1) * 0.5f;
            p.localPosition = new Vector3(x, topY, zc);

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

        /// <summary>Positions are in the course frame. With a rail, the target drifts along it.</summary>
        static ShootTarget Target(string name, Vector3 position, Vector3 faceTowards, Vector3 rail = default, float railPeriod = 6f, float railPhase = 0f)
        {
            Transform parent = _root;
            if (rail != Vector3.zero)
            {
                var r = new GameObject(name + "_Rail").transform;
                r.SetParent(_root, false);
                r.localPosition = position;
                var mover = r.gameObject.AddComponent<MovingPlatform>();
                mover.travel = rail;
                mover.period = railPeriod;
                mover.phaseOffset = railPhase;
                parent = r;
                faceTowards -= position;
                position = Vector3.zero;
            }
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            Vector3 look = faceTowards - position;
            look.y = 0f;
            root.localPosition = position;
            root.localRotation = Quaternion.LookRotation(-look.normalized);

            Disc(root, "Rim", new Vector3(0f, 0f, 0.03f), 0.95f, 0.05f, _m["Body"]).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var face = Disc(root, "Face", Vector3.zero, 0.74f, 0.07f, _m["TargetIdle"]);
            face.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var core = Disc(root, "Core", new Vector3(0f, 0f, -0.045f), 0.22f, 0.02f, _m["Body"]);
            // cross bars so the spin reads
            var barA = Box(root, "BarA", new Vector3(0f, 0f, -0.04f), new Vector3(0.76f, 0.06f, 0.02f), _m["Body"]);
            var barB = Box(root, "BarB", new Vector3(0f, 0f, -0.04f), new Vector3(0.06f, 0.76f, 0.02f), _m["Body"]);
            foreach (var g in new[] { core, barA, barB }) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            root.gameObject.layer = TargetLayer;
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
            gate.SetParent(_root, false);
            gate.localPosition = new Vector3(x, floorY, z);
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
            b.SetParent(_root, false);
            float len = z1 - z0;
            const float drop = 9f;
            b.localPosition = new Vector3(x, topY - drop, (z0 + z1) * 0.5f);
            Box(b, "Deck", new Vector3(0f, -0.15f, 0f), new Vector3(width, 0.3f, len), _m["Body"], collider: true);
            Box(b, "TrimL", new Vector3(-width * 0.5f, -0.03f, 0f), new Vector3(0.06f, 0.06f, len), _m["TrimOrange"], glow: true);
            Box(b, "TrimR", new Vector3(width * 0.5f, -0.03f, 0f), new Vector3(0.06f, 0.06f, len), _m["TrimOrange"], glow: true);
            for (int i = 0; i < 4; i++) // cross strips so the run across reads as a bridge
                Box(b, "Rung" + i, new Vector3(0f, 0.005f, -len * 0.5f + (i + 0.5f) * len / 4f), new Vector3(width - 0.2f, 0.01f, 0.05f), _m["TrimCyan"], glow: true);

            var opener = b.gameObject.AddComponent<PathOpener>();
            opener.targets = targets;
            opener.openOffset = new Vector3(0f, drop, 0f); // world up either way
            opener.duration = 1.8f;
            opener.disableOnOpen = new Collider[0];
            return opener;
        }

        /// <summary>A moving slab (ferry, lift, shuttle). <paramref name="top"/> is the top centre at the start of its trip.</summary>
        static void Mover(string name, Vector3 top, Vector2 size, Vector3 travel, float period, float phase)
        {
            var p = new GameObject(name).transform;
            p.SetParent(_root, false);
            p.localPosition = top;
            Box(p, "Deck", new Vector3(0f, -0.2f, 0f), new Vector3(size.x, 0.4f, size.y), _m["Body"], collider: true);
            foreach (float sx in new[] { -1f, 1f })
            {
                Box(p, "TrimX", new Vector3(sx * size.x * 0.5f, -0.04f, 0f), new Vector3(0.07f, 0.07f, size.y), _m["TrimOrange"], glow: true);
                Box(p, "TrimZ", new Vector3(0f, -0.04f, sx * size.y * 0.5f), new Vector3(size.x, 0.07f, 0.07f), _m["TrimOrange"], glow: true);
            }
            Box(p, "Underglow", new Vector3(0f, -0.41f, 0f), new Vector3(size.x * 0.6f, 0.02f, size.y * 0.6f), _m["TrimCyan"], glow: true);
            p.gameObject.AddComponent<Rigidbody>().isKinematic = true; // a moved collider should be kinematic
            var m = p.gameObject.AddComponent<MovingPlatform>();
            m.travel = travel;
            m.period = period;
            m.phaseOffset = phase;
        }

        static void Checkpoint(string name, Transform platform, float x, float topY, float z, float width, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(platform);
            go.transform.position = _root.TransformPoint(new Vector3(x, topY + 1f, z));
            go.transform.rotation = _root.rotation;
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(width, 2f, depth);

            var pad = Disc(go.transform, "Pad", Vector3.zero, 1.3f, 0.02f, _m["CheckpointIdle"]);
            pad.transform.position = _root.TransformPoint(new Vector3(x, topY + 0.01f, z));
            pad.transform.rotation = Quaternion.identity; // flat on the slab
            pad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(go.transform);
            spawn.SetPositionAndRotation(_root.TransformPoint(new Vector3(x, topY + 0.05f, z)), _root.rotation); // faces down the course

            var cp = go.AddComponent<Gameplay.Checkpoint>();
            cp.spawnPoint = spawn;
            cp.pad = pad.GetComponent<Renderer>();
            cp.activeMaterial = _m["TargetHit"];
        }

        static void Goal(Transform env, Transform slab, Vector3 floorPoint)
        {
            var goal = new GameObject("Goal").transform;
            goal.SetParent(slab);
            goal.SetPositionAndRotation(_root.TransformPoint(floorPoint), _root.rotation);
            floorPoint = goal.position;

            // the small arch from the hero shot, reused as the finish ring
            var src = env.Find("Arch_Small").gameObject;
            var ring = Object.Instantiate(src, goal);
            ring.name = "GoalRing";
            ring.transform.localScale = Vector3.one * 0.85f;
            var b = ring.GetComponent<Renderer>().bounds;
            ring.transform.position += floorPoint + Vector3.up * (b.extents.y + 0.05f) - b.center;
            ring.isStatic = false;
            ring.layer = IgnoreRaycast; // keep the camera deoccluder from diving into the ring at the finish
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
            inner.layer = IgnoreRaycast;
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
            trigger.transform.SetPositionAndRotation(ringCenter, goal.rotation);
            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.2f, 2.6f, 1.2f);
            var zone = trigger.AddComponent<GoalZone>();
            zone.spinner = pivot;
            zone.spinSpeed = 45f;
        }
    }
}
