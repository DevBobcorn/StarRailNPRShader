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

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HSR.NPRShader.Passes
{
    public class ScreenSpaceShadowsPostPass : ScriptableRenderPass
    {
        private static readonly RTHandle k_CurrentActive = RTHandles.Alloc(BuiltinRenderTextureType.CurrentActive);

        public ScreenSpaceShadowsPostPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            profilingSampler = new ProfilingSampler("ScreenSpaceShadows Post");
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(k_CurrentActive);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                ShadowData shadowData = renderingData.shadowData;
                int cascadesCount = shadowData.mainLightShadowCascadesCount;
                bool mainLightShadows = renderingData.shadowData.supportsMainLightShadows;
                bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
                bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

                // Before transparent object pass, force to disable screen space shadow of main light
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

                // then enable main light shadows with or without cascades
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /*
        private class PassData
        {
            internal TextureHandle renderTarget; // Imported texture handle of k_CurrentActive
            internal UniversalShadowData shadowData;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>(GetType().ToString(), out var passData, profilingSampler))
            {
                var shadowData = frameData.Get<UniversalShadowData>();
                
                passData.renderTarget = renderGraph.ImportTexture(k_CurrentActive);
                builder.UseTexture(passData.renderTarget);
                
                passData.shadowData = shadowData;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((PassData pd, UnsafeGraphContext context) => ExecutePass(pd, context));
            }
        }
        
        private void ExecutePass(PassData passData, UnsafeGraphContext context)
        {
            var cmd = context.cmd;
            
            cmd.SetRenderTarget(passData.renderTarget);
            
            int cascadesCount = passData.shadowData.mainLightShadowCascadesCount;
            bool mainLightShadows = passData.shadowData.supportsMainLightShadows;
            bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
            bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

            // Before transparent object pass, force to disable screen space shadow of main light
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

            // then enable main light shadows with or without cascades
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
        }
        */
    }
}
