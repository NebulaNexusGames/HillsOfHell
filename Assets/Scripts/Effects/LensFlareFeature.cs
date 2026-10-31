using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CarGame.Effects
{
    /// <summary>
    /// Постобработка «шестиугольный блик» (hex lens flare). Источники собирает
    /// LensFlareSource (свет + неон), а этот проход проецирует их в экран и
    /// кладёт аддитивных призраков-шестиугольников поверх кадра. Материал должен
    /// использовать шейдер CarGame/HexLensFlare.
    /// </summary>
    public sealed class LensFlareFeature : ScriptableRendererFeature
    {
        [Tooltip("Материал блика (CarGame/HexLensFlare). Пусто — эффект выключен")]
        public Material flareMaterial;
        [Tooltip("Сколько источников рисовать за кадр (дороже — больше). Максимум 16")]
        public int maxSources = 16;
        [Tooltip("Общий множитель яркости бликов")]
        public float intensity = 1f;
        [Tooltip("Источники дальше этого расстояния блик не дают (нужно подойти близко)")]
        public float maxDistance = 12f;
        [Tooltip("Насколько точно нужно смотреть на источник: доля половины диагонали экрана")]
        [Range(0.01f, 1f)] public float lookAccuracy = 0.2f;
        [Tooltip("Слои, которые перекрывают источник (стены и т.п.)")]
        public LayerMask occlusionMask = ~0;

        /// <summary>
        /// Включены ли блики по настройке игрока. Статический флаг, чтобы UI
        /// (GameSettings) мог гасить эффект, не трогая ассет рендерера.
        /// </summary>
        public static bool RuntimeEnabled = true;

        private LensFlarePass flarePass;

        public override void Create()
        {
            flarePass = new LensFlarePass("Hex Lens Flare");
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!RuntimeEnabled) return;
            if (flareMaterial == null) return;
            if (LensFlareSource.Active.Count == 0) return;

            if (renderingData.cameraData.cameraType == CameraType.Preview
                || renderingData.cameraData.cameraType == CameraType.Reflection)
                return;

            // Кадр превью предмета не должен получать всполохи (см. PreviewRenderExclusion).
            if (renderingData.cameraData.camera.TryGetComponent<PreviewRenderExclusion>(out _))
                return;

            flarePass.Setup(flareMaterial, renderingData.cameraData.camera, maxSources, intensity, maxDistance, lookAccuracy, occlusionMask);
            flarePass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            flarePass.ConfigureInput(ScriptableRenderPassInput.None);
            renderer.EnqueuePass(flarePass);
        }

        protected override void Dispose(bool disposing)
        {
            flarePass?.Dispose();
        }

        private sealed class LensFlarePass : ScriptableRenderPass
        {
            internal const int MaxSources = 16;

            private static readonly MaterialPropertyBlock sharedPropertyBlock = new MaterialPropertyBlock();
            private static readonly int blitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int blitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int flareDataId = Shader.PropertyToID("_FlareData");
            private static readonly int flareColorId = Shader.PropertyToID("_FlareColor");
            private static readonly int flareDirId = Shader.PropertyToID("_FlareDir");
            private static readonly int flareCountId = Shader.PropertyToID("_FlareCount");
            private static readonly int intensityId = Shader.PropertyToID("_Intensity");

            private static readonly Vector4[] flareData = new Vector4[MaxSources];
            private static readonly Vector4[] flareColor = new Vector4[MaxSources];
            private static readonly Vector4[] flareDir = new Vector4[MaxSources];

            private Material material;
            private int sourceCount;
            private float intensityScale;
            private RTHandle copiedColor;
            private readonly string copyName;

            public LensFlarePass(string passName)
            {
                profilingSampler = new ProfilingSampler(passName);
                copyName = passName + "_ColorCopy";
                requiresIntermediateTexture = true;
            }

            public void Setup(Material effectMaterial, Camera camera, int maxSources, float intensity, float maxDistance, float lookAccuracy, LayerMask occlusionMask)
            {
                material = effectMaterial;
                intensityScale = intensity;
                sourceCount = 0;

                if (camera == null) return;

                int limit = Mathf.Clamp(Mathf.Min(maxSources, MaxSources), 0, MaxSources);
                if (limit <= 0) return;

                IReadOnlyList<LensFlareSource> sources = LensFlareSource.Active;
                float aspect = camera.aspect;
                float maxDistanceFromCenter = 0.5f * Mathf.Sqrt(aspect * aspect + 1f) * Mathf.Clamp(lookAccuracy, 0.05f, 1f);
                float viewDistance = Mathf.Max(0.1f, maxDistance);

                candidates.Clear();

                for (int i = 0; i < sources.Count; i++)
                {
                    LensFlareSource source = sources[i];
                    if (source == null || !source.Emit) continue;

                    Vector3 worldPosition = source.WorldPosition;
                    Vector3 toSource = worldPosition - camera.transform.position;
                    float distance = toSource.magnitude;

                    // Блики видны только у достаточно близких источников.
                    if (distance > viewDistance) continue;

                    Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
                    if (viewport.z <= 0.01f) continue;
                    if (viewport.x < -0.25f || viewport.x > 1.25f || viewport.y < -0.25f || viewport.y > 1.25f) continue;

                    // Источник не даёт блика, если его перекрывает стена и т.п.
                    if (IsOccluded(camera, source, occlusionMask, distance)) continue;

                    // Блик виден только когда игрок смотрит примерно на источник.
                    float dx = (viewport.x - 0.5f) * aspect;
                    float dy = viewport.y - 0.5f;
                    float distanceFromCenter = Mathf.Sqrt(dx * dx + dy * dy);
                    float near = Mathf.Clamp01(1f - distanceFromCenter / Mathf.Max(0.0001f, maxDistanceFromCenter));

                    float centerFade = near * near * (3f - 2f * near);
                    if (centerFade <= 0.001f) continue;

                    // И затухание к пределу дальности, чтобы блик не «щёлкал».
                    float distanceFade = 1f - Mathf.Clamp01(distance / viewDistance);
                    distanceFade = distanceFade * distanceFade * (3f - 2f * distanceFade);

                    float brightness = source.Intensity * centerFade * distanceFade;
                    if (brightness <= 0.001f) continue;

                    candidates.Add(new FlareCandidate
                    {
                        source = source,
                        viewport = viewport,
                        brightness = brightness
                    });
                }

                // Слоты ограничены: сначала показываем те блики, что ярче на экране
                // (ближе к центру взгляда и/или ближе к игроку), чтобы не терять
                // источники из-за случайного порядка обхода.
                candidates.Sort(CompareByBrightness);

                int shown = Mathf.Min(candidates.Count, limit);
                for (int i = 0; i < shown; i++)
                {
                    FlareCandidate candidate = candidates[i];
                    LensFlareSource source = candidate.source;
                    Color color = source.GetColor();
                    Vector2 direction = source.Direction;

                    flareData[sourceCount] = new Vector4(candidate.viewport.x, candidate.viewport.y, candidate.brightness, source.Radius);
                    flareColor[sourceCount] = new Vector4(color.r, color.g, color.b, 1f);
                    flareDir[sourceCount] = new Vector4(direction.x, direction.y, 0f, 0f);
                    sourceCount++;
                }
            }

            private struct FlareCandidate
            {
                public LensFlareSource source;
                public Vector3 viewport;
                public float brightness;
            }

            private static readonly List<FlareCandidate> candidates = new List<FlareCandidate>(MaxSources);

            private static int CompareByBrightness(FlareCandidate a, FlareCandidate b)
            {
                return b.brightness.CompareTo(a.brightness);
            }

            private static bool IsOccluded(Camera camera, LensFlareSource source, LayerMask mask, float distance)
            {
                Vector3 origin = camera.transform.position;
                Vector3 target = source.WorldPosition;
                Vector3 direction = target - origin;
                if (direction.sqrMagnitude <= 0.000001f) return false;
                direction /= Mathf.Sqrt(direction.sqrMagnitude);

                if (!Physics.Raycast(origin, direction, out RaycastHit hit, Mathf.Max(0.01f, distance), mask, QueryTriggerInteraction.Ignore))
                    return false;

                Transform hitTransform = hit.collider.transform;
                if (source.IsPartOf(hitTransform)) return false;

                // Камера и игрок не должны сами себя перекрывать.
                if (hitTransform.root == camera.transform.root) return false;

                // Объект, на котором висит источник (машина, прибор и т.п.), не
                // считается препятствием: неоновый элемент может быть встроен
                // внутрь корпуса, и луч попадал бы в собственный коллайдер.
                if (hitTransform.root == source.transform.root) return false;

                return true;
            }

            private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sourceTexture)
            {
                Blitter.BlitTexture(cmd, sourceTexture, new Vector4(1, 1, 0, 0), 0.0f, false);
            }

            private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle sourceTexture, Material effectMaterial, int count, float intensity)
            {
                sharedPropertyBlock.Clear();
                if (sourceTexture != null)
                    sharedPropertyBlock.SetTexture(blitTextureId, sourceTexture);
                sharedPropertyBlock.SetVector(blitScaleBiasId, new Vector4(1, 1, 0, 0));
                sharedPropertyBlock.SetVectorArray(flareDataId, flareData);
                sharedPropertyBlock.SetVectorArray(flareColorId, flareColor);
                sharedPropertyBlock.SetVectorArray(flareDirId, flareDir);
                sharedPropertyBlock.SetFloat(flareCountId, count <= 0 ? 0f : count);
                sharedPropertyBlock.SetFloat(intensityId, intensity);
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
                if (material == null || sourceCount <= 0) return;

                ref var cameraData = ref renderingData.cameraData;
                var cmd = CommandBufferPool.Get();

                using (new ProfilingScope(cmd, profilingSampler))
                {
                    RasterCommandBuffer rasterCmd = CommandBufferHelpers.GetRasterCommandBuffer(cmd);
                    CoreUtils.SetRenderTarget(cmd, copiedColor);
                    ExecuteCopyColorPass(rasterCmd, cameraData.renderer.cameraColorTargetHandle);
                    CoreUtils.SetRenderTarget(cmd, cameraData.renderer.cameraColorTargetHandle);
                    ExecuteMainPass(rasterCmd, copiedColor, material, sourceCount, intensityScale);
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
                    mainData.count = sourceCount;
                    mainData.intensity = intensityScale;
                    builder.UseTexture(mainData.source, AccessFlags.Read);

                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((MainPassData data, RasterGraphContext rgContext) => ExecuteMainPass(rgContext.cmd, data.source.IsValid() ? data.source : null, data.material, data.count, data.intensity));
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
                public int count;
                public float intensity;
            }
        }
    }
}
