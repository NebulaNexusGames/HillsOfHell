using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarGame.Effects
{
    public static class PSXPostFXSetupEditor
    {
        private const string PsxShaderName = "CarGame/PSXPostEffect";
        private const string VhsShaderName = "CarGame/VHSPostEffect";
        private const string MaterialsFolder = "Assets/Materials";

        [MenuItem("Tools/CarGame/Setup PSX/VHS Post FX in Renderers")]
        public static void Setup()
        {
            EnsureFolder(MaterialsFolder);

            Material psxMaterial = GetOrCreateMaterial(MaterialsFolder + "/PSXPostEffect.mat", PsxShaderName);
            Material vhsMaterial = GetOrCreateMaterial(MaterialsFolder + "/VHSPostEffect.mat", VhsShaderName);
            if (psxMaterial == null || vhsMaterial == null)
                return;

            string[] rendererGuids = AssetDatabase.FindAssets("t:ScriptableRendererData");
            if (rendererGuids.Length == 0)
            {
                Debug.LogError("Не найдено ни одного ScriptableRendererData в проекте.");
                return;
            }

            foreach (string guid in rendererGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (rendererData == null)
                    continue;

                if (rendererData.TryGetRendererFeature<PSXPostFXFeature>(out var existing))
                {
                    existing.psxMaterial = psxMaterial;
                    existing.vhsMaterial = vhsMaterial;
                    existing.vhsEnabled = true;
                    existing.SetActive(true);
                    EditorUtility.SetDirty(rendererData);
                    Debug.Log($"{path}: фича уже была — материалы обновлены.");
                    continue;
                }

                var feature = ScriptableObject.CreateInstance<PSXPostFXFeature>();
                feature.name = "PSXPostFXFeature";
                feature.psxMaterial = psxMaterial;
                feature.vhsMaterial = vhsMaterial;
                feature.vhsEnabled = true;
                feature.SetActive(true);

                AssetDatabase.AddObjectToAsset(feature, rendererData);
                rendererData.rendererFeatures.Add(feature);
                rendererData.SetDirty();
                EditorUtility.SetDirty(rendererData);
                Debug.Log($"{path}: добавлена фича PSXPostFXFeature.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("PSX/VHS post FX настроен. Проверь Game view в Play mode.");
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
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Шейдер не найден: {shaderName}");
                return null;
            }

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"Создан материал: {path}");
            return material;
        }
    }
}