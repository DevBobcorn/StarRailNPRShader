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
    public class ScreenSpaceShadowsPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial m_ShadowMaterial = new(StarRailBuiltinShaders.ScreenSpaceShadowsShader);
#if STARRAIL_URP_COMPATIBILITY_MODE
        private RTHandle m_RenderTarget;
#endif
        private int m_ScreenSpaceShadowmapTextureID;

        public ScreenSpaceShadowsPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            profilingSampler = new ProfilingSampler("ScreenSpaceShadows");
            m_ScreenSpaceShadowmapTextureID = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");
        }

        public void Dispose()
        {
            m_ShadowMaterial.DestroyCache();
#if STARRAIL_URP_COMPATIBILITY_MODE
            m_RenderTarget?.Release(); // For Non-RG path
#endif
        }

        public void NeoSetup()
        {
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

#if STARRAIL_URP_COMPATIBILITY_MODE
        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            base.Configure(cmd, cameraTextureDescriptor);

            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat =
                SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Linear | GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;

            RenderingUtils.ReAllocateHandleIfNeeded(ref m_RenderTarget, desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: "_ScreenSpaceShadowmapTexture");
            cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

            ConfigureTarget(m_RenderTarget);
            ConfigureClear(ClearFlag.None, Color.white);
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Material material = m_ShadowMaterial.Value;
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                Blitter.BlitCameraTexture(cmd, m_RenderTarget, m_RenderTarget, material, 0);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif

        private class PassData
        {
            public TextureHandle target;
            public Material material;
            internal int shadowmapID;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat =
                SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Linear | GraphicsFormatUsage.Render)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;
            TextureHandle color = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true);
            
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetType().ToString(), out var passData, profilingSampler))
            {
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                
                passData.material = m_ShadowMaterial.Value;
                passData.target = color;
                passData.shadowmapID = m_ScreenSpaceShadowmapTextureID;
                builder.AllowGlobalStateModification(true);
                
                if (color.IsValid())
                    builder.SetGlobalTextureAfterPass(color, m_ScreenSpaceShadowmapTextureID);
                
                builder.SetRenderFunc((PassData pd, RasterGraphContext context) =>
                {
                    ExecutePass(context.cmd, pd, passData.target);
                });
            }
        }
        
        private static void ExecutePass(RasterCommandBuffer cmd, PassData passData, RTHandle target)
        {
            Blitter.BlitTexture(cmd, target, Vector2.one, passData.material, 0);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
        }
    }
}
