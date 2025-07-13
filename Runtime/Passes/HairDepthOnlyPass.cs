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

using System;
using HSR.NPRShader.Utils;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HSR.NPRShader.Passes
{
    public class HairDepthOnlyPass : ScriptableRenderPass, IDisposable
    {
        public enum DownscaleMode
        {
            None = 1,
            Half = 2,
            Quarter = 4,
        }

        private static readonly ShaderTagId s_ShaderTagId = new("HSRHairDepthOnly");

        private FilteringSettings m_FilteringSettings;
        private DownscaleMode m_DownscaleMode;
        private DepthBits m_DepthBits;
        private RTHandle m_DepthRT;

        public HairDepthOnlyPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            profilingSampler = new ProfilingSampler("StarRailHairDepthPrepass");

            m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        }

        public void Dispose()
        {
            m_DepthRT?.Release();
        }

        public void Setup(DownscaleMode downscaleMode, DepthBits depthBits)
        {
            m_DownscaleMode = downscaleMode;
            m_DepthBits = depthBits;
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            base.Configure(cmd, cameraTextureDescriptor);

            RenderTextureDescriptor depthDesc = cameraTextureDescriptor;
            depthDesc.width /= (int)m_DownscaleMode;
            depthDesc.height /= (int)m_DownscaleMode;
            depthDesc.msaaSamples = 1;
            depthDesc.graphicsFormat = GraphicsFormat.None;

            int depthBits = Mathf.Max((int)m_DepthBits, (int)DepthBits.Depth8);
            depthDesc.depthStencilFormat = GraphicsFormatUtility.GetDepthStencilFormat(depthBits, 0);

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_DepthRT, in depthDesc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_HairDepthTexture");

            ConfigureTarget(m_DepthRT);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                // 在 context.DrawRenderers 之前 ExecuteCommandBuffer
                // 保证 FrameDebugger 里 context.DrawRenderers 会被包在这个 scope 中
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                SortingCriteria sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = RenderingUtils.CreateDrawingSettings(s_ShaderTagId, ref renderingData, sortFlags);
                drawSettings.perObjectData = PerObjectData.None;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings);

                cmd.SetGlobalTexture(PropertyIds._HairDepthTexture, m_DepthRT);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class PassData
        {
            internal RendererListHandle rendererListHandle;
            internal TextureHandle renderTarget; // Imported texture handle of m_DepthRT
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if ((int)m_DownscaleMode == 0)
            {
                Debug.LogWarning($"Downscale mode is not set!");
                return;
            }
            
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetType().ToString(), out var passData, profilingSampler))
            {
                var renderingData = frameData.Get<UniversalRenderingData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();
                
                // Configure
                var depthDesc = cameraData.cameraTargetDescriptor;
                depthDesc.width /= (int)m_DownscaleMode;
                depthDesc.height /= (int)m_DownscaleMode;
                depthDesc.msaaSamples = 1;
                depthDesc.graphicsFormat = GraphicsFormat.None;
                int depthBits = Mathf.Max((int)m_DepthBits, (int)DepthBits.Depth8);
                depthDesc.depthStencilFormat = GraphicsFormatUtility.GetDepthStencilFormat(depthBits, 0);
                
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_DepthRT, in depthDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_HairDepthTexture");
                passData.renderTarget = renderGraph.ImportTexture(m_DepthRT);
                builder.UseTexture(passData.renderTarget, AccessFlags.Write);

                var sortFlags = cameraData.defaultOpaqueSortFlags;
                var drawSettings = RenderingUtils.CreateDrawingSettings(s_ShaderTagId, renderingData, cameraData, lightData, sortFlags);
                drawSettings.perObjectData = PerObjectData.None;
                
                var rendererListParameters = new RendererListParams(renderingData.cullResults, drawSettings, m_FilteringSettings);
                var rendererListHandle = renderGraph.CreateRendererList(rendererListParameters);
                passData.rendererListHandle = rendererListHandle;
                builder.UseRendererList(passData.rendererListHandle);
                
                builder.AllowPassCulling(false);
                //builder.SetRenderAttachment(passData.renderTarget, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(passData.renderTarget, PropertyIds._HairDepthTexture);
                
                builder.SetRenderFunc((PassData pd, RasterGraphContext context) => ExecutePass(pd, context));
            }
        }

        private static void ExecutePass(PassData passData, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            cmd.ClearRenderTarget(true, true, Color.black);

            cmd.DrawRendererList(passData.rendererListHandle);
        }

        private static class PropertyIds
        {
            public static readonly int _HairDepthTexture = MemberNameHelpers.ShaderPropertyID();
        }
    }
}
