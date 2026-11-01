using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarGame.Effects
{
    /// <summary>
    /// Добавляет проход блика (hex lens flare) во все ScriptableRendererData
    /// проекта и создаёт общий материал. Запуск — пункт меню, как у виньетки
    /// урона и PSX/VHS-эффектов: править рендереры руками не нужно.
    /// </summary>
    public static class LensFlareSetupEditor
    {
        private const string ShaderName = "CarGame/HexLensFlare";
        private const string MaterialPath = "Assets/Materials/HexLensFlare.mat";

        [MenuItem("Tools/CarGame/Setup Hex Lens Flare Post FX in Renderers")]
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

                if (rendererData.TryGetRendererFeature<LensFlareFeature>(out var existing))
                {
                    existing.flareMaterial = material;
                    existing.SetActive(true);
                    EditorUtility.SetDirty(rendererData);
                    Debug.Log($"{path}: фича уже была — материал обновлён.");
                    continue;
                }

                LensFlareFeature feature = ScriptableObject.CreateInstance<LensFlareFeature>();
                feature.name = "LensFlareFeature";
                feature.flareMaterial = material;
                feature.SetActive(true);

                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
                Debug.Log($"{path}: добавлена фича LensFlareFeature.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Блик (hex lens flare) настроен. Проверь Game view в Play mode.");
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
