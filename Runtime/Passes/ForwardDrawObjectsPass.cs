/*
 * StarRailNPRShader - Fan-made shaders for Unity URP attempting to replicate
 * the shading of Honkai: Star Rail.
 * https://github.com/stalomeow/StarRailNPRShader
 *
 * Copyright (C) 2023 Stalo <stalowork@163.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using HSR.NPRShader.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#if STARRAIL_URP_COMPATIBILITY_MODE
using UnityEngine.Rendering.Universal.Internal;
#endif

namespace HSR.NPRShader.Passes
{
#if STARRAIL_URP_COMPATIBILITY_MODE
    public class ForwardDrawObjectsPass : DrawObjectsPass
    {
        public ForwardDrawObjectsPass(string profilerTag, bool isOpaque, params ShaderTagId[] shaderTagIds)
            : this(profilerTag, isOpaque,
                // 放在最后绘制，这样就不需要清理被挡住的角色的 Stencil
                isOpaque ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.AfterRenderingTransparents,
                shaderTagIds) { }

        public ForwardDrawObjectsPass(string profilerTag, bool isOpaque, RenderPassEvent evt, params ShaderTagId[] shaderTagIds)
            : this(profilerTag, isOpaque, -1, evt, shaderTagIds) { }

        public ForwardDrawObjectsPass(string profilerTag, bool isOpaque, LayerMask layerMask, RenderPassEvent evt, params ShaderTagId[] shaderTagIds)
            : base(profilerTag, shaderTagIds, isOpaque, evt,
                isOpaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
                layerMask, new StencilState(), 0) { }
    }
#else
    // RG 版的 DrawObjectsPass 没有重写 RecordRenderGraph 方法（而是由 UniversalRenderer 直接
    // 调用其 Render 方法完成绘制），而且其 Render 方法外部不可访问，所以直接继承不好使了
    public class ForwardDrawObjectsPass : ScriptableRenderPass
    {
        public ForwardDrawObjectsPass(string profilerTag, bool isOpaque, params ShaderTagId[] shaderTagIds)
            : this(profilerTag, isOpaque,
                // 放在最后绘制，这样就不需要清理被挡住的角色的 Stencil
                isOpaque ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.AfterRenderingTransparents,
                shaderTagIds) { }

        public ForwardDrawObjectsPass(string profilerTag, bool isOpaque, RenderPassEvent evt, params ShaderTagId[] shaderTagIds)
            : this(profilerTag, isOpaque, -1, evt, shaderTagIds) { }

        public ForwardDrawObjectsPass(string profilerTag, bool isOpaque, LayerMask layerMask, RenderPassEvent evt, params ShaderTagId[] shaderTagIds)
            : this(profilerTag, shaderTagIds, isOpaque, evt,
                isOpaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
                layerMask, new StencilState(), 0) { }
        
        private FilteringSettings m_FilteringSettings;
        private RenderStateBlock m_RenderStateBlock;
        private readonly List<ShaderTagId> m_ShaderTagIdList = new();

        private bool m_IsOpaque;

        /// <summary>
        /// Used to indicate whether transparent objects should receive shadows or not.
        /// </summary>
        public bool m_ShouldTransparentsReceiveShadows;

        private static readonly int s_DrawObjectPassDataPropID = Shader.PropertyToID("_DrawObjectPassData");

        /// <summary>
        /// Creates a new <c>ForwardDrawObjectsPass</c> instance.
        /// </summary>
        /// <param name="profilerTag">The profiler tag used with the pass.</param>
        /// <param name="shaderTagIds"></param>
        /// <param name="opaque">Marks whether the objects are opaque or transparent.</param>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="renderQueueRange">The <c>RenderQueueRange</c> to use for creating filtering settings that control what objects get rendered.</param>
        /// <param name="layerMask">The layer mask to use for creating filtering settings that control what objects get rendered.</param>
        /// <param name="stencilState">The stencil settings to use with this poss.</param>
        /// <param name="stencilReference">The stencil reference value to use with this pass.</param>
        /// <seealso cref="ShaderTagId"/>
        /// <seealso cref="RenderPassEvent"/>
        /// <seealso cref="RenderQueueRange"/>
        /// <seealso cref="LayerMask"/>
        /// <seealso cref="StencilState"/>
        public ForwardDrawObjectsPass(string profilerTag, ShaderTagId[] shaderTagIds, bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference)
        {
            Init(opaque, evt, renderQueueRange, layerMask, stencilState, stencilReference, shaderTagIds);

            profilingSampler = new ProfilingSampler(profilerTag);
        }

        private void Init(bool opaque, RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int stencilReference, ShaderTagId[] shaderTagIds)
        {
            foreach (ShaderTagId sid in shaderTagIds)
                m_ShaderTagIdList.Add(sid);
            
            renderPassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_IsOpaque = opaque;
            m_ShouldTransparentsReceiveShadows = false;

            if (stencilState.enabled)
            {
                m_RenderStateBlock.stencilReference = stencilReference;
                m_RenderStateBlock.mask = RenderStateMask.Stencil;
                m_RenderStateBlock.stencilState = stencilState;
            }
        }

        private static void ExecutePass(RasterCommandBuffer cmd, PassData data, RendererList rendererList/*, RendererList objectsWithErrorRendererList*/, bool yFlip)
        {
            // Global render pass data containing various settings.
            // x,y,z are currently unused
            // w is used for knowing whether the object is opaque(1) or alpha blended(0)
            Vector4 drawObjectPassData = new Vector4(0.0f, 0.0f, 0.0f, (data.isOpaque) ? 1.0f : 0.0f);
            cmd.SetGlobalVector(s_DrawObjectPassDataPropID, drawObjectPassData);

            if (data.cameraData.xr.enabled && data.isActiveTargetBackBuffer)
            {
                cmd.SetViewport(data.cameraData.xr.GetViewport());
            }

            // scaleBias.x = flipSign
            // scaleBias.y = scale
            // scaleBias.z = bias
            // scaleBias.w = unused
            float flipSign = yFlip ? -1.0f : 1.0f;
            Vector4 scaleBias = (flipSign < 0.0f)
                ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
            cmd.SetGlobalVector(ShaderPropertyId.scaleBiasRt, scaleBias);

            // Set a value that can be used by shaders to identify when AlphaToMask functionality may be active
            // The material shader alpha clipping logic requires this value in order to function correctly in all cases.
            float alphaToMaskAvailable = ((data.cameraData.cameraTargetDescriptor.msaaSamples > 1) && data.isOpaque) ? 1.0f : 0.0f;
            cmd.SetGlobalFloat(ShaderPropertyId.alphaToMaskAvailable, alphaToMaskAvailable);

            cmd.DrawRendererList(rendererList);
            // Render objects that did not match any shader pass with error shader
            //PublicRenderingUtils.DrawRendererListObjectsWithError(cmd, ref objectsWithErrorRendererList);
        }

        /// <summary>
        /// Shared pass data
        /// </summary>
        public class PassData
        {
            public TextureHandle albedoHdl;
            public TextureHandle depthHdl;

            public UniversalCameraData cameraData;
            public bool isOpaque;
            public bool shouldTransparentsReceiveShadows;
            public uint batchLayerMask;
            public bool isActiveTargetBackBuffer;
            public RendererListHandle rendererListHdl;
            //public RendererListHandle objectsWithErrorRendererListHdl;
        }

        /// <summary>
        /// Initialize the shared pass data.
        /// </summary>
        private void InitPassData(UniversalCameraData cameraData, ref PassData passData, uint batchLayerMask, bool isActiveTargetBackBuffer = false)
        {
            passData.cameraData = cameraData;
            passData.isOpaque = m_IsOpaque;
            passData.shouldTransparentsReceiveShadows = m_ShouldTransparentsReceiveShadows;
            passData.batchLayerMask = batchLayerMask;
            passData.isActiveTargetBackBuffer = isActiveTargetBackBuffer;
        }

        private void InitRendererLists(UniversalRenderingData renderingData, UniversalCameraData cameraData, UniversalLightData lightData, ref PassData passData, ScriptableRenderContext context, RenderGraph renderGraph)
        {
            ref Camera camera = ref cameraData.camera;
            var sortFlags = (m_IsOpaque) ? cameraData.defaultOpaqueSortFlags : SortingCriteria.CommonTransparent;
            if (/*cameraData.renderer.useDepthPriming && */m_IsOpaque && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
                sortFlags = SortingCriteria.SortingLayer | SortingCriteria.RenderQueue | SortingCriteria.OptimizeStateChanges | SortingCriteria.CanvasOrder;

            var filterSettings = m_FilteringSettings;
            filterSettings.batchLayerMask = passData.batchLayerMask;
#if UNITY_EDITOR
                // When rendering the preview camera, we want the layer mask to be forced to Everything
                if (cameraData.isPreviewCamera)
                {
                    filterSettings.layerMask = -1;
                }
#endif
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, renderingData, cameraData, lightData, sortFlags);
            if (/*cameraData.renderer.useDepthPriming && */m_IsOpaque && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
            {
                m_RenderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }

            PublicRenderingUtils.CreateRendererListWithRenderStateBlock(renderGraph, ref renderingData.cullResults, drawSettings, filterSettings, m_RenderStateBlock, ref passData.rendererListHdl);
            //RenderingUtils.CreateRendererListObjectsWithError(renderGraph, ref renderingData.cullResults, camera, filterSettings, sortFlags, ref passData.objectsWithErrorRendererListHdl);
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var colorTarget = resourceData.activeColorTexture;
            var depthTarget = resourceData.activeDepthTexture;
            var mainShadowsTexture = resourceData.mainShadowsTexture;
            var additionalShadowsTexture = resourceData.additionalShadowsTexture;
            const uint batchLayerMask = uint.MaxValue;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                builder.UseAllGlobalTextures(true);

                InitPassData(cameraData, ref passData, batchLayerMask, resourceData.isActiveTargetBackBuffer);

                if (colorTarget.IsValid())
                {
                    passData.albedoHdl = colorTarget;
                    builder.SetRenderAttachment(colorTarget, 0, AccessFlags.Write);
                }

                if (depthTarget.IsValid())
                {
                    passData.depthHdl = depthTarget;
                    builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Write);
                }

                if (mainShadowsTexture.IsValid())
                    builder.UseTexture(mainShadowsTexture, AccessFlags.Read);
                if (additionalShadowsTexture.IsValid())
                    builder.UseTexture(additionalShadowsTexture, AccessFlags.Read);

                TextureHandle ssaoTexture = resourceData.ssaoTexture;
                if (ssaoTexture.IsValid())
                    builder.UseTexture(ssaoTexture, AccessFlags.Read);
                PublicRenderGraphUtils.UseDBufferIfValid(builder, resourceData);

                InitRendererLists(renderingData, cameraData, lightData, ref passData, default(ScriptableRenderContext), renderGraph);
                
                builder.UseRendererList(passData.rendererListHdl);
                //builder.UseRendererList(passData.objectsWithErrorRendererListHdl);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                /*
                if (cameraData.xr.enabled)
                {
                    bool passSupportsFoveation = cameraData.xrUniversal.canFoveateIntermediatePasses || resourceData.isActiveTargetBackBuffer;
                    builder.EnableFoveatedRasterization(cameraData.xr.supportsFoveatedRendering && passSupportsFoveation);
                }
                */

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    /*
                    // Currently we only need to call this additional pass when the user
                    // doesn't want transparent objects to receive shadows
                    if (!data.isOpaque && !data.shouldTransparentsReceiveShadows)
                        TransparentSettingsPass.ExecutePass(context.cmd);
                    */

                    bool yFlip = data.cameraData.IsRenderTargetProjectionMatrixFlipped(data.albedoHdl, data.depthHdl);

                    ExecutePass(context.cmd, data, data.rendererListHdl/*, data.objectsWithErrorRendererListHdl*/, yFlip);
                });
            }
        }
        
        internal static class ShaderPropertyId
        {
            public static readonly int scaleBiasRt = Shader.PropertyToID("_ScaleBiasRt");
            public static readonly int alphaToMaskAvailable = Shader.PropertyToID("_AlphaToMaskAvailable");
        }
    }
#endif
}
