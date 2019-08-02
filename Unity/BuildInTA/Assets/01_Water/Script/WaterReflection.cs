using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode, DisallowMultipleComponent]
public class WaterReflection : MonoBehaviour
{
    public static WaterReflection Inst = null;

    public bool m_DisablePixelLights = true;
    public int m_TextureSize = 256;
    public float m_ClipPlaneOffset = 0.07f;

    public LayerMask m_ReflectLayers = -1;
    private Hashtable m_ReflectionCameras = new Hashtable();

    private RenderTexture m_ReflectionTexture = null;
    private int m_OldReflectionTextureSize = 0;

    public bool ignoreOcclusionCulling;

    private Camera reflectionCamera = null;
    private Vector3 oldpos;

    private Material m_material = null;
    private Transform m_mainCamTr = null;
    private Transform m_reflCamTr = null;

    public float disToDisableRefl = 20;

    private void Awake()
    {
        Inst = this;
    }

    void Start()
    {
        
    }

    void Update()
    {
        Camera cam = null;

        if (Application.isPlaying)
        {
            cam = Camera.main;
        }
        else
        {
#if UNITY_EDITOR
            cam = SceneView.lastActiveSceneView.camera;
#else
            cam = Camera.main;
#endif
        }

        if (!cam)
        {
            return;
        }

        if (null == reflectionCamera)
        {
            Camera newCam;
            CreateMirrorObjects(cam, out newCam);
            newCam.depth = -2;

            reflectionCamera = newCam;
        }

        if (null != reflectionCamera)
        {
            if (null == m_mainCamTr)
            {
                m_mainCamTr = cam.transform;
            }

            if (null == m_reflCamTr)
            {
                m_reflCamTr = reflectionCamera.transform;
            }

            Vector3 camPos = m_mainCamTr.position;
            Vector3 reflCamPos = m_reflCamTr.position;

            if (Mathf.Abs(camPos.y - reflCamPos.y) > disToDisableRefl)
            {
                reflectionCamera.enabled = false;
            }
            else if (Time.realtimeSinceStartup > 10)
            {
                //relative to envronment
            }

            if (reflectionCamera.enabled)
            {
                UpdateCam();
            }

            if (!m_ReflectionTexture || m_OldReflectionTextureSize != m_TextureSize)
            {
                if (m_ReflectionTexture)
                    DestroyImmediate(m_ReflectionTexture);
                m_ReflectionTexture = new RenderTexture(m_TextureSize, m_TextureSize, 16);
                m_ReflectionTexture.name = "__MirrorReflection" + GetInstanceID();
                m_ReflectionTexture.isPowerOfTwo = true;
                m_ReflectionTexture.hideFlags = HideFlags.DontSave;

                m_OldReflectionTextureSize = m_TextureSize;

                reflectionCamera.targetTexture = m_ReflectionTexture;
            }
        }
    }

    private void UpdateCam()
    {
        if (!enabled)
        {
            return;
        }

        Camera cam = null;

        if (Application.isPlaying)
        {
            cam = Camera.main;
        }
        else
        {
#if UNITY_EDITOR
            cam = SceneView.lastActiveSceneView.camera;
#else
            cam = Camera.main;
#endif
        }

        if (!cam)
        {
            return;
        }

        if (null == m_material)
        {
            Renderer render = GetComponent<Renderer>();
            if (!render || !render.sharedMaterial)
            {
                return;
            }

            m_material = render.sharedMaterial;
        }

        Vector3 pos = transform.position;
        Vector3 normal = transform.up;

        UpdateCameraModes(cam, reflectionCamera);

        // render reflection
        // reflect camera around refelction plane
        float d = -Vector3.Dot(normal, pos) - m_ClipPlaneOffset;
        Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

        if (ignoreOcclusionCulling)
        {
            reflectionCamera.useOcclusionCulling = false;
        }
        else
        {
            reflectionCamera.useOcclusionCulling = true;
        }

        Matrix4x4 reflection = Matrix4x4.zero;
        CalculateReflectionMatrix(ref reflection, reflectionPlane);
        Vector3 oldpos = cam.transform.position;
        Vector3 newpos = reflection.MultiplyPoint(oldpos);
        reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

        // setup oblique projection matrix so that near plane is our reflection
        // plane. This way we clip everything below/above it for free.
        Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 1.0f);
        Matrix4x4 projection = cam.projectionMatrix;
        CalculateObliqueMatrix(ref projection, clipPlane);
        reflectionCamera.projectionMatrix = projection;

        reflectionCamera.cullingMask = ~(1 << 4) & m_ReflectLayers.value;//never render water layer
        reflectionCamera.targetTexture = m_ReflectionTexture;
        reflectionCamera.transform.position = newpos;
        Vector3 euler = cam.transform.eulerAngles;
        reflectionCamera.transform.eulerAngles = new Vector3(0, euler.y, euler.z);

        if (m_material.HasProperty("_ReflectionTex"))
        {
            m_material.SetTexture("_ReflectionTex", m_ReflectionTexture);
        }

