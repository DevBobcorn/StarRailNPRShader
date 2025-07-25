using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Create a Scriptable Renderer Feature that implements a post-processing effect when the camera is inside a custom volume.
// For more information about creating scriptable renderer features, refer to https://docs.unity3d.com/Manual/urp/customizing-urp.html
public sealed class SamplePostProcessEffectRendererFeature : ScriptableRendererFeature
{
    #region FEATURE_FIELDS

    // Declare the material used to render the post-processing effect.
    // Add a [SerializeField] attribute so Unity serializes the property and includes it in builds.
    [SerializeField]
    [HideInInspector]
    private Material m_Material;

    // Declare the render pass that renders the effect.
    private CustomPostRenderPass m_FullScreenPass;

    #endregion

    #region FEATURE_METHODS

    // Override the Create method.
    // Unity calls this method when the Scriptable Renderer Feature loads for the first time, and when you change a property.
    public override void Create()
    {
#if UNITY_EDITOR
        // Assign a material asset to m_Material in the Unity Editor.
        if (m_Material == null)
            m_Material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/FullscreenInvertColors.mat");
#endif

        if(m_Material)
            m_FullScreenPass = new CustomPostRenderPass(name, m_Material);
    }

    // Override the AddRenderPasses method to inject passes into the renderer. Unity calls AddRenderPasses once per camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Skip rendering if m_Material or the pass instance are null.
        if (m_Material == null || m_FullScreenPass == null)
            return;

        // Skip rendering if the target is a Reflection Probe or a preview camera.
        if (renderingData.cameraData.cameraType == CameraType.Preview || renderingData.cameraData.cameraType == CameraType.Reflection)
            return;

        // Skip rendering if the camera is outside the custom volume.
        SamplePostProcessEffectVolumeComponent myVolume = VolumeManager.instance.stack?.GetComponent<SamplePostProcessEffectVolumeComponent>();
        if (myVolume == null || !myVolume.IsActive())
            return;

        // Specify when the effect will execute during the frame.
        // For a post-processing effect, the injection point is usually BeforeRenderingTransparents, BeforeRenderingPostProcessing, or AfterRenderingPostProcessing.
        // For more information, refer to https://docs.unity3d.com/Manual/urp/customize/custom-pass-injection-points.html 
        m_FullScreenPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        // Specify that the effect doesn't need scene depth, normals, motion vectors, or the color texture as input.
        m_FullScreenPass.ConfigureInput(ScriptableRenderPassInput.None);

