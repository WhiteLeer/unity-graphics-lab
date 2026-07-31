using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class SSPRRenderPass : ScriptableRenderPass
{
    private Material m_Material;
    private SSPRRenderFeature.Settings m_Settings;
    private ScriptableRenderer m_Renderer;
    private ComputeShader m_PPRCompute;

    private int m_TempRT = Shader.PropertyToID("_SSPR_TempRT");
    private int m_TempColorRT = Shader.PropertyToID("_SSPR_ColorCopy");
    private int m_PPRDepthRT = Shader.PropertyToID("_SSPR_PPRDepthRT");
    private int m_PPROffsetRT = Shader.PropertyToID("_SSPR_PPROffsetRT");
    private Vector4 m_DebugFlags = Vector4.zero;

    private ProfilingSampler m_Sampler = new ProfilingSampler("SSPR");

    public Material Material => m_Material;

    public SSPRRenderPass(Material material, SSPRRenderFeature.Settings settings, ComputeShader pprCompute)
    {
        m_Material = material;
        m_Settings = settings;
        m_PPRCompute = pprCompute;
    }

    public void Setup(ScriptableRenderer renderer)
    {
        m_Renderer = renderer;
        // 需要深度、法线和相机不透明纹理
        ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color);
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        if (m_Material == null)
            return;
        Camera camera = renderingData.cameraData.camera;

        // 设置SSPR参数
        m_Material.SetVector(Shader.PropertyToID("_SSPRParams"), new Vector4(
            m_Settings.intensity,
            m_Settings.fresnelPower,
            m_Settings.fadeStart,
            m_Settings.fadeEnd
        ));
        m_Material.SetVector(Shader.PropertyToID("_SSPRParams2"), new Vector4(
            m_Settings.normalThreshold,
            m_Settings.maxPlaneDistance,
            m_Settings.holeFillRadius,
            m_Settings.enableHoleFill ? 1.0f : 0.0f
        ));
        m_Material.SetVector(Shader.PropertyToID("_SSPRParams3"), new Vector4(
            m_Settings.roughness,
            m_Settings.maxBlurPixels,
            0.0f,
            0.0f
        ));
        m_Material.SetVector(Shader.PropertyToID("_SSPRReflectorCenter"), m_Settings.reflectorCenter);
        m_Material.SetVector(Shader.PropertyToID("_SSPRReflectorHalfSize"),
            new Vector4(m_Settings.reflectorSize.x * 0.5f, m_Settings.reflectorSize.y * 0.5f, 0, 0));
        m_Material.SetFloat(Shader.PropertyToID("_SSPRUseReflectorBounds"), m_Settings.enableReflectorBounds ? 1.0f : 0.0f);
        m_Material.SetFloat(Shader.PropertyToID("_SSPROcclusionThickness"), m_Settings.occlusionThickness);
        m_Material.SetFloat(Shader.PropertyToID("_SSPRDepthConsistency"), m_Settings.depthConsistency);
        m_DebugFlags = m_Settings.enableDebugVisualization
            ? new Vector4(
                m_Settings.debugBypassNormalFilter ? 1.0f : 0.0f,
                m_Settings.debugBypassDepthConsistency ? 1.0f : 0.0f,
                m_Settings.debugBypassComputeOcclusion ? 1.0f : 0.0f,
                0.0f)
            : Vector4.zero;
        m_Material.SetVector(Shader.PropertyToID("_SSPRDebugFlags"), m_DebugFlags);

        // 设置反射平面
        m_Material.SetVector(Shader.PropertyToID("_ReflectionPlane"), new Vector4(
            m_Settings.planeNormal.x,
            m_Settings.planeNormal.y,
            m_Settings.planeNormal.z,
            m_Settings.planeDistance
        ));

        // 设置世界空间向量重建参数（复用SSAO的方法）
        SetupWorldSpaceVectorParams(camera);

        // Set view/proj matrices for shader (match compute)
        Matrix4x4 view = camera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        Matrix4x4 viewProj = proj * view;
        Matrix4x4 invViewProj = viewProj.inverse;
        m_Material.SetMatrix(Shader.PropertyToID("_SSPRViewProj"), viewProj);
        m_Material.SetMatrix(Shader.PropertyToID("_SSPRInvViewProj"), invViewProj);
        m_Material.SetMatrix(Shader.PropertyToID("_SSPRView"), view);
    }

    private void SetupWorldSpaceVectorParams(Camera camera)
    {
        Matrix4x4 view = camera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);

        // 计算cview（移除平移的View矩阵）
        Matrix4x4 cview = view;
        cview.SetColumn(3, new Vector4(0, 0, 0, 1));

        // 计算View空间的frustum corners
        float tanHalfFOV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = camera.aspect;

        Vector3 topLeftView = new Vector3(-tanHalfFOV * aspect, tanHalfFOV, -1);
        Vector3 topRightView = new Vector3(tanHalfFOV * aspect, tanHalfFOV, -1);
        Vector3 bottomLeftView = new Vector3(-tanHalfFOV * aspect, -tanHalfFOV, -1);

        // 转换到世界空间
        Matrix4x4 cviewInv = cview.inverse;
        Vector3 topLeft = cviewInv.MultiplyPoint3x4(topLeftView);
        Vector3 topRight = cviewInv.MultiplyPoint3x4(topRightView);
        Vector3 bottomLeft = cviewInv.MultiplyPoint3x4(bottomLeftView);

        Vector4 xExtent = topRight - topLeft;
        Vector4 yExtent = bottomLeft - topLeft;

        m_Material.SetVector(Shader.PropertyToID("_CameraViewTopLeftCorner"), topLeft);
        m_Material.SetVector(Shader.PropertyToID("_CameraViewXExtent"), xExtent);
        m_Material.SetVector(Shader.PropertyToID("_CameraViewYExtent"), yExtent);
        m_Material.SetVector(Shader.PropertyToID("_ProjectionParams2"), new Vector4(1.0f / camera.nearClipPlane, 0, 0, 0));
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (m_Material == null)
        {
            Debug.LogWarning("[SSPR] Material is null!");
            return;
        }

        if (m_PPRCompute == null)
        {
            Debug.LogWarning("[SSPR] SSPR compute shader is null. Assign it in the Render Feature settings.");
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get("SSPR");

        using (new ProfilingScope(cmd, m_Sampler))
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;

            // PPR buffers (uint)
            RenderTextureDescriptor uintDesc = desc;
            uintDesc.graphicsFormat = GraphicsFormat.R32_UInt;
            uintDesc.enableRandomWrite = true;

            // 分配两个临时RT
            // 1. 用于存储原始场景颜色的副本
            cmd.GetTemporaryRT(m_TempColorRT, desc, FilterMode.Bilinear);
            // 2. 用于SSPR处理结果
            cmd.GetTemporaryRT(m_TempRT, desc, FilterMode.Bilinear);
            // 3. PPR Depth/Offset
            cmd.GetTemporaryRT(m_PPRDepthRT, uintDesc, FilterMode.Point);
            cmd.GetTemporaryRT(m_PPROffsetRT, uintDesc, FilterMode.Point);

            // 先复制当前场景颜色到临时RT（保存原始场景）
            Blit(cmd, m_Renderer.cameraColorTarget, m_TempColorRT);

            // 手动设置_BaseMap纹理（关键！）
            cmd.SetGlobalTexture(Shader.PropertyToID("_BaseMap"), m_TempColorRT);

            // === PPR Compute ===
            Camera camera = renderingData.cameraData.camera;
            if (!m_PPRCompute.HasKernel("CSClear") || !m_PPRCompute.HasKernel("CSProject"))
            {
                Debug.LogError("[SSPR] Compute shader kernels not found. Please reassign the SSPR compute shader.");
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }
            int kernelClear = m_PPRCompute.FindKernel("CSClear");
            int kernelProject = m_PPRCompute.FindKernel("CSProject");
            try
            {
                uint tx, ty, tz;
                m_PPRCompute.GetKernelThreadGroupSizes(kernelClear, out tx, out ty, out tz);
                m_PPRCompute.GetKernelThreadGroupSizes(kernelProject, out tx, out ty, out tz);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SSPR] Kernel validation failed for compute shader '{m_PPRCompute.name}'. Please reassign the compute asset. {e.GetType().Name}: {e.Message}");
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            int width = desc.width;
            int height = desc.height;
            Vector4 screenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);

            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            Matrix4x4 viewProj = proj * view;
            Matrix4x4 invViewProj = viewProj.inverse;

            // Support both legacy (_PPR*) and new (_SSPR*) parameter names
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRScreenSize"), screenSize);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_PPRScreenSize"), screenSize);
            cmd.SetComputeMatrixParam(m_PPRCompute, Shader.PropertyToID("_SSPRViewProj"), viewProj);
            cmd.SetComputeMatrixParam(m_PPRCompute, Shader.PropertyToID("_PPRViewProj"), viewProj);
            cmd.SetComputeMatrixParam(m_PPRCompute, Shader.PropertyToID("_SSPRInvViewProj"), invViewProj);
            cmd.SetComputeMatrixParam(m_PPRCompute, Shader.PropertyToID("_PPRInvViewProj"), invViewProj);
            cmd.SetComputeMatrixParam(m_PPRCompute, Shader.PropertyToID("_SSPRView"), view);
            cmd.SetComputeMatrixParam(m_PPRCompute, Shader.PropertyToID("_PPRView"), view);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_ReflectionPlane"),
                new Vector4(m_Settings.planeNormal.x, m_Settings.planeNormal.y, m_Settings.planeNormal.z, m_Settings.planeDistance));
            cmd.SetComputeFloatParam(m_PPRCompute, Shader.PropertyToID("_SSPRMaxPlaneDistance"), m_Settings.maxPlaneDistance);

            // Reflector bounds basis
            BuildPlaneBasis(m_Settings.planeNormal, out Vector3 axisU, out Vector3 axisV);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRReflectorCenter"), m_Settings.reflectorCenter);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRReflectorU"), axisU);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRReflectorV"), axisV);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRReflectorHalfSize"),
                new Vector4(m_Settings.reflectorSize.x * 0.5f, m_Settings.reflectorSize.y * 0.5f, 0, 0));
            cmd.SetComputeFloatParam(m_PPRCompute, Shader.PropertyToID("_SSPRUseReflectorBounds"), m_Settings.enableReflectorBounds ? 1.0f : 0.0f);
            cmd.SetComputeFloatParam(m_PPRCompute, Shader.PropertyToID("_SSPROcclusionThickness"), m_Settings.occlusionThickness);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRDebugFlags"), m_DebugFlags);

            // Match SSAO-style reconstruction parameters
            ComputeReconstructParams(camera, out Vector4 topLeft, out Vector4 xExtent, out Vector4 yExtent, out Vector4 projParams2);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_CameraViewTopLeftCorner"), topLeft);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_CameraViewXExtent"), xExtent);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_CameraViewYExtent"), yExtent);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_ProjectionParams2"), projParams2);
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_ZBufferParams"), Shader.GetGlobalVector("_ZBufferParams"));
            cmd.SetComputeVectorParam(m_PPRCompute, Shader.PropertyToID("_SSPRCameraPos"),
                new Vector4(camera.transform.position.x, camera.transform.position.y, camera.transform.position.z, 1.0f));

            cmd.SetComputeTextureParam(m_PPRCompute, kernelClear, Shader.PropertyToID("_SSPRDepth"), m_PPRDepthRT);
            cmd.SetComputeTextureParam(m_PPRCompute, kernelClear, Shader.PropertyToID("_PPRDepth"), m_PPRDepthRT);
            cmd.SetComputeTextureParam(m_PPRCompute, kernelClear, Shader.PropertyToID("_SSPROffset"), m_PPROffsetRT);
            cmd.SetComputeTextureParam(m_PPRCompute, kernelClear, Shader.PropertyToID("_PPROffset"), m_PPROffsetRT);
            try
            {
                Dispatch(cmd, m_PPRCompute, kernelClear, width, height);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SSPR] Dispatch CSClear failed. clear={kernelClear}, project={kernelProject}, shader={m_PPRCompute.name}. {e.GetType().Name}: {e.Message}");
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            cmd.SetComputeTextureParam(m_PPRCompute, kernelProject, Shader.PropertyToID("_CameraDepthTexture"),
                Shader.PropertyToID("_CameraDepthTexture"));
            cmd.SetComputeTextureParam(m_PPRCompute, kernelProject, Shader.PropertyToID("_SSPRDepth"), m_PPRDepthRT);
            cmd.SetComputeTextureParam(m_PPRCompute, kernelProject, Shader.PropertyToID("_PPRDepth"), m_PPRDepthRT);
            cmd.SetComputeTextureParam(m_PPRCompute, kernelProject, Shader.PropertyToID("_SSPROffset"), m_PPROffsetRT);
            cmd.SetComputeTextureParam(m_PPRCompute, kernelProject, Shader.PropertyToID("_PPROffset"), m_PPROffsetRT);
            try
            {
                Dispatch(cmd, m_PPRCompute, kernelProject, width, height);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SSPR] Dispatch CSProject failed. clear={kernelClear}, project={kernelProject}, shader={m_PPRCompute.name}. {e.GetType().Name}: {e.Message}");
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
                return;
            }

            cmd.SetGlobalTexture(Shader.PropertyToID("_SSPR_Offset"), m_PPROffsetRT);
            cmd.SetGlobalTexture(Shader.PropertyToID("_SSPR_Depth"), m_PPRDepthRT);
            cmd.SetGlobalVector(Shader.PropertyToID("_SSPRScreenSize"), screenSize);

            // 应用SSPR效果
            // 输入：m_TempColorRT（原始场景）
            // 输出：m_TempRT（处理后）
            Blit(cmd, m_TempColorRT, m_TempRT, m_Material, 0);

            // 将处理结果Blit回原target
            Blit(cmd, m_TempRT, m_Renderer.cameraColorTarget);

            // 释放临时RT
            cmd.ReleaseTemporaryRT(m_TempColorRT);
            cmd.ReleaseTemporaryRT(m_TempRT);
            cmd.ReleaseTemporaryRT(m_PPRDepthRT);
            cmd.ReleaseTemporaryRT(m_PPROffsetRT);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    private static void Dispatch(CommandBuffer cmd, ComputeShader cs, int kernel, int width, int height)
    {
        uint x, y, z;
        cs.GetKernelThreadGroupSizes(kernel, out x, out y, out z);
        int tx = Mathf.CeilToInt(width / (float)x);
        int ty = Mathf.CeilToInt(height / (float)y);
        cmd.DispatchCompute(cs, kernel, tx, ty, 1);
    }

    private static void ComputeReconstructParams(Camera camera, out Vector4 topLeft, out Vector4 xExtent, out Vector4 yExtent, out Vector4 projParams2)
    {
        Matrix4x4 view = camera.worldToCameraMatrix;

        Matrix4x4 cview = view;
        cview.SetColumn(3, new Vector4(0, 0, 0, 1));

        float tanHalfFOV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = camera.aspect;

        Vector3 topLeftView = new Vector3(-tanHalfFOV * aspect, tanHalfFOV, -1);
        Vector3 topRightView = new Vector3(tanHalfFOV * aspect, tanHalfFOV, -1);
        Vector3 bottomLeftView = new Vector3(-tanHalfFOV * aspect, -tanHalfFOV, -1);

        Matrix4x4 cviewInv = cview.inverse;
        Vector3 topLeftWS = cviewInv.MultiplyPoint3x4(topLeftView);
        Vector3 topRightWS = cviewInv.MultiplyPoint3x4(topRightView);
        Vector3 bottomLeftWS = cviewInv.MultiplyPoint3x4(bottomLeftView);

        xExtent = topRightWS - topLeftWS;
        yExtent = bottomLeftWS - topLeftWS;
        topLeft = topLeftWS;
        projParams2 = new Vector4(1.0f / camera.nearClipPlane, 0, 0, 0);
    }

    private static void BuildPlaneBasis(Vector3 normal, out Vector3 axisU, out Vector3 axisV)
    {
        Vector3 n = normal.normalized;
        Vector3 up = Mathf.Abs(n.y) < 0.999f ? Vector3.up : Vector3.right;
        axisU = Vector3.Normalize(Vector3.Cross(up, n));
        axisV = Vector3.Normalize(Vector3.Cross(n, axisU));
    }
}
