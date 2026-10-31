using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.Effects
{
    /// <summary>
    /// Метка источника блика. Компонент можно поставить руками, но игра сама
    /// цепляет его на все обычные источники света и на объекты с неоновым
    /// материалом (CarGame/Neon) — отдельно размечать сцену не нужно.
    ///
    /// Цвет берётся у источника: у света — его цвет, у неона — _NeonColor
    /// материала. Мировую позицию блика считает эффект (LensFlareFeature).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LensFlareSource : MonoBehaviour
    {
        private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
        private const string NeonShaderName = "CarGame/Neon";

        private static readonly List<LensFlareSource> active = new List<LensFlareSource>();

        /// <summary>Все живые источники блика в сцене.</summary>
        public static IReadOnlyList<LensFlareSource> Active => active;

        [Tooltip("Снять галочку — у этого источника блика нет")]
        [SerializeField] private bool emit = true;
        [Tooltip("Цвет блика, если у объекта нет ни Light, ни неонового материала")]
        [SerializeField] private Color tint = new Color(1f, 0.86f, 0.55f, 1f);
        [Tooltip("Множитель яркости блика")]
        [SerializeField, Range(0f, 4f)] private float intensity = 1f;
        [Tooltip("Размер блика в долях высоты экрана")]
        [SerializeField, Range(0.01f, 1.5f)] private float radius = 0.07f;

        private Light lightSource;
        private Renderer neonRenderer;

        // Случайная сторона, в которую уходит цепочка бликов.
        private Vector2 flareDirection = Vector2.up;

        public bool Emit => emit;
        public float Radius => radius;
        public Vector2 Direction => flareDirection;

        public float Intensity
        {
            get
            {
                float value = intensity;
                if (lightSource != null) value *= Mathf.Clamp(lightSource.intensity, 0.25f, 4f);
                return value;
            }
        }

        public Vector3 WorldPosition
        {
            get
            {
                if (neonRenderer != null) return neonRenderer.bounds.center;
                return transform.position;
            }
        }

        /// <summary>Цвет блика: цвет света, иначе цвет неона, иначе заданный вручную.</summary>
        public Color GetColor()
        {
            if (lightSource != null) return lightSource.color;

            Material material = neonRenderer != null ? neonRenderer.sharedMaterial : null;
            if (material != null && material.HasProperty(NeonColorId)) return material.GetColor(NeonColorId);

            return tint;
        }

        /// <summary>Принадлежит ли объект этому источнику (сам он или его потомок).</summary>
        public bool IsPartOf(Transform target)
        {
            return target != null && (target == transform || target.IsChildOf(transform));
        }

        private void Awake()
        {
            lightSource = GetComponent<Light>();
            if (lightSource == null) lightSource = GetComponentInChildren<Light>(true);
            neonRenderer = GetComponent<Renderer>();
            if (neonRenderer == null) neonRenderer = GetComponentInChildren<Renderer>(true);

            float angle = Random.Range(0f, Mathf.PI * 2f);
            flareDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private void Start()
        {
            if (lightSource == null) lightSource = GetComponent<Light>();
            if (lightSource == null) lightSource = GetComponentInChildren<Light>(true);
            if (neonRenderer == null) neonRenderer = GetComponent<Renderer>();
            if (neonRenderer == null) neonRenderer = GetComponentInChildren<Renderer>(true);
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        /// <summary>Находит все источники света и неон и вешает на них метку блика.</summary>
        public static void CollectSceneSources()
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !UsesNeon(renderer)) continue;
                if (renderer.GetComponentInParent<CarGame.Items.SpawnTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.ShopTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.SlotMachineTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.ExchangerTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.CraftingStation>() != null) continue;
                if (renderer.GetComponent<LensFlareSource>() == null) renderer.gameObject.AddComponent<LensFlareSource>();
            }

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.type == LightType.Directional) continue;
                if (light.intensity <= 0.05f) continue;
                if (!UsesNeonLight(light)) continue;
                if (light.GetComponent<LensFlareSource>() == null) light.gameObject.AddComponent<LensFlareSource>();
            }
        }

        private static bool UsesNeonLight(Light light)
        {
            if (light.GetComponentInParent<CarGame.Items.SpawnTerminal>() != null) return false;
            if (light.GetComponentInParent<CarGame.Items.ShopTerminal>() != null) return false;
            if (light.GetComponentInParent<CarGame.Items.SlotMachineTerminal>() != null) return false;
            if (light.GetComponentInParent<CarGame.Items.ExchangerTerminal>() != null) return false;
            if (light.GetComponentInParent<CarGame.Items.CraftingStation>() != null) return false;
            Renderer[] renderers = light.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (UsesNeon(renderers[i])) return true;
            }
            // Не добавлять блики просто от света без неонового материала
            return false;
        }

        private static bool UsesNeon(Renderer renderer)
        {
            if (renderer == null) return false;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                if (material.shader != null && material.shader.name == NeonShaderName)
                    return true;
                if (!string.IsNullOrEmpty(material.name) && material.name.Contains("Neon"))
                    return true;
            }
            return false;
        }

        /// <summary>Принудительно добавить источники блика на объект и всех потомков (для динамически созданных).</summary>
        public static void EnsureSources(GameObject root)
        {
            if (root == null) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !UsesNeon(renderer)) continue;
                if (renderer.GetComponentInParent<CarGame.Items.SpawnTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.ShopTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.SlotMachineTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.ExchangerTerminal>() != null) continue;
                if (renderer.GetComponentInParent<CarGame.Items.CraftingStation>() != null) continue;
                if (renderer.GetComponent<LensFlareSource>() == null) renderer.gameObject.AddComponent<LensFlareSource>();
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.type == LightType.Directional) continue;
                if (light.intensity <= 0.05f) continue;
                if (!UsesNeonLight(light)) continue;
                if (light.GetComponent<LensFlareSource>() == null) light.gameObject.AddComponent<LensFlareSource>();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            CollectSceneSources();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CollectSceneSources();
        }
    }
}
