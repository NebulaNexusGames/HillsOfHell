using CarGame.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Собирает в текущей сцене панель паузы: затемнение, заголовок и кнопки
    /// Продолжить / Настройки / Главное меню / Выход плюс скрытую панель
    /// настроек.
    ///
    /// Это для ИГРОВОЙ сцены: запускайте пункт при открытой игровой сцене и
    /// сохраняйте. Пауза включается Esc (клавишу можно сменить в инспекторе).
    /// </summary>
    public static class PauseMenuSetupMenu
    {
        [MenuItem("Tools/CarGame/Menus/Create Pause Menu")]
        public static void CreatePauseMenu()
        {
            MenuUiBuilder.EnsureEventSystem();

            Canvas canvas = MenuUiBuilder.GetOrCreateCanvas("PauseCanvas", 200);
            PauseMenuUI pause = MenuUiBuilder.GetOrAddComponent<PauseMenuUI>(canvas.gameObject);
            Undo.RecordObject(pause, "Обновить панель паузы");

            MenuUiBuilder.ClearChildren(canvas.transform);

            Image panel = MenuUiBuilder.CreateFullscreen(canvas.transform, "PausePanel", new Color(0f, 0f, 0f, 0.72f), false);

            MenuUiBuilder.CreateLabel(panel.transform, "Title", "ПАУЗА",
                new Vector2(0f, 300f), new Vector2(1000f, 140f), 96f, MenuUiBuilder.TitleColor, TextAlignmentOptions.Center);

            Button resume = MenuUiBuilder.CreateButton(panel.transform, "ResumeButton", "ПРОДОЛЖИТЬ", new Vector2(0f, 110f), new Vector2(480f, 90f));
            Button settings = MenuUiBuilder.CreateButton(panel.transform, "SettingsButton", "НАСТРОЙКИ", new Vector2(0f, 10f), new Vector2(480f, 90f));
            Button menu = MenuUiBuilder.CreateButton(panel.transform, "MenuButton", "ГЛАВНОЕ МЕНЮ", new Vector2(0f, -90f), new Vector2(480f, 90f));
            Button report = MenuUiBuilder.CreateButton(panel.transform, "ReportButton", "СООБЩИТЬ ОБ ОШИБКЕ", new Vector2(0f, -190f), new Vector2(480f, 90f));
            Button quit = MenuUiBuilder.CreateButton(panel.transform, "QuitButton", "ВЫХОД", new Vector2(0f, -290f), new Vector2(480f, 90f));

            MenuUiBuilder.Localize(resume.GetComponentInChildren<TextMeshProUGUI>(), "ПРОДОЛЖИТЬ", "RESUME");
            MenuUiBuilder.Localize(settings.GetComponentInChildren<TextMeshProUGUI>(), "НАСТРОЙКИ", "SETTINGS");
            MenuUiBuilder.Localize(menu.GetComponentInChildren<TextMeshProUGUI>(), "ГЛАВНОЕ МЕНЮ", "MAIN MENU");
            MenuUiBuilder.Localize(report.GetComponentInChildren<TextMeshProUGUI>(), "СООБЩИТЬ ОБ ОШИБКЕ", "REPORT A BUG");
            MenuUiBuilder.Localize(quit.GetComponentInChildren<TextMeshProUGUI>(), "ВЫХОД", "QUIT");

            Image settingsPanel = MenuUiBuilder.BuildSettingsPanel(canvas.transform, out _, out Button back);

            SerializedObject so = new SerializedObject(pause);
            MenuUiBuilder.SetString(so, "menuSceneName", "Menu");
            MenuUiBuilder.SetReference(so, "panel", panel.gameObject);
            MenuUiBuilder.SetReference(so, "resumeButton", resume);
            MenuUiBuilder.SetReference(so, "settingsButton", settings);
            MenuUiBuilder.SetReference(so, "menuButton", menu);
            MenuUiBuilder.SetReference(so, "quitButton", quit);
            MenuUiBuilder.SetReference(so, "reportButton", report);
            MenuUiBuilder.SetReference(so, "settingsPanel", settingsPanel.gameObject);
            MenuUiBuilder.SetReference(so, "settingsBackButton", back);
            so.ApplyModifiedProperties();

            panel.gameObject.SetActive(false);
            settingsPanel.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;

            Debug.Log("[Menus] Панель паузы создана. Сохраните сцену (Ctrl+S).");
        }
    }
}