        // Add the render pass to the renderer.
        renderer.EnqueuePass(m_FullScreenPass);
    }

    protected override void Dispose(bool disposing)
    {
        // Free the resources the render pass uses.
        m_FullScreenPass.Dispose();
    }

    #endregion

    // Create the custom render pass.
    private class CustomPostRenderPass : ScriptableRenderPass
    {
        #region PASS_FIELDS

        // Declare the material used to render the post-processing effect.
        private Material m_Material;

        // Declare a texture to use as a temporary color copy. This texture is used only in the Compatibility Mode path.
        private RTHandle m_CopiedColor;

        // Declare a property block to set additional properties for the material.
        private static MaterialPropertyBlock s_SharedPropertyBlock = new MaterialPropertyBlock();

        // Declare a property that enables or disables the render pass that samples the color texture.
        private static readonly bool kSampleActiveColor = true;

        // Declare a property that adds or removes depth-stencil support.
        private static readonly bool kBindDepthStencilAttachment = false;

        // Create shader properties in advance, which is more efficient than referencing them by string.
        private static readonly int kBlitTexturePropertyId = Shader.PropertyToID("_BlitTexture");
        private static readonly int kBlitScaleBiasPropertyId = Shader.PropertyToID("_BlitScaleBias");

        #endregion

        public CustomPostRenderPass(string passName, Material material)
        {
            // Add a profiling sampler.
            profilingSampler = new ProfilingSampler(passName);

            // Assign the material to the render pass.
            m_Material = material;

            // To make sure the render pass can sample the active color buffer, set URP to render to intermediate textures instead of directly to the backbuffer.
            requiresIntermediateTexture = kSampleActiveColor;
        }

        #region PASS_SHARED_RENDERING_CODE

        // Add a command to create the temporary color copy texture.
        // This method is used in both the render graph system path and the Compatibility Mode path.
        private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sourceTexture)
        {
            Blitter.BlitTexture(cmd, sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
        }

        // Add commands to render the effect.
        // This method is used in both the render graph system path and the Compatibility Mode path.
        private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle sourceTexture, Material material)
        {
            // Clear the material properties.
            s_SharedPropertyBlock.Clear();
            if(sourceTexture != null)
                s_SharedPropertyBlock.SetTexture(kBlitTexturePropertyId, sourceTexture);

            // Set the scale and bias so shaders that use Blit.hlsl work correctly.
            s_SharedPropertyBlock.SetVector(kBlitScaleBiasPropertyId, new Vector4(1, 1, 0, 0));

            // Set the material properties based on the blended values of the custom volume.
            // For more information, refer to https://docs.unity3d.com/Manual/urp/post-processing/custom-post-processing-with-volume.html
            SamplePostProcessEffectVolumeComponent myVolume = VolumeManager.instance.stack?.GetComponent<SamplePostProcessEffectVolumeComponent>();
            if (myVolume != null)
                s_SharedPropertyBlock.SetFloat("_Intensity", myVolume.intensity.value);

            // Draw to the current render target.
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
        }

        // Get the texture descriptor needed to create the temporary color copy texture.
        // This method is used in both the render graph system path and the Compatibility Mode path.
        private static RenderTextureDescriptor GetCopyPassTextureDescriptor(RenderTextureDescriptor desc)
        {
            // Avoid an unnecessary multisample anti-aliasing (MSAA) resolve before the main render pass.
            desc.msaaSamples = 1;

            // Avoid copying the depth buffer, as the main pass render in this example doesn't use depth.
            desc.depthBufferBits = (int)DepthBits.None;

            return desc;
        }

        #endregion

        #region PASS_NON_RENDER_GRAPH_PATH

        // Override the OnCameraSetup method to configure render targets and their clear states, and create temporary render target textures.
        // Unity calls this method before executing the render pass.
        // This method is used only in the Compatibility Mode path.
        // Use ConfigureTarget or ConfigureClear in this method. Don't use CommandBuffer.SetRenderTarget.
        [System.Obsolete("This rendering path works in Compatibility Mode only, which is deprecated. Use the render graph API instead.", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // Reset the render target to default.
            ResetTarget();

            // Allocate a temporary texture, and reallocate it if there's a change to camera settings, for example resolution.
            if (kSampleActiveColor)
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_CopiedColor, GetCopyPassTextureDescriptor(renderingData.cameraData.cameraTargetDescriptor), name: "_CustomPostPassCopyColor");
        }

        // Override the Execute method to implement the rendering logic. Use ScriptableRenderContext to issue drawing commands or execute command buffers.
        // You don't need to call ScriptableRenderContext.Submit.
        // This method is used only in the Compatibility Mode path.
        [System.Obsolete("This rendering path works in Compatibility Mode only, which is deprecated. Use the render graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            // Get the camera data and command buffer.
            ref var cameraData = ref renderingData.cameraData;
            var cmd = CommandBufferPool.Get();

            // Add a profiling sampler.
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Create a command buffer to execute the render pass.
                RasterCommandBuffer rasterCmd = CommandBufferHelpers.GetRasterCommandBuffer(cmd);
                if (kSampleActiveColor)
                {
                    CoreUtils.SetRenderTarget(cmd, m_CopiedColor);
                    ExecuteCopyColorPass(rasterCmd, cameraData.renderer.cameraColorTargetHandle);
                }

                // Set the render target based on the depth-stencil attachment.
                if(kBindDepthStencilAttachment)
                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle, cameraData.renderer.cameraDepthTargetHandle);
                else
                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle);

                // Execute the main render pass.
                ExecuteMainPass(rasterCmd, kSampleActiveColor ? m_CopiedColor : null, m_Material);
            }

            // Execute the command buffer.
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            // Release the command buffer.
            CommandBufferPool.Release(cmd);
        }

        // Free the resources the camera uses.
        // This method is used only in the Compatibility Mode path.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }

        // Free the resources the texture uses.
        // This method is used only in the Compatibility Mode path.
        public void Dispose()
        {
            m_CopiedColor?.Release();
        }

        #endregion

        #region PASS_RENDER_GRAPH_PATH

        // Declare the resource the copy render pass uses.
        // This method is used only in the render graph system path.
        private class CopyPassData
        {
            public TextureHandle inputTexture;
        }

        // Declare the resources the main render pass uses.
        // This method is used only in the render graph system path.
        private class MainPassData
        {
            public Material material;
            public TextureHandle inputTexture;
        }

        private static void ExecuteCopyColorPass(CopyPassData data, RasterGraphContext context)
        {
            ExecuteCopyColorPass(context.cmd, data.inputTexture);
        }

        private static void ExecuteMainPass(MainPassData data, RasterGraphContext context)
        {
            ExecuteMainPass(context.cmd, data.inputTexture.IsValid() ? data.inputTexture : null, data.material);
        }

        // Override the RecordRenderGraph method to implement the rendering logic.
        // This method is used only in the render graph system path.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {

            // Get the resources the pass uses.
            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // Sample from the current color texture.
            using (var builder = renderGraph.AddRasterRenderPass<MainPassData>(passName, out var passData, profilingSampler))
            {
                passData.material = m_Material;

                TextureHandle destination;

                // Copy cameraColor to a temporary texture, if the kSampleActiveColor property is set to true. 
                if (kSampleActiveColor)
                {
                    var cameraColorDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                    cameraColorDesc.name = "_CameraColorCustomPostProcessing";
                    cameraColorDesc.clearBuffer = false;

                    destination = renderGraph.CreateTexture(cameraColorDesc);
                    passData.inputTexture = resourcesData.cameraColor;

                    // If you use framebuffer fetch in your material, use builder.SetInputAttachment to reduce GPU bandwidth usage and power consumption. 
                    builder.UseTexture(passData.inputTexture, AccessFlags.Read);
                }
                else
                {
                    destination = resourcesData.cameraColor;
                    passData.inputTexture = TextureHandle.nullHandle;
                }

                
                // Set the render graph to render to the temporary texture.
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                // Bind the depth-stencil buffer.
                // This is a demonstration. The code isn't used in the example.
                if (kBindDepthStencilAttachment)
                    builder.SetRenderAttachmentDepth(resourcesData.activeDepthTexture, AccessFlags.Write);

                // Set the render method.
                builder.SetRenderFunc((MainPassData data, RasterGraphContext context) => ExecuteMainPass(data, context));

                // Set cameraColor to the new temporary texture so the next render pass can use it. You don't need to blit to and from cameraColor if you use the render graph system.
                if (kSampleActiveColor)
                {
                    resourcesData.cameraColor = destination;
                }
            }
        }

        #endregion
    }
}
