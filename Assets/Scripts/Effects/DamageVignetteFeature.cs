using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CarGame.Effects
{
    /// <summary>
    /// Проход постобработки для виньетки урона. Материал использует шейдер
    /// CarGame/DamageVignettePostEffect и читает глобальные свойства
    /// (_DamageVignetteIntensity и т.д.), которые выставляет DamageVignette.
    /// Это тот же приём, что и у PSXPostFXFeature: полноэкранный blit цвета в
    /// общем рендерере, поэтому эффект не зависит от конкретной сцены.
    /// </summary>
    public sealed class DamageVignetteFeature : ScriptableRendererFeature
    {
        [Tooltip("Материал виньетки урона (CarGame/DamageVignettePostEffect). Пусто — эффект выключен")]
        public Material vignetteMaterial;

        private VignettePass vignettePass;

        public override void Create()
        {
            vignettePass = new VignettePass("Damage Vignette Post FX");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (vignetteMaterial == null) return;

            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            // Кадр превью предмета не должен получать винье́тку урона (см. PreviewRenderExclusion).
            if (renderingData.cameraData.camera.TryGetComponent<PreviewRenderExclusion>(out _))
                return;

            vignettePass.Setup(vignetteMaterial);
            vignettePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            vignettePass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(vignettePass);
        }

        protected override void Dispose(bool disposing)
        {
            vignettePass?.Dispose();
        }

        private sealed class VignettePass : ScriptableRenderPass
        {
            private static readonly MaterialPropertyBlock sharedPropertyBlock = new MaterialPropertyBlock();
            private static readonly int blitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int blitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            private Material material;
            private RTHandle copiedColor;
            private readonly string copyName;

            public VignettePass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
                copyName = passName + "_ColorCopy";
                requiresIntermediateTexture = true;
            }

            public void Setup(Material effectMaterial)
            {
                material = effectMaterial;
            }

            private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sourceTexture)
            {
                Blitter.BlitTexture(cmd, sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
            }

            private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle sourceTexture, Material effectMaterial)
            {
                sharedPropertyBlock.Clear();
                if (sourceTexture != null)
                    sharedPropertyBlock.SetTexture(blitTextureId, sourceTexture);
                sharedPropertyBlock.SetVector(blitScaleBiasId, new Vector4(1, 1, 0, 0));
                cmd.DrawProcedural(Matrix4x4.identity, effectMaterial, 0, MeshTopology.Triangles, 3, 1, sharedPropertyBlock);
            }

            private static RenderTextureDescriptor GetCopyDescriptor(RenderTextureDescriptor desc)
            {
                desc.msaaSamples = 1;
                desc.depthBufferBits = (int)DepthBits.None;
                return desc;
            }

            [System.Obsolete("Non-Render Graph compatibility path.", false)]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
#pragma warning disable CS0618
                ResetTarget();
#pragma warning restore CS0618
                RenderingUtils.ReAllocateHandleIfNeeded(ref copiedColor, GetCopyDescriptor(renderingData.cameraData.cameraTargetDescriptor), name: copyName);
            }

            [System.Obsolete("Non-Render Graph compatibility path.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                ref var cameraData = ref renderingData.cameraData;
                var cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, profilingSampler))
                {
                    RasterCommandBuffer rasterCmd = CommandBufferHelpers.GetRasterCommandBuffer(cmd);
                    CoreUtils.SetRenderTarget(cmd, copiedColor);
                    ExecuteCopyColorPass(rasterCmd, cameraData.renderer.cameraColorTargetHandle);
                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle);
                    ExecuteMainPass(rasterCmd, copiedColor, material);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                copiedColor?.Release();
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                Debug.Assert(resourcesData.cameraColor.IsValid());

                var targetDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
                targetDesc.name = copyName;
                targetDesc.clearBuffer = false;

                TextureHandle source = resourcesData.activeColorTexture;
                TextureHandle destination = renderGraph.CreateTexture(targetDesc);

                using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("Copy Color Full Screen", out var copyData, profilingSampler))
                {
                    copyData.source = source;
                    builder.UseTexture(copyData.source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((CopyPassData data, RasterGraphContext rgContext) => ExecuteCopyColorPass(rgContext.cmd, data.source));
                }

                source = destination;
                destination = resourcesData.activeColorTexture;

                using (var builder = renderGraph.AddRasterRenderPass<MainPassData>(passName, out var mainData, profilingSampler))
                {
                    mainData.material = material;
                    mainData.source = source;
                    builder.UseTexture(mainData.source, AccessFlags.Read);

                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((MainPassData data, RasterGraphContext rgContext) => ExecuteMainPass(rgContext.cmd, data.source.IsValid() ? data.source : null, data.material));
                }

                resourcesData.cameraColor = destination;
            }

            private class CopyPassData
            {
                public TextureHandle source;
            }

            private class MainPassData
            {
                public Material material;
                public TextureHandle source;
            }
        }
    }
}
