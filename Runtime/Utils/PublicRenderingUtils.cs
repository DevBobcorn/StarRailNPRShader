using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace HSR.NPRShader.Utils
{
    public class PublicRenderingUtils
    {
        private static readonly ShaderTagId[] s_ShaderTagValues = new ShaderTagId[1];
        private static readonly RenderStateBlock[] s_RenderStateBlocks = new RenderStateBlock[1];

        /// <summary>
        /// Taken from <c>UnityEngine.Rendering.Universal.RenderingUtils</c>
        /// (com.unity.render-pipelines.universal/Runtime/RenderingUtils.cs)
        /// <br/>
        /// Create a RendererList using a RenderStateBlock override is quite common so we have this optimized utility function for it
        /// </summary>
        internal static void CreateRendererListWithRenderStateBlock(RenderGraph renderGraph, ref CullingResults cullResults, DrawingSettings ds, FilteringSettings fs, RenderStateBlock rsb, ref RendererListHandle rl)
        {
            s_ShaderTagValues[0] = ShaderTagId.none;
            s_RenderStateBlocks[0] = rsb;
            NativeArray<ShaderTagId> tagValues = new NativeArray<ShaderTagId>(s_ShaderTagValues, Allocator.Temp);
            NativeArray<RenderStateBlock> stateBlocks = new NativeArray<RenderStateBlock>(s_RenderStateBlocks, Allocator.Temp);
            var param = new RendererListParams(cullResults, ds, fs)
            {
                tagValues = tagValues,
                stateBlocks = stateBlocks,
                isPassTagName = false
            };
            rl = renderGraph.CreateRendererList(param);
        }
        
        public static void DrawRendererListObjectsWithError(RasterCommandBuffer cmd, ref RendererList rl)
        {
            cmd.DrawRendererList(rl);
        }
    }
}