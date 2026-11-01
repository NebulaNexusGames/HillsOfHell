using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarGame.Combat
{
    /// <summary>
    /// Пулевая декаль на стене. Полностью процедурная: маленький квад с
    /// радиальным затемнением, прижатый к поверхности чуть выше неё, чтобы не
    /// мерцал сквозь стену (ZTest LEqual + смещение по нормали).
    ///
    /// Вызывается из WeaponItem после hitscan-выстрела:
    /// BulletDecal.Spawn(point, normal). Живёт lifetime секунд, затем
    /// затухает и удаляется. Всего на сцене держится не больше poolCap штук —
    /// старые вытесняются, чтобы дробь из 8 пуль не плодила объекты бесконечно.
    /// </summary>
    public static class BulletDecal
    {
        private class Entry
        {
            public GameObject go;
            public Material material;
        }

        private static readonly List<Entry> Active = new List<Entry>();

        private static Texture2D decalTexture;
        private static Material baseMaterial;

        public static float Lifetime = 12f;
        public static int PoolCap = 96;
        public static float Size = 0.14f;
        public static Color Tint = new Color(0.05f, 0.04f, 0.035f, 0.92f);

        /// <summary>
        /// Поставить декаль в точку point по нормали normal. Нормаль берётся
        /// из HitscanResult.Normal; нулевая — декаль не ставится.
        /// </summary>
        public static void Spawn(Vector3 point, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.5f) return;

            EnsureMaterials();
            Prune();

            if (Active.Count >= PoolCap)
            {
                Entry oldest = Active[0];
                Active.RemoveAt(0);
                if (oldest != null && oldest.go != null) Object.Destroy(oldest.go);
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BulletDecal";

            Collider quadCollider = go.GetComponent<Collider>();
            if (quadCollider != null) Object.Destroy(quadCollider);

            Renderer renderer = go.GetComponent<Renderer>();

            // У каждой декали свой экземпляр материала: затухание по alpha не
            // должно задевать остальные следы.
            Material material = new Material(baseMaterial);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightmapIndex = -1;

            // Лицевая сторона примитива Quad смотрит вдоль локального -Z,
            // поэтому разворачиваем квадрат нормалью к себе (LookRotation(-normal)).
            go.transform.position = point + normal * 0.012f;
            go.transform.rotation = Quaternion.LookRotation(-normal);
            go.transform.localScale = Vector3.one * Size * Random.Range(0.85f, 1.15f);

            Entry entry = new Entry { go = go, material = material };
            Active.Add(entry);

            CoroutineHost.Run(Fade(entry));
        }

        private static IEnumerator Fade(Entry entry)
        {
            float half = Lifetime * 0.7f;
            yield return new WaitForSeconds(half);

            float fadeTime = Mathf.Max(0.01f, Lifetime - half);
            float elapsed = 0f;
            Color start = Tint;

            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                if (entry.material == null) yield break;

                Color color = start;
                color.a = start.a * (1f - elapsed / fadeTime);
                entry.material.color = color;
                yield return null;
            }

            if (entry.go != null) Object.Destroy(entry.go);
            Active.Remove(entry);
        }

        private static void Prune()
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (Active[i].go == null) Active.RemoveAt(i);
            }
        }

        private static void EnsureMaterials()
        {
            if (decalTexture == null) decalTexture = BuildTexture();
            if (baseMaterial != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");

            baseMaterial = new Material(shader);
            baseMaterial.hideFlags = HideFlags.HideAndDontSave;

            if (baseMaterial.HasProperty("_BaseMap")) baseMaterial.SetTexture("_BaseMap", decalTexture);
            if (baseMaterial.HasProperty("_MainTex")) baseMaterial.SetTexture("_MainTex", decalTexture);
            if (baseMaterial.HasProperty("_BaseColor")) baseMaterial.SetColor("_BaseColor", Color.white);
            baseMaterial.color = Tint;

            // Прозрачность + сортировка поверх геометрии стены с небольшим offset.
            baseMaterial.SetFloat("_Surface", 3f); // URP Unlit: Transparent
            baseMaterial.SetFloat("_Blend", 0f);
            baseMaterial.SetFloat("_ZWrite", 0f);
            baseMaterial.SetFloat("_Cull", 2f);    // Off — сторона всегда видна
            baseMaterial.renderQueue = 3000;
        }

        /// <summary>
        /// Мягкое круглое пятно с рваным краем: тёмный центр, затухание к
        /// краям, лёгкий шум по радиусу — след от пули, а не идеальная мишень.
        /// </summary>
        private static Texture2D BuildTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            System.Random rng = new System.Random(1337);
            float[] jag = new float[16];
            for (int i = 0; i < jag.Length; i++) jag[i] = 0.72f + (float)rng.NextDouble() * 0.28f;

            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 delta = new Vector2(x - center.x, y - center.y);
                    float distance = delta.magnitude / center.x;

                    float alpha = 0f;
                    if (distance < 1f)
                    {
                        float angle = Mathf.Atan2(delta.y, delta.x);
                        int sector = Mathf.FloorToInt((angle + Mathf.PI) / (2f * Mathf.PI) * jag.Length) % jag.Length;
                        float edge = jag[sector];

                        if (distance < edge)
                        {
                            float core = Mathf.SmoothStep(1f, 0.15f, distance / edge);
                            alpha = core * core;
                        }
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Хост корутин: статический вызову негде жить, поэтому один скрытый
        /// MonoBehaviour на весь процесс.
        /// </summary>
        private class CoroutineHost : MonoBehaviour
        {
            private static CoroutineHost instance;

            public static CoroutineHost Instance
            {
                get
                {
                    if (instance == null)
                    {
                        instance = FindObjectOfType<CoroutineHost>();
                        if (instance == null)
                        {
                            GameObject go = new GameObject("[BulletDecalHost]");
                            go.hideFlags = HideFlags.HideAndDontSave;
                            instance = go.AddComponent<CoroutineHost>();
                        }
                    }
                    return instance;
                }
            }

            public static void Run(IEnumerator routine) => Instance.StartCoroutine(routine);
        }
    }
}
