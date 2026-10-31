using System;
using System.Collections.Generic;
using CarGame.Items;
using CarGame.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarGame.UI
{
    /// <summary>
    /// Рисует предметы в инвентаре их настоящими 3D-моделями.
    ///
    /// Схема простая и deliberately одноразовая: держит ОДНУ камеру и ОДИН риг
    /// в одной точке сцены, а для каждого типа предмета делает ровно один
    /// рендер в текстуру и кладёт её в кэш. Копий моделей не остаётся: клон
    /// создаётся на время одного рендера и сразу удаляется. В сцене навсегда
    /// остаётся только один риг с камерой и двумя лампами.
    ///
    /// Дальше слоты инвентаря просто показывают готовую текстуру, поэтому
    /// каждый кадр открытого инвентаря стоит практически ноль — именно этим и
    /// был вызван фриз при открытии панели.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemPreviewService : MonoBehaviour
    {
        /// <summary>
        /// Слой 31 в проекте не занят (см. TagManager). На нём висит риг, и камера
        /// рисует только его — так в кадр превью случайно не попадёт ничего из
        /// игрового мира.
        /// </summary>
        private const int PreviewLayer = 31;

        [Serializable]
        public sealed class Source
        {
            [Tooltip("ID предмета из ItemDefs")]
            public string itemId = string.Empty;

            [Tooltip("Модель, которую надо показать. Подойдёт любой объект с Renderer")]
            public GameObject model;

            [Tooltip("Префаб из Assets/Prefabs/Items, если в сцене нет модели. Берётся у ItemWorldCatalog")]
            public GameObject prefab;

            [Tooltip("Поворот модели в кадре")]
            public Vector3 euler = new Vector3(12f, -35f, 0f);

            [Tooltip("Насколько крупно показать. 1 — модель ровно в кадр")]
            public float padding = 1.35f;

            [Tooltip("Домножитель размера, если модель живёт не в натуральную величину")]
            public float scale = 1f;
        }

        /// <summary>
        /// Сколько раз пробуем найти модель, прежде чем признать предмет
        /// без картинки. Провал не запоминается навсегда: первый запрос может
        /// прийти раньше, чем PlayerHand соберёт свои варианты, и тогда модель в
        /// проекте есть, но найти её ещё нельзя. Пара попыток после этого
        /// закрывает вопрос насовсем.
        /// </summary>
        private const int MaxAttempts = 4;

        [Header("Источники моделей")]
        [Tooltip("По одному источнику на тип предмета. Повторять itemId не нужно: рендер делается один раз на предмет")]
        [SerializeField] private Source[] sources = new Source[0];

        [Header("Рендер")]
        [Tooltip("Размер текстуры превью в пикселях")]
        [SerializeField] private int textureSize = 256;
        [Tooltip("Фон превью. Если альфа не проходит и получается чёрный квадрат — поставьте здесь 1")]
        [SerializeField] private Color background = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Яркость ключевого света")]
        [SerializeField] private float keyIntensity = 1.7f;
        [Tooltip("Яркость заполняющего света")]
        [SerializeField] private float fillIntensity = 0.85f;

        private static ItemPreviewService instance;

        private readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> attempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Camera previewCamera;
        private Transform rig;
        private bool building;

        public static ItemPreviewService Instance
        {
            get
            {
                if (instance == null) instance = FindFirstObjectByType<ItemPreviewService>();
                return instance;
            }
        }

        /// <summary>Готовое превью предмета. null — если для этого ID нет модели.</summary>
        public Texture2D Get(ItemDef def)
        {
            return Get(def, DefaultPadding);
        }

        /// <summary>Готовое превью предмета. null — если для этого ID нет модели.</summary>
        public Texture2D Get(ItemDef def, float padding)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) return null;

            string cacheKey = Padded(def.Id, padding);
            Texture2D ready;
            if (cache.TryGetValue(cacheKey, out ready)) return ready;

            // Пробел модели не значит, что её нет: возможно, PlayerHand ещё не
            // собрал список вариантов. Поэтому пробуем несколько раз и только
            // потом сдаёмся — раньше предмет оставался без иконки до конца игры.
            int tries;
            if (attempts.TryGetValue(def.Id, out tries) && tries >= MaxAttempts) return null;

            return Render(def, cacheKey, padding);
        }

        /// <summary>
        /// Кадр по умолчанию: с воздухом вокруг модели. Панель терминала зовёт
        /// перегрузку с меньшим запасом — там модель должна занять всю иконку.
        /// </summary>
        public static float DefaultPadding
        {
            get { return DefaultPaddingValue; }
        }

        private const float DefaultPaddingValue = 1.35f;

        /// <summary>
        /// Сбрасывает неудачные попытки. Нужен после того, как список вариантов
        /// руки изменился: то, что не нашлось в самом начале сцены, может
        /// появиться позже.
        /// </summary>
        public void ResetFailedAttempts()
        {
            attempts.Clear();
        }

        private bool GiveUpOn(string itemId)
        {
            int tries;
            attempts.TryGetValue(itemId, out tries);
            tries++;
            attempts[itemId] = tries;

            if (tries < MaxAttempts) return false;

            Debug.LogWarning("[ItemPreview] Для предмета '" + itemId + "' не нашлось модели — ячейка останется без картинки", this);
            return true;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            EnsureRig();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            foreach (Texture2D texture in cache.Values)
            {
                if (texture != null) Destroy(texture);
            }

            cache.Clear();
            attempts.Clear();
        }

        /// <summary>
        /// Риг создаётся один раз и живёт в одной точке сцены, глубоко под уровнем.
        /// Ничего из этого не видно в кадре: камера рендерит только в offscreen-текстуру.
        /// </summary>
        private void EnsureRig()
        {
            if (previewCamera != null || building) return;

            building = true;

            GameObject root = new GameObject("ItemPreviewRig");
            root.layer = PreviewLayer;
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, -1000f, 0f);
            rig = root.transform;

            GameObject camObject = new GameObject("PreviewCamera");
            camObject.layer = PreviewLayer;
            camObject.transform.SetParent(rig, false);

            Camera cam = camObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 1 << PreviewLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depth = -100f;
            cam.targetTexture = null;
            previewCamera = cam;

            // Пост-обработки в проекте (PSX/VHS, всполохи, винье́тка урона) —
            // это проходы рендерера, которые сами решают, рисоваться ли для
            // камеры. Помечаем камеру маркером, чтобы ни один проход не тронул
            // кадр иконки: превью предметов должно быть чистым, без пост-эффектов.
            camObject.AddComponent<CarGame.Effects.PreviewRenderExclusion>();

            // URP сам решает, каким шейдером рисовать. Не трогаем материалы:
            // у вложенных префабов они уже настроены так, как автор хотел.
            AddLight(rig, new Vector3(0.6f, 0.9f, -0.5f), keyIntensity, true);
            AddLight(rig, new Vector3(-0.7f, 0.2f, 0.6f), fillIntensity, false);

            building = false;
        }

        private static void AddLight(Transform parent, Vector3 euler, float intensity, bool key)
        {
            GameObject go = new GameObject(key ? "PreviewKeyLight" : "PreviewFillLight");
            go.layer = PreviewLayer;
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(euler);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = key ? new Color(1f, 0.97f, 0.92f) : new Color(0.74f, 0.8f, 1f);
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PreviewLayer;
        }

