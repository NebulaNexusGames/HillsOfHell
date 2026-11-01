using CarGame.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Собирает в текущей сцене главное меню: фон, заголовок и кнопки
    /// Играть / Настройки / Выход, плюс скрытая панель настроек с кнопкой
    /// «Назад».
    ///
    /// Инструмент не создаёт саму сцену: откройте (или создайте) сцену меню,
    /// запустите пункт меню и сохраните. Игровая сцена по умолчанию —
    /// SampleScene, правьте её в инспекторе на объекте с MainMenuUI.
    /// </summary>
    public static class MainMenuSetupMenu
    {
        [MenuItem("Tools/CarGame/Menus/Create Main Menu")]
        public static void CreateMainMenu()
        {
            GameObject root = Build();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            Debug.Log("[Menus] Главное меню создано. Сохраните сцену (Ctrl+S).");
        }

        /// <summary>
        /// Собирает главное меню в текущей сцене и возвращает корневой объект.
        /// Вынесено отдельно, чтобы сцену меню можно было создать целиком
        /// через SceneFlowSetupMenu.
        /// </summary>
        public static GameObject Build()
        {
            MenuUiBuilder.EnsureEventSystem();
            MenuUiBuilder.EnsureCamera();
            MenuUiBuilder.EnsureDirectionalLight();

            Canvas canvas = MenuUiBuilder.GetOrCreateCanvas("MenuCanvas", 100);
            MainMenuUI menu = MenuUiBuilder.GetOrAddComponent<MainMenuUI>(canvas.gameObject);
            Undo.RecordObject(menu, "Обновить главное меню");

            MenuUiBuilder.ClearChildren(canvas.transform);

            MenuUiBuilder.CreateFullscreen(canvas.transform, "Backdrop", MenuUiBuilder.BackdropColor, true);

            MenuUiBuilder.CreateLabel(canvas.transform, "Title", "CAR GAME",
                new Vector2(0f, 320f), new Vector2(1200f, 160f), 120f, MenuUiBuilder.TitleColor, TextAlignmentOptions.Center);

            Button play = MenuUiBuilder.CreateButton(canvas.transform, "PlayButton", "ИГРАТЬ", new Vector2(0f, 40f), new Vector2(460f, 96f));
            Button settings = MenuUiBuilder.CreateButton(canvas.transform, "SettingsButton", "НАСТРОЙКИ", new Vector2(0f, -80f), new Vector2(460f, 96f));
            Button quit = MenuUiBuilder.CreateButton(canvas.transform, "QuitButton", "ВЫХОД", new Vector2(0f, -200f), new Vector2(460f, 96f));

            Button telegram = MenuUiBuilder.CreateButton(canvas.transform, "TelegramButton", "TELEGRAM", new Vector2(-330f, -360f), new Vector2(300f, 72f));
            Button discord = MenuUiBuilder.CreateButton(canvas.transform, "DiscordButton", "DISCORD", new Vector2(0f, -360f), new Vector2(300f, 72f));
            Button youtube = MenuUiBuilder.CreateButton(canvas.transform, "YoutubeButton", "YOUTUBE", new Vector2(330f, -360f), new Vector2(300f, 72f));

            MenuUiBuilder.Localize(play.GetComponentInChildren<TextMeshProUGUI>(), "ИГРАТЬ", "PLAY");
            MenuUiBuilder.Localize(settings.GetComponentInChildren<TextMeshProUGUI>(), "НАСТРОЙКИ", "SETTINGS");
            MenuUiBuilder.Localize(quit.GetComponentInChildren<TextMeshProUGUI>(), "ВЫХОД", "QUIT");

            Image settingsPanel = MenuUiBuilder.BuildSettingsPanel(canvas.transform, out _, out Button back);

            SerializedObject so = new SerializedObject(menu);
            MenuUiBuilder.SetString(so, "gameSceneName", "SampleScene");
            MenuUiBuilder.SetReference(so, "playButton", play);
            MenuUiBuilder.SetReference(so, "settingsButton", settings);
            MenuUiBuilder.SetReference(so, "quitButton", quit);
            MenuUiBuilder.SetReference(so, "settingsPanel", settingsPanel.gameObject);
            MenuUiBuilder.SetReference(so, "settingsBackButton", back);
            MenuUiBuilder.SetReference(so, "telegramButton", telegram);
            MenuUiBuilder.SetReference(so, "discordButton", discord);
            MenuUiBuilder.SetReference(so, "youtubeButton", youtube);
            so.ApplyModifiedProperties();

            settingsPanel.gameObject.SetActive(false);
            return canvas.gameObject;
        }
    }
}
