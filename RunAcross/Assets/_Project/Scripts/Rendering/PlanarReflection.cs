using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RunAcross.Rendering
{
    /// <summary>
    /// Renders the scene mirrored across this object's surface into a global HDR texture
    /// (_PlanarReflectionTex) that RunAcross/NeonWater samples in screen space.
    /// URP has no screen-space reflections, and this is what gives the wet floor its sharp neon mirror.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class PlanarReflection : MonoBehaviour
    {
        [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
        public LayerMask reflectLayers = ~0;
        [Tooltip("Pushes the clip plane slightly below the surface so objects touching the water don't leak.")]
        public float clipPlaneOffset = 0.03f;

        static readonly int ReflectionTexId = Shader.PropertyToID("_PlanarReflectionTex");

        Camera _reflectionCamera;
        RenderTexture _texture;
        Renderer _renderer;

        void OnEnable()
        {
            _renderer = GetComponent<Renderer>();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (_reflectionCamera) DestroyImmediate(_reflectionCamera.gameObject);
            if (_texture) { _texture.Release(); DestroyImmediate(_texture); }
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == _reflectionCamera || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return;

            EnsureResources(cam);

            Vector3 planePos = new Vector3(0f, _renderer.bounds.center.y, 0f);
            Vector3 normal = Vector3.up;

            _reflectionCamera.CopyFrom(cam);
            _reflectionCamera.cullingMask = reflectLayers;
            _reflectionCamera.targetTexture = _texture;
            _reflectionCamera.cameraType = CameraType.Reflection;
            _reflectionCamera.clearFlags = cam.clearFlags;

            float d = -Vector3.Dot(normal, planePos) - clipPlaneOffset;
            Matrix4x4 reflection = ReflectionMatrix(new Vector4(normal.x, normal.y, normal.z, d));
            _reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

            Vector4 clipPlane = CameraSpacePlane(_reflectionCamera, planePos, normal);
            _reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);
            _reflectionCamera.transform.SetPositionAndRotation(reflection.MultiplyPoint(cam.transform.position), cam.transform.rotation);

            GL.invertCulling = true;
#pragma warning disable CS0618 // SubmitRenderRequest can't run inside another camera's render; RenderSingleCamera can.
            UniversalRenderPipeline.RenderSingleCamera(context, _reflectionCamera);
#pragma warning restore CS0618
            GL.invertCulling = false;

            Shader.SetGlobalTexture(ReflectionTexId, _texture);
        }

        void EnsureResources(Camera cam)
        {
            int w = Mathf.Max(64, (int)(cam.pixelWidth * resolutionScale));
            int h = Mathf.Max(64, (int)(cam.pixelHeight * resolutionScale));
            if (_texture == null || _texture.width != w || _texture.height != h)
            {
                if (_texture) { _texture.Release(); DestroyImmediate(_texture); }
                _texture = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "PlanarReflection", hideFlags = HideFlags.HideAndDontSave, useMipMap = false
                };
            }

            if (_reflectionCamera == null)
            {
                var go = new GameObject("PlanarReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
                _reflectionCamera = go.AddComponent<Camera>();
                _reflectionCamera.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.requiresDepthOption = CameraOverrideOption.Off;
                data.requiresColorOption = CameraOverrideOption.Off;
            }
        }

        static Matrix4x4 ReflectionMatrix(Vector4 p)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y; m.m02 = -2f * p.x * p.z; m.m03 = -2f * p.w * p.x;
            m.m10 = -2f * p.y * p.x; m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z; m.m13 = -2f * p.w * p.y;
            m.m20 = -2f * p.z * p.x; m.m21 = -2f * p.z * p.y; m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
            return m;
        }

        Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal)
        {
            Vector3 offsetPos = pos + normal * clipPlaneOffset;
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cpos = m.MultiplyPoint(offsetPos);
            Vector3 cnormal = m.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }
    }
}