        // Set matrix on the shader that transforms UVs from object space into screen
        // space. We want to just project reflection texture on screen.
        Matrix4x4 scaleOffset = Matrix4x4.TRS(
            new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.5f));
        Vector3 scale = transform.lossyScale;
        Matrix4x4 mtx = transform.localToWorldMatrix * Matrix4x4.Scale(new Vector3(1.0f / scale.x, 1.0f / scale.y, 1.0f / scale.z));
        mtx = scaleOffset * cam.projectionMatrix * cam.worldToCameraMatrix * mtx;
        if (m_material.HasProperty("_ReflectionTex"))
        {
            m_material.SetMatrix("_ProjMatrix", mtx);
        }
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private void OnBeginFrame(ScriptableRenderContext context, Camera[] cameras)
    {
        if (null != reflectionCamera)
        {
            OnBeginCameraRendering(context, reflectionCamera);
        }
    }

    private void OnEndFrame(ScriptableRenderContext context, Camera[] cameras)
    {
        if (null != reflectionCamera)
        {
            OnEndCameraRendering(context, reflectionCamera);
        }
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        Camera cam = Camera.main;
        if (!cam)
        {
            return;
        }

#if UNITY_EDITOR
        if (cam.cameraType == CameraType.Preview)
        {
            return;
        }
        if (cam.cameraType == CameraType.Preview)
        {
            return;
        }
#endif
        if (camera != reflectionCamera)
        {
            return;
        }

        UpdateCam();

        GL.invertCulling = true;
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        Camera cam = Camera.main;
        if (!cam)
        {
            return;
        }

#if UNITY_EDITOR
        if (cam.cameraType == CameraType.Preview)
        {
            return;
        }
        if (cam.cameraType == CameraType.Preview)
        {
            //return;
        }
#endif
        if (camera != reflectionCamera)
        {
            return;
        }

        GL.invertCulling = false;
    }

    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
            return;

        if (src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox sky = src.GetComponent<Skybox>();
            Skybox mysky = dest.GetComponent<Skybox>();
            if (!sky || !sky.material)
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }

        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }

    // Creates any objects needed on demand
    private void CreateMirrorObjects(Camera currentCamera, out Camera outCamera)
    {
        outCamera = null;

        // Camera for reflection
        outCamera = m_ReflectionCameras[currentCamera] as Camera;
        if (!reflectionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
        {
            GameObject go = new GameObject("Mirror_Refl_Cam_" + GetInstanceID(), typeof(Camera), typeof(Skybox));
            outCamera = go.GetComponent<Camera>();

            outCamera.enabled = false;
            outCamera.transform.position = transform.position;
            outCamera.transform.rotation = transform.rotation;
            outCamera.gameObject.AddComponent<FlareLayer>();

            outCamera.clearFlags = CameraClearFlags.Color;
            outCamera.backgroundColor = Color.white;
            Shader shader = Shader.Find("SimpleWater/ReflectionByReplaceShader");
            outCamera.SetReplacementShader(shader, "RenderType");

            go.hideFlags = HideFlags.HideAndDontSave;
            m_ReflectionCameras[currentCamera] = reflectionCamera;
        }
    }

    private static float sgn(float a)
    {
        if (a > 0.0f)
            return 1.0f;
        if (a < 0.0f)
            return -1.0f;
        return 0.0f;
    }

    // Given position/normal of the plane, calculate plane in camera space
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    // Adjust the given projection matrix so that near plane is the given clipPlane
    // clipPlane is given in camera space
    private static void CalculateObliqueMatrix(ref Matrix4x4 projection, Vector4 clipPlane)
    {
        Vector4 q = projection.inverse * new Vector4(
            sgn(clipPlane.x),
            sgn(clipPlane.y),
            1.0f,
            1.0f);
        Vector4 c = clipPlane * (2.0f / Vector4.Dot(clipPlane, q));
        projection[2] = c.x - projection[3];
        projection[6] = c.y - projection[7];
        projection[10] = c.z - projection[11];
        projection[14] = c.w - projection[15];
    }

    // Calculates reflection matrix around the given plane
    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1f - 2f * plane[0] * plane[0]);
        reflectionMat.m01 = (-2f * plane[0] * plane[1]);
        reflectionMat.m02 = (-2f * plane[0] * plane[2]);
        reflectionMat.m03 = (-2f * plane[0] * plane[3]);

        reflectionMat.m10 = (-2f * plane[1] * plane[0]);
        reflectionMat.m11 = (1f - 2f * plane[1] * plane[1]);
        reflectionMat.m12 = (-2f * plane[1] * plane[2]);
        reflectionMat.m13 = (-2f * plane[1] * plane[3]);

        reflectionMat.m20 = (-2f * plane[2] * plane[0]);
        reflectionMat.m21 = (-2f * plane[2] * plane[1]);
        reflectionMat.m22 = (1f - 2f * plane[2] * plane[2]);
        reflectionMat.m23 = (-2f * plane[2] * plane[3]);

        reflectionMat.m30 = 0f;
        reflectionMat.m31 = 0f;
        reflectionMat.m32 = 0f;
        reflectionMat.m33 = 1f;
    }

    void OnDestroy()
    {
        Inst = null;
    }
}
