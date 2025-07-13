using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HSR.NPRShader.Utils
{
    public static class PublicRenderGraphUtils
    {
        private const int DBufferSize = 3;
        
        /// <summary>
        /// Taken from <c>UnityEngine.Rendering.Universal.RenderGraphUtils</c>
        /// (com.unity.render-pipelines.universal/Runtime/UniversalRendererRenderGraph.cs)
        /// </summary>
        public static void UseDBufferIfValid(IRasterRenderGraphBuilder builder, UniversalResourceData resourceData)
        {
            TextureHandle[] dbufferHandles = resourceData.dBuffer;
            for (int i = 0; i < DBufferSize; ++i)
            {
                TextureHandle dbuffer = dbufferHandles[i];
                if (dbuffer.IsValid())
                    builder.UseTexture(dbuffer);
            }
        }

    }
}