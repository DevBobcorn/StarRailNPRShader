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

        private RTHandle m_RenderTarget;

        public ScreenSpaceShadowsPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
            profilingSampler = new ProfilingSampler("ScreenSpaceShadows");
        }

        public void Dispose()
        {
            m_ShadowMaterial.DestroyCache();
            m_RenderTarget?.Release();
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            base.Configure(cmd, cameraTextureDescriptor);

            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

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

        /*
        private class PassData
        {
            internal TextureHandle renderTarget; // Imported texture handle of m_RenderTarget
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().ToString(), out var passData, profilingSampler))
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                
                ConfigureInput(ScriptableRenderPassInput.Depth);

                var desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.graphicsFormat =
                    SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Linear | GraphicsFormatUsage.Render)
                        ? GraphicsFormat.R8_UNorm
                        : GraphicsFormat.B8G8R8A8_UNorm;

                RenderingUtils.ReAllocateHandleIfNeeded(ref m_RenderTarget, desc, FilterMode.Point, TextureWrapMode.Clamp,
                    name: "_ScreenSpaceShadowmapTexture");
                passData.renderTarget = renderGraph.ImportTexture(m_RenderTarget);
                builder.UseTexture(passData.renderTarget);
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((PassData pd, UnsafeGraphContext context) => ExecutePass(pd, context));
            }
        }
        
        private void ExecutePass(PassData passData, UnsafeGraphContext context)
        {
            var cmd = context.cmd;
            
            cmd.SetGlobalTexture(m_RenderTarget.name, passData.renderTarget);
            
            cmd.SetRenderTarget(passData.renderTarget);
            cmd.ClearRenderTarget(false, false, Color.white);
            
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            var material = m_ShadowMaterial.Value;
            
            Blitter.BlitCameraTexture(nativeCmd, m_RenderTarget, m_RenderTarget, material, 0);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, false);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, true);
        }
        */
    }
}
