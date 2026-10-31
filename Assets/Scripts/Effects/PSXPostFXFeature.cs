using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CarGame.Effects
{
    public sealed class PSXPostFXFeature : ScriptableRendererFeature
    {
        [Tooltip("Материал PSX-эффекта (CarGame/PSXPostEffect). Пусто — эффект выключен")]
        public Material psxMaterial;

        [Tooltip("Материал VHS-эффекта (CarGame/VHSPostEffect). Пусто — эффект выключен")]
        public Material vhsMaterial;

        [Tooltip("Включает проход VHS поверх PSX")]
        public bool vhsEnabled = true;

        /// <summary>
        /// Включён ли ретро-эффект по настройке игрока. Статический флаг, чтобы
        /// UI (GameSettings) мог гасить эффект, не трогая ассет рендерера.
        /// </summary>
        public static bool RuntimeEnabled = true;

        private PsxPass psxPass;
        private VhsPass vhsPass;

        public override void Create()
        {
            psxPass = new PsxPass("PSX Post FX");
            vhsPass = new VhsPass("VHS Post FX");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!RuntimeEnabled) return;

            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            // Кадр превью предмета (инвентарь, терминалы) должен быть чистым:
            // камеру помечают PreviewRenderExclusion, чтобы её не трогала
            // пост-обработка.
            if (renderingData.cameraData.camera.TryGetComponent<PreviewRenderExclusion>(out _))
                return;

            if (psxMaterial != null)
            {
                psxPass.Setup(psxMaterial);
                psxPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                psxPass.ConfigureInput(ScriptableRenderPassInput.None);
                renderer.EnqueuePass(psxPass);
            }

            if (vhsEnabled && vhsMaterial != null)
            {
                vhsPass.Setup(vhsMaterial);
                vhsPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                vhsPass.ConfigureInput(ScriptableRenderPassInput.None);
                renderer.EnqueuePass(vhsPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            psxPass?.Dispose();
            vhsPass?.Dispose();
        }

        private abstract class PostPass : ScriptableRenderPass
        {
            private static readonly MaterialPropertyBlock sharedPropertyBlock = new MaterialPropertyBlock();
            private static readonly int blitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int blitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            private Material material;
            private RTHandle copiedColor;
            private string copyName;

            protected PostPass(string passName)
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
                    builder.SetRenderFunc((MainPassData data, RasterGraphContext rgContext) => ExecuteMainPassData(data, rgContext));
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

            private static void ExecuteCopyPassData(CopyPassData data, RasterGraphContext context)
            {
                ExecuteCopyColorPass(context.cmd, data.source);
            }

            private static void ExecuteMainPassData(MainPassData data, RasterGraphContext context)
            {
                ExecuteMainPass(context.cmd, data.source.IsValid() ? data.source : null, data.material);
            }
        }

        private sealed class PsxPass : PostPass
        {
            public PsxPass(string passName) : base(passName) { }
        }

        private sealed class VhsPass : PostPass
        {
            public VhsPass(string passName) : base(passName) { }
        }
    }
}