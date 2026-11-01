using CarGame.UI;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Создаёт ассет оформления терминалов в Resources — ровно по тому пути,
    /// откуда <see cref="TerminalTheme"/> подхватывает его автоматически
    /// (Resources/TerminalTheme).
    ///
    /// После создания ассет выделяется: в инспекторе назначаются спрайты фона,
    /// ячейки, кнопки и шрифт — и это сразу меняет вид всех терминалов игры.
    /// </summary>
    public static class TerminalThemeSetupMenu
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string AssetPath = ResourcesFolder + "/TerminalTheme.asset";

        [MenuItem("Tools/CarGame/UI/Create Terminal Theme")]
        public static void CreateTerminalTheme()
        {
            TerminalTheme existing = AssetDatabase.LoadAssetAtPath<TerminalTheme>(AssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log("[Theme] Ассет темы уже есть — выделен в проекте.");
                return;
            }

            EnsureFolder(ResourcesFolder);

            TerminalTheme theme = ScriptableObject.CreateInstance<TerminalTheme>();
            AssetDatabase.CreateAsset(theme, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            TerminalTheme.Reload();
            Selection.activeObject = theme;
            EditorGUIUtility.PingObject(theme);

            Debug.Log("[Theme] Ассет темы создан: " + AssetPath + ". Назначьте спрайты и шрифт в инспекторе.");
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
