using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarGame.Effects
{
    /// <summary>
    /// Добавляет проход виньетки урона во все ScriptableRendererData проекта и
    /// создаёт общий материал. Запуск — пункт меню, как у PSX/VHS-эффектов:
    /// править рендереры руками не нужно.
    /// </summary>
    public static class DamageVignetteSetupEditor
    {
        private const string ShaderName = "CarGame/DamageVignettePostEffect";
        private const string MaterialPath = "Assets/Materials/DamageVignettePostEffect.mat";

        [MenuItem("Tools/CarGame/Setup Damage Vignette Post FX in Renderers")]
        public static void Setup()
        {
            EnsureFolder("Assets/Materials");

            Material material = GetOrCreateMaterial(MaterialPath, ShaderName);
            if (material == null) return;

            string[] rendererGuids = AssetDatabase.FindAssets("t:ScriptableRendererData");
            if (rendererGuids.Length == 0)
            {
                Debug.LogError("Не найдено ни одного ScriptableRendererData в проекте.");
                return;
            }

            foreach (string guid in rendererGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableRendererData rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (rendererData == null) continue;

                if (rendererData.TryGetRendererFeature<DamageVignetteFeature>(out var existing))
                {
                    existing.vignetteMaterial = material;
                    existing.SetActive(true);
                    EditorUtility.SetDirty(rendererData);
                    Debug.Log($"{path}: фича уже была — материал обновлён.");
                    continue;
                }

                DamageVignetteFeature feature = ScriptableObject.CreateInstance<DamageVignetteFeature>();
                feature.name = "DamageVignetteFeature";
                feature.vignetteMaterial = material;
                feature.SetActive(true);

                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
                Debug.Log($"{path}: добавлена фича DamageVignetteFeature.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Виньетка урона настроена. Проверь Game view в Play mode.");
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string parent = System.IO.Path.GetDirectoryName(folderPath).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(folderPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static Material GetOrCreateMaterial(string path, string shaderName)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Шейдер не найден: {shaderName}");
                return null;
            }

            Material material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"Создан материал: {path}");
            return material;
        }
    }
}
