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
using HSR.NPRShader.PerObjectShadow;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HSR.NPRShader.Passes
{
    public class PerObjectShadowCasterPreviewPass : ScriptableRenderPass
    {
        private readonly ShadowUsage m_Usage;

        public PerObjectShadowCasterPreviewPass(string profilerTag, ShadowUsage usage)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            profilingSampler = new ProfilingSampler(profilerTag);

            m_Usage = usage;
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalInt(PerObjectShadowCasterPass.PropertyIds.ShadowCount(m_Usage), 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private class PassData
        {
            // Nothing to store here...
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetType().ToString(), out _, profilingSampler))
            {
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((PassData _, RasterGraphContext context) => ExecutePass(context));
            }
        }
        
        private void ExecutePass(RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            cmd.SetGlobalInt(PerObjectShadowCasterPass.PropertyIds.ShadowCount(m_Usage), 0);
        }
    }
}