private Texture2D Render(ItemDef def, string cacheKey, float padding)
        {
            Source source = FindSource(def.Id);
            if (source != null && !IsUsable(source)) source = null;
            if (source == null) source = CreateSourceFromCatalog(def);
            if (source == null) source = CreateSourceFromHandVariant(def);
            if (source == null) return RenderSource(cacheKey, null);

            source.padding = padding;
            return RenderSource(cacheKey, source);
        }

        /// <summary>
        /// Превью не по ItemDef, а напрямую по префабу. Так рисуются мобы и
        /// любые объекты без предмета в каталоге: ключ нужен только для кэша.
        ///
        /// Кадр подбирается автоматически по габаритам модели, поэтому модель
        /// всегда вписана в текстуру целиком и не может «вылезти» за ячейку.
        /// </summary>
        public Texture2D GetPrefab(string key, GameObject prefab, float padding)
        {
            if (string.IsNullOrEmpty(key) || prefab == null) return null;

            string cacheKey = Padded(key, padding);
            Texture2D ready;
            if (cache.TryGetValue(cacheKey, out ready)) return ready;

            return RenderSource(cacheKey, new Source { itemId = key, prefab = prefab, padding = padding });
        }

        /// <summary>
        /// Запас кадра можно задать на конкретный вызов: панель терминала хочет
        /// модель почти во всю иконку, хотбар — с воздухом вокруг. Кэш разводится
        /// по этому же числу, иначе первый запрос победил бы и второй получил бы
        /// чужой кадр.
        /// </summary>
        private static string Padded(string key, float padding)
        {
            return Mathf.Approximately(padding, DefaultPadding)
                ? key
                : key + "@pad" + padding.ToString("0.###");
        }

        /// <summary>Общий рендер: и предмет из каталога, и произвольный префаб идут через
        /// один и тот же риг, камеру и кэш — различается только источник модели.</summary>
        private Texture2D RenderSource(string key, Source source)
        {
            GameObject template = source != null ? (source.model != null ? source.model : source.prefab) : null;
            if (template == null)
            {
                if (GiveUpOn(key)) return null;

                // Модель, возможно, ещё не зарегистрирована: у варианта руки своя
                // модель есть всегда, но список вариантов собирается в Awake, а
                // первый запрос кэша может прийти раньше. Пробуем.
                EnsureRig();
                return null;
            }

            EnsureRig();
            if (previewCamera == null) return null;

            GameObject clone = null;
            RenderTexture target = null;
            Texture2D result = null;

            try
            {
                clone = Instantiate(template, rig);
                clone.name = "Preview_" + key;
                NormalizeClone(clone, source);
                clone.SetActive(true);
                StripPreviewClutter(clone);
                PlaceForCamera(clone, source);

                int size = Mathf.Clamp(textureSize, 32, 1024);
                target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
                {
                    name = "ItemPreviewRT_" + key,
                    antiAliasing = 1,
                    useMipMap = false
                };

                RenderTexture previous = RenderTexture.active;
                previewCamera.backgroundColor = background;
                RenderInto(previewCamera, target);

                // Кадр уже в текстуре — гасим клон немедленно. Destroy отложен
                // до конца кадра, а превью рисуются пачкой (весь терминал за
                // один кадр), и предыдущие клоны всё это время висели бы в
                // риге на слое превью. Камера рисовала бы их вместе с текущим,
                // и в иконке предмета оказывался кусок соседней модели.
                clone.SetActive(false);

                RenderTexture.active = target;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "ItemPreview_" + key
                };
                result.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
                result.Apply();
                RenderTexture.active = previous;

                if (result != null)
                {
                    cache[key] = result;
                    attempts.Remove(key);
                }
                return result;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[ItemPreview] Не удалось сделать превью для '" + key + "': " + error.Message, this);
                if (result != null) Destroy(result);
                GiveUpOn(key);
                return null;
            }
            finally
            {
                previewCamera.targetTexture = null;
                if (clone != null) Destroy(clone);
                if (target != null)
                {
                    target.Release();
                    Destroy(target);
                }
            }
        }


        /// <summary>
        /// В URP обычный Camera.Render() не работает, поэтому рендер отдаём
        /// пайплайну через запрос. Camera.Render() оставлен запасным путём для
        /// встроенного рендерера.
        /// </summary>
        private static void RenderInto(Camera cam, RenderTexture target)
        {
            UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = target
            };

            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                return;
            }

            cam.targetTexture = target;
            cam.Render();
        }

        private Source FindSource(string itemId)
        {
            string wanted = ItemDefs.Canonical(itemId);

            for (int i = 0; i < sources.Length; i++)
            {
                Source source = sources[i];
                if (source == null || string.IsNullOrEmpty(source.itemId)) continue;

                if (string.Equals(ItemDefs.Canonical(source.itemId), wanted, StringComparison.OrdinalIgnoreCase))
                    return source;
            }

            return null;
        }

        /// <summary>
        /// Источник пригоден, только если в нём есть что рисовать. У части
        /// вариантов рук в сцене стоит пустая заготовка без Renderer: такой
        /// источник не даёт картинки, поэтому он отбрасывается в пользу
        /// префаба из каталога.
        /// </summary>
        private static bool IsUsable(Source source)
        {
            if (source == null) return false;
            if (source.prefab != null) return true;
            return HasRenderer(source.model);
        }

        private static bool HasRenderer(GameObject go)
        {
            return go != null && go.GetComponentInChildren<Renderer>(true) != null;
        }

        /// <summary>
        /// Источник из каталога альт-версий. У стволов и патронов в руках лежат
        /// пустые заготовки, а настоящая модель есть только в префабе из
        /// Assets/Prefabs/Items — именно она и рисуется в ячейке.
        /// </summary>
        private static Source CreateSourceFromCatalog(ItemDef def)
        {
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog == null) return null;

            GameObject prefab = catalog.Find(def.Id);
            if (prefab == null) return null;

            return new Source { itemId = def.Id, prefab = prefab };
        }

        /// <summary>
        /// Запасной источник — уже готовая модель из варианта руки. Так ключ и
        /// инжектор получают картинку без ручной правки сцены: вариант руки есть
        /// в любом проекте с этим набором предметов, а список источников мог про
        /// него просто забыть.
        /// </summary>
        private static Source CreateSourceFromHandVariant(ItemDef def)
        {
            PlayerHand hand = FindFirstObjectByType<PlayerHand>();
            if (hand == null) return null;

            GameObject variant = hand.FindHandVariant(def.Id);

            // Список вариантов собирается в Awake, а превью может запроситься
            // раньше — порядок Awake у сценовых объектов не гарантирован. Если
            // ключ не нашёлся, пересобираем список и пробуем ещё раз, иначе предмет
            // навсегда остался бы без картинки.
            if (variant == null)
            {
                hand.CacheHandVariants();
                variant = hand.FindHandVariant(def.Id);
            }

            // Пустая заготовка без меша картинки не даст: лучше не рисовать вовсе.
            if (!HasRenderer(variant)) return null;

            return new Source { itemId = def.Id, model = variant };
        }

        /// <summary>
        /// Модель в сцене висит в руке: у неё свой поворот, смещение и слои.
        /// Для картинки это лишнее, поэтому всё это сбрасывается, а масштаб
        /// остаётся — он задаёт реальный размер модели.
        /// </summary>
        private static void NormalizeClone(GameObject clone, Source source)
        {
            SetLayerRecursive(clone, PreviewLayer);

            Transform t = clone.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            float wanted = source.scale == 0f ? 1f : source.scale;
            if (!Mathf.Approximately(wanted, 1f)) t.localScale = t.localScale * wanted;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
            }
        }

        /// <summary>Клон живёт только ради картинки: звуки, физика и анимация в нём не нужны.</summary>
        private static void StripPreviewClutter(GameObject clone)
        {
            Behaviour[] behaviours = clone.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                behaviour.enabled = false;
            }

            Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        private void PlaceForCamera(GameObject clone, Source source)
        {
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 direction = Quaternion.Euler(source.euler) * Vector3.forward;
            Quaternion look = Quaternion.LookRotation(direction, Vector3.up);

            // Кадр берём по габаритам модели В ПЛОСКОСТИ КАМЕРЫ, а не по
            // bounds.extents.magnitude. Magnitude — это половина диагонали
            // ограничивающего параллелепипеда, то есть в sqrt(3) ≈ 1.73 раза
            // больше нужного: куб занимал в кадре меньше трети, и иконка
            // получалась мелкой, хотя модель в неё целиком помещалась.
            // Теперь мерим проекцию углов bounds на right/up камеры — ровно то,
            // сколько места модель занимает на экране, — и добавляем небольшой
            // запас, чтобы она не упиралась в край.
            Vector3 right = look * Vector3.right;
            Vector3 up = look * Vector3.up;

            float halfW = 0f;
            float halfH = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z) - bounds.center;

                halfW = Mathf.Max(halfW, Mathf.Abs(Vector3.Dot(corner, right)));
                halfH = Mathf.Max(halfH, Mathf.Abs(Vector3.Dot(corner, up)));
            }

            float half = Mathf.Max(0.01f, Mathf.Max(halfW, halfH));
            float radius = half * 1.7320508f;   // для отступа камеры назад

            previewCamera.transform.position = bounds.center - direction * (radius * 4f);
            previewCamera.transform.rotation = look;
            previewCamera.orthographicSize = half * Mathf.Max(1f, source.padding);
        }
    }
}