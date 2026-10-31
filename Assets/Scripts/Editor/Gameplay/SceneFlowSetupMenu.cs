using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Создаёт и регистрирует сцены меню и смерти, чтобы игровой поток
    /// «меню → игра → смерть → меню» работал из коробки.
    ///
    /// Сцены строятся теми же сборщиками, что и пункты Create Main Menu /
    /// Create Death Screen, только в новой сцене, и сразу попадают в Build
    /// Settings — без этого SceneManager.LoadScene по имени не найдёт их.
    ///
    /// Игровая сцена (SampleScene) не трогается: только проверяется, что она
    /// есть в Build Settings.
    /// </summary>
    public static class SceneFlowSetupMenu
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string GameScenePath = ScenesFolder + "/SampleScene.unity";
        private const string MenuScenePath = ScenesFolder + "/Menu.unity";
        private const string DeathScenePath = ScenesFolder + "/DeathScreen.unity";

        [MenuItem("Tools/CarGame/Scenes/Create Main Menu Scene")]
        public static void CreateMainMenuScene()
        {
            CreateScene(MenuScenePath, "главного меню", MainMenuSetupMenu.Build);
        }

        [MenuItem("Tools/CarGame/Scenes/Create Death Screen Scene")]
        public static void CreateDeathScreenScene()
        {
            CreateScene(DeathScenePath, "экрана смерти", DeathScreenSetupMenu.Build);
        }

        [MenuItem("Tools/CarGame/Scenes/Register Scenes in Build Settings")]
        public static void RegisterScenes()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int added = 0;

            added += EnsureScene(scenes, GameScenePath);
            added += EnsureScene(scenes, MenuScenePath);
            added += EnsureScene(scenes, DeathScenePath);

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Scenes] Build Settings обновлены, добавлено сцен: {added}.");
        }

        private static void CreateScene(string path, string title, System.Func<GameObject> build)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            build();

            EnsureFolder(ScenesFolder);
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();

            RegisterScenes();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            Debug.Log($"[Scenes] Сцена {title} создана: {path}");
        }

        private static int EnsureScene(List<EditorBuildSettingsScene> scenes, string path)
        {
            if (System.IO.File.Exists(path) == false) return 0;

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == path) return 0;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            return 1;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            int slash = folder.LastIndexOf('/');
            string parent = slash > 0 ? folder.Substring(0, slash) : "Assets";
            string leaf = folder.Substring(parent.Length + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
