using System.IO;
using RunAcross.Audio;
using RunAcross.Core;
using RunAcross.Player;
using RunAcross.Rendering;
using RunAcross.UI;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace RunAcross.EditorTools
{
    /// <summary>
    /// Rebuilds Assets/_Project/Scenes/Level_01.unity from the exported Blender assets
    /// (Tools/export_fbx.py): the neon look, the main menu on the hero shot, the player and the course
    /// (<see cref="CourseBuilder"/>). Re-run after re-exporting. Every look value lives in the constants
    /// and the Look* methods below, so tuning the mood means changing one place.
    /// </summary>
    public static class LevelBuilder
    {
        const string Root = "Assets/_Project";
        const string SceneName = "Level_01";
        const string ScenePath = Root + "/Scenes/" + SceneName + ".unity";
        const string MatDir = Root + "/Art/Materials";
        const string TexDir = Root + "/Art/Textures";
        const string EnvFbx = Root + "/Art/Models/Environment.fbx";
        const string CourierFbx = Root + "/Art/Models/Courier.fbx";
        const string ProfilePath = Root + "/Settings/VP_LookDev_Neon.asset";

        // ---- palette (linear) ----
        static readonly Color FogColor = new Color(0.17f, 0.11f, 0.46f);
        static readonly Color SkyTop = new Color(0.03f, 0.045f, 0.17f);
        static readonly Color Orange = new Color(1f, 0.13f, 0.0f);
        static readonly Color Cyan = new Color(0.0f, 0.75f, 1f);
        static readonly Color NeonBlue = new Color(0.05f, 0.45f, 1f);
        static readonly Color Red = new Color(1f, 0.06f, 0.02f);

        [MenuItem("RunAcross/Build Level 01")]
        public static void Build()
        {
            ConfigureTextures();
            var mats = BuildMaterials();
            ConfigureModels(mats);
            ConfigurePipeline();
            QualityPresets.Configure();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var env = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnvFbx));
            env.isStatic = true;
            foreach (var t in env.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;

            Transform Find(string n) => env.transform.Find(n);
            var water = Find("Floor_Water");
            water.gameObject.AddComponent<PlanarReflection>();
            foreach (var n in new[] { "Floor_Rock", "Floor_Water", "Arch_Large", "Arch_Small", "Building_Body", "Building_Panel" })
                Find(n).gameObject.AddComponent<MeshCollider>();

            LookEnvironment(mats["Sky"]);
            LookLights(Find("Sun_Blue"), Find("Light_Red"), Find("NeonBar"), Find("Arch_Large"), mats["Window"]);
            var volume = LookVolume();

            var course = CourseBuilder.Build(env.transform, mats);
            var player = BuildPlayer(Find("PlayerSpawn"), mats);
            var cams = BuildCameras(Find("LookDevCamera"), player);
            BuildSystems(cams, player, course, volume, water.GetComponent<PlanarReflection>());

            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.position = new Vector3(0f, 1.5f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(24f, 10f, 24f);
            probe.boxProjection = true;
            probe.hdr = true;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Bake only the reflection probe; there are no lightmaps (all lights are realtime).
            string probeTex = Root + "/Scenes/" + SceneName + "/ReflectionProbe-0.exr";
            Directory.CreateDirectory(Path.GetDirectoryName(probeTex));
            Lightmapping.BakeReflectionProbe(probe, probeTex);
            AssetDatabase.ImportAsset(probeTex);
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(probeTex);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[RunAcross] Level built: " + ScenePath);
        }

        // ------------------------------------------------------------------ assets

        static void ConfigureTextures()
        {
            foreach (var path in new[] { "Floor_Rock_Normal.png", "bpdy_normal_norm.jpg", "Scene_-_Root_normal.jpg" })
                SetTexture(path, TextureImporterType.NormalMap, false);
            foreach (var path in new[] { "Floor_Rock_MetallicSmooth.png", "bpdy_MetallicSmooth.png", "Scene_-_Root_MetallicSmooth.png" })
                SetTexture(path, TextureImporterType.Default, false);
        }

        static void SetTexture(string file, TextureImporterType type, bool srgb)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/" + file);
            if (ti == null) { Debug.LogWarning("[RunAcross] Missing texture " + file); return; }
            if (ti.textureType == type && ti.sRGBTexture == srgb) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.SaveAndReimport();
        }

        static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + file);

        static System.Collections.Generic.Dictionary<string, Material> BuildMaterials()
        {
            Directory.CreateDirectory(MatDir);
            var m = new System.Collections.Generic.Dictionary<string, Material>();

            var rock = Lit("M_Rock", new Color(0.02f, 0.018f, 0.03f), 0f, 0.68f);
            rock.SetTexture("_BumpMap", Tex("Floor_Rock_Normal.png"));
            rock.SetFloat("_BumpScale", 1.0f);
            rock.SetTexture("_MetallicGlossMap", Tex("Floor_Rock_MetallicSmooth.png"));
            rock.EnableKeyword("_NORMALMAP");
            rock.EnableKeyword("_METALLICSPECGLOSSMAP");
            m["Material.001"] = rock;

            var water = Save(new Material(Shader.Find("RunAcross/NeonWater")), "M_Water");
            water.SetTexture("_BumpMap", Tex("Floor_Rock_Normal.png"));
            m["Material.002"] = water;

            m["Material.003"] = Lit("M_ArchBody", new Color(0.04f, 0.04f, 0.07f), 0.3f, 0.25f);
            m["Material.004"] = Emissive("M_Emissive_Orange", Orange, 3.4f);
            m["Window"] = Emissive("M_Emissive_Window", new Color(1f, 0.26f, 0.02f), 2.6f);
            m["emission.001"] = Emissive("M_Emissive_Cyan", Cyan, 3f);
            m["Material"] = Emissive("M_NeonBar", new Color(0.02f, 0.55f, 1f), 4.5f);
            m["decor.001"] = Lit("M_BuildingDecor", new Color(0.25f, 0.87f, 1f), 1f, 0.8f);

            var body = Lit("M_BuildingBody", new Color(0.22f, 0.22f, 0.3f), 1f, 0.5f);
            body.SetTexture("_BaseMap", Tex("bpdy_baseColor.jpg"));
            body.SetTexture("_BumpMap", Tex("bpdy_normal_norm.jpg"));
            body.SetTexture("_MetallicGlossMap", Tex("bpdy_MetallicSmooth.png"));
            body.EnableKeyword("_NORMALMAP");
            body.EnableKeyword("_METALLICSPECGLOSSMAP");
            m["bpdy.001"] = body;

            var courier = Lit("M_Courier", Color.white, 1f, 1f);
            courier.SetTexture("_BaseMap", Tex("Scene_-_Root_baseColor.jpg"));
            courier.SetTexture("_BumpMap", Tex("Scene_-_Root_normal.jpg"));
            courier.SetTexture("_MetallicGlossMap", Tex("Scene_-_Root_MetallicSmooth.png"));
            courier.SetTexture("_EmissionMap", Tex("Scene_-_Root_emissive.jpg"));
            courier.SetColor("_EmissionColor", Color.white * 2f);
            courier.EnableKeyword("_NORMALMAP");
            courier.EnableKeyword("_METALLICSPECGLOSSMAP");
            courier.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            m["Scene___Root"] = courier;

            // gameplay pieces (keys used by CourseBuilder)
            m["Body"] = m["Material.003"];
            m["TrimCyan"] = m["emission.001"];
            m["TrimOrange"] = m["Material.004"];
            m["TargetIdle"] = Emissive("M_Target_Idle", new Color(1f, 0.32f, 0.02f), 4.2f);
            m["TargetHit"] = Emissive("M_Target_Hit", new Color(0.05f, 0.7f, 1f), 4.4f);
            m["Laser"] = Emissive("M_Laser", new Color(1f, 0.05f, 0.03f), 5f);
            m["CheckpointIdle"] = Emissive("M_Checkpoint_Idle", new Color(0.2f, 0.35f, 1f), 0.6f);
            m["Beacon"] = Emissive("M_Beacon", new Color(0.1f, 0.6f, 1f), 3.6f);

            var sky = Save(new Material(Shader.Find("RunAcross/GradientSky")), "M_Sky");
            sky.SetColor("_TopColor", SkyTop);
            sky.SetColor("_HorizonColor", FogColor);
            sky.SetColor("_BottomColor", FogColor * 0.6f);
            m["Sky"] = sky;

            foreach (var mat in new System.Collections.Generic.HashSet<Material>(m.Values))
            {
                // Let URP derive keywords (_NORMALMAP, _EMISSION, cull state...) from the properties we set.
                if (mat.shader.name == "Universal Render Pipeline/Lit")
                    BaseShaderGUI.SetMaterialKeywords(mat, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material Lit(string name, Color baseColor, float metallic, float smoothness)
        {
            var mat = Save(new Material(Shader.Find("Universal Render Pipeline/Lit")), name);
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        static Material Emissive(string name, Color color, float intensityEv)
        {
            var mat = Lit(name, Color.black, 0f, 0.5f);
            mat.SetColor("_EmissionColor", color * Mathf.Pow(2f, intensityEv));
            mat.EnableKeyword("_EMISSION");
            // URP strips _EMISSION unless a GI flag is set; nothing is baked, so this only keeps the keyword.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            mat.SetFloat("_Cull", (float)CullMode.Off); // thin planes in the source; visible from both sides
            mat.doubleSidedGI = true;
            return mat;
        }

        static Material Save(Material fresh, string name)
        {
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = fresh.shader;
                existing.CopyPropertiesFromMaterial(fresh);
                existing.shaderKeywords = fresh.shaderKeywords;
                Object.DestroyImmediate(fresh);
                return existing;
            }
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }

        static void ConfigureModels(System.Collections.Generic.Dictionary<string, Material> mats)
        {
            foreach (var path in new[] { EnvFbx, CourierFbx })
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.importCameras = false;
                mi.importLights = false;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var kv in mats)
                    if (kv.Key.Contains(".") || kv.Key == "Material" || kv.Key == "Scene___Root")
                        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                mi.SaveAndReimport();
            }
        }

        static void ConfigurePipeline()
        {
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) return;
            rp.supportsHDR = true;
            rp.msaaSampleCount = 4;
            rp.shadowDistance = 30f;
            EditorUtility.SetDirty(rp);
        }

        // ------------------------------------------------------------------ look

        static void LookEnvironment(Material sky)
        {
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.08f, 0.22f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.05f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.02f, 0.05f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = 0.07f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
        }

        static void LookLights(Transform sunRef, Transform redRef, Transform neonBar, Transform arch, Material windowMat)
        {
            var sun = NewLight("Sun_Blue", LightType.Directional, new Color(0.2f, 0.3f, 1f), 0.35f);
            sun.transform.rotation = sunRef.rotation;
            sun.shadows = LightShadows.Soft;

            // Blender gets its orange windows from a red area light inside the building. URP area lights
            // are bake-only, so: an emissive panel behind the window openings, plus a realtime point light
            // in front of them that spills the colour onto the wet floor.
            // Corners traced from the look-dev shot onto the building's front face (x = -1.83).
            WindowPane("Building_WindowGlow_Upper", windowMat,
                new Vector3(-1.83f, 0.561f, -3.183f), new Vector3(-1.83f, 0.486f, -4.963f),
                new Vector3(-1.83f, 0.189f, -4.958f), new Vector3(-1.83f, 0.222f, -3.234f));
            WindowPane("Building_WindowGlow_Lower", windowMat,
                new Vector3(-1.83f, 0.157f, -3.233f), new Vector3(-1.83f, 0.131f, -4.957f),
                new Vector3(-1.83f, -0.165f, -4.952f), new Vector3(-1.83f, -0.178f, -3.304f));

            var red = NewLight("Light_Red", LightType.Point, new Color(1f, 0.16f, 0.03f), 3f);
            red.transform.position = redRef.position + new Vector3(-0.7f, 0.1f, -0.4f);
            red.range = 5f;

            // Fill from the emissive arch faces onto the wet floor in front of them.
            var archGlow = NewLight("ArchGlow_Orange", LightType.Point, Orange, 2f);
            archGlow.transform.position = arch.position + new Vector3(0f, 0.1f, 0f);
            archGlow.range = 7f;

            var neon = NewLight("NeonBar_Blue", LightType.Point, NeonBlue, 2.5f);
            neon.transform.position = neonBar.GetComponent<Renderer>().bounds.center + Vector3.up * 0.2f;
            neon.range = 6f;
        }

        static void WindowPane(string name, Material mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { a, b, c, d };
            mesh.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.left, Vector3.left, Vector3.left, Vector3.left };
            mesh.RecalculateBounds();
            var go = new GameObject(name) { isStatic = true };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Light NewLight(string name, LightType type, Color color, float intensity)
        {
            var go = new GameObject(name);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            return l;
        }

        static Volume LookVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            foreach (var c in profile.components.ToArray()) { profile.Remove(c.GetType()); Object.DestroyImmediate(c, true); }

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.8f);
            bloom.scatter.Override(0.8f);
            bloom.highQualityFiltering.Override(true);

            Add<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

            var ca = Add<ColorAdjustments>(profile);
            ca.postExposure.Override(0.9f);
            ca.contrast.Override(12f);
            ca.saturation.Override(25f);

            Add<WhiteBalance>(profile).temperature.Override(-12f);

            var dof = Add<DepthOfField>(profile);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(11.5f);
            dof.focalLength.Override(55f);
            dof.aperture.Override(5.6f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.5f);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.18f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcess_Global");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            return volume;
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ------------------------------------------------------------------ player + cameras

        const int IgnoreRaycastLayer = 2;

        static GameObject BuildPlayer(Transform spawn, System.Collections.Generic.Dictionary<string, Material> mats)
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var player = new GameObject("Player") { tag = "Player", layer = IgnoreRaycastLayer }; // shots and the camera deoccluder ignore the courier
            player.transform.position = spawn.position;
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // hero-shot pose; turns to the course on Play

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.3f;
            cc.radius = 0.22f;
            cc.center = new Vector3(0f, 0.67f, 0f);
            cc.skinWidth = 0.03f;
            cc.stepOffset = 0.35f;

            var motor = player.AddComponent<PlayerMotor>();
            motor.actions = actions;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CourierFbx), player.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // FBX comes in lying along Z
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IgnoreRaycastLayer;

            var target = new GameObject("CameraTarget").transform;
            target.SetParent(player.transform, false);
            target.localPosition = new Vector3(0f, 1.1f, 0f);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(player.transform, false);
            muzzle.localPosition = new Vector3(0.22f, 0.85f, 0.25f);

            var shooter = player.AddComponent<PlayerShooter>();
            shooter.actions = actions;
            shooter.muzzle = muzzle;
            shooter.tracerMaterial = mats["TargetHit"];
            shooter.sparkMaterial = mats["TargetHit"];
            return player;
        }

        struct Cams
        {
            public CinemachineCamera menu, player;
            public CinemachineBrain brain;
            public UniversalAdditionalCameraData data;
        }

        static Cams BuildCameras(Transform lookDevRef, GameObject player)
        {
            // With camera import off, the FBX node keeps Blender's camera axes: the lens looks down the
            // node's local -X in Unity. Re-aim so forward is the real view direction.
            var shot = Quaternion.LookRotation(-lookDevRef.right, lookDevRef.up);

            var main = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = main.AddComponent<Camera>();
            cam.fieldOfView = 61.6f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            var data = main.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var brain = main.AddComponent<CinemachineBrain>();
            brain.IgnoreTimeScale = true; // keep blending while paused menus are up
            main.AddComponent<AudioListener>();
            main.transform.SetPositionAndRotation(lookDevRef.position, shot);

            // The menu shot = the Blender render's camera. 17 mm on a 36 mm sensor, horizontal fit -> ~61.6 deg vertical at 16:9.
            var menu = new GameObject("CM_Menu").AddComponent<CinemachineCamera>();
            menu.transform.SetPositionAndRotation(lookDevRef.position, shot);
            menu.Lens = new LensSettings { FieldOfView = 61.6f, NearClipPlane = 0.05f, FarClipPlane = 200f };
            menu.Priority = 20;

            var target = player.transform.Find("CameraTarget");
            var follow = new GameObject("CM_Player").AddComponent<CinemachineCamera>();
            follow.Lens = new LensSettings { FieldOfView = 58f, NearClipPlane = 0.05f, FarClipPlane = 200f };
            follow.Target.TrackingTarget = target;
            follow.Priority = 0;
            var orbit = follow.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 4.5f;
            orbit.VerticalAxis.Value = 14f;
            orbit.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            var composer = follow.gameObject.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 0.6f, 0f); // aim above the head so the crosshair clears the courier
            follow.gameObject.AddComponent<CinemachineInputAxisController>();
            var deocc = follow.gameObject.AddComponent<CinemachineDeoccluder>();
            deocc.CollideAgainst = 1; // Default layer only: invisible walls, gates and the player are on Ignore Raycast
            deocc.IgnoreTag = "Player";

            player.GetComponent<PlayerMotor>().cameraTransform = main.transform;
            player.GetComponent<PlayerShooter>().aimCamera = main.transform;
            return new Cams { menu = menu, player = follow, brain = brain, data = data };
        }

        // ------------------------------------------------------------------ systems + UI

        const string UiDir = Root + "/UI";
        const string AudioDir = Root + "/Audio";

        static void BuildSystems(Cams cams, GameObject player, CourseBuilder.Result course, Volume volume, PlanarReflection reflection)
        {
            var systems = new GameObject("GameSystems");

            var audio = systems.AddComponent<GameAudio>();
            audio.music = Clip("music_neon_loop.wav", streaming: true);
            audio.clips = new[]
            {
                Clip("sfx_shoot.wav"), Clip("sfx_target_hit.wav"), Clip("sfx_gate_open.wav"), Clip("sfx_ui_click.wav"),
                Clip("sfx_ui_hover.wav"), Clip("sfx_jump.wav"), Clip("sfx_land.wav"), Clip("sfx_checkpoint.wav"),
                Clip("sfx_complete.wav"), Clip("sfx_respawn.wav"),
            };

            var graphics = systems.AddComponent<GraphicsApplier>();
            graphics.volume = volume;
            graphics.reflection = reflection;
            graphics.cameraData = cams.data;

            var uiGo = new GameObject("UI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = PanelSettingsAsset();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "/GameUI.uxml");
            var ui = uiGo.AddComponent<GameUI>();

            var flow = systems.AddComponent<GameFlow>();
            flow.menuCamera = cams.menu;
            flow.playerCamera = cams.player;
            flow.brain = cams.brain;
            flow.player = player.GetComponent<PlayerMotor>();
            flow.shooter = player.GetComponent<PlayerShooter>();
            flow.playerStart = course.playerStart;
            flow.ui = ui;
            flow.graphics = graphics;
            flow.path = course.path;
            ui.flow = flow;
        }

        static AudioClip Clip(string file, bool streaming = false)
        {
            string path = AudioDir + "/" + file;
            var ai = (AudioImporter)AssetImporter.GetAtPath(path);
            if (ai == null) { Debug.LogWarning("[RunAcross] Missing audio " + path + " (run Tools/gen_audio.py)"); return null; }
            var s = ai.defaultSampleSettings;
            var want = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            if (s.loadType != want || ai.forceToMono != true)
            {
                s.loadType = want;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                ai.defaultSampleSettings = s;
                ai.forceToMono = true;
                ai.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static PanelSettings PanelSettingsAsset()
        {
            string path = UiDir + "/PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiDir + "/RunAcrossTheme.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }
    }
}
