using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Следит, чтобы все сцены из Assets/Scenes были в Build Settings.
    ///
    /// Без регистрации SceneManager.LoadScene("DeathScreen") в билде не
    /// сработает — именно поэтому смерть раньше не открывала экран смерти.
    /// Инструмент идемпотентный: существующий порядок сцен сохраняется,
    /// недостающие просто дописываются в конец.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBuildBootstrap
    {
        private const string ScenesFolder = "Assets/Scenes";

        static SceneBuildBootstrap()
        {
            EditorApplication.delayCall += EnsureScenesInBuild;
        }

        [MenuItem("Tools/CarGame/Scenes/Ensure Scenes In Build Settings")]
        public static void EnsureScenesInBuild()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder });
            if (guids == null || guids.Length == 0) return;

            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            HashSet<string> registered = new HashSet<string>(scenes.Select(s => s.path));

            bool changed = false;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity")) continue;
                if (registered.Contains(path)) continue;

                scenes.Add(new EditorBuildSettingsScene(path, true));
                registered.Add(path);
                changed = true;
            }

            if (!changed) return;

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Scenes] В Build Settings добавлены сцены: " + string.Join(", ", scenes.Select(s => s.path)));
        }
    }
}
