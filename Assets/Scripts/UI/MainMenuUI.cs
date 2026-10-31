using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Главное меню. Кнопки Играть / Настройки / Выход.
    ///
    /// «Играть» грузит игровую сцену, «Выход» закрывает игру (в редакторе —
    /// выходит из Play Mode), «Настройки» открывает панель настроек, если она
    /// назначена. Никакой логики в сцене меню больше нет — все ссылки и имена
    /// сцен правятся в инспекторе.
    ///
    /// Само меню строит инструмент Tools/CarGame/Menus/Create Main Menu.
    /// </summary>
    [DisallowMultipleComponent]
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Сцены")]
        [Tooltip("Сцена, которая запускается по кнопке «Играть»")]
        [SerializeField] private string gameSceneName = "SampleScene";

        [Header("Кнопки")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Header("Ссылки")]
        [SerializeField] private string telegramUrl = "https://t.me/";
        [SerializeField] private string discordUrl = "https://discord.gg/";
        [SerializeField] private string youtubeUrl = "https://youtube.com/";
        [SerializeField] private Button telegramButton;
        [SerializeField] private Button discordButton;
        [SerializeField] private Button youtubeButton;

        [Header("Настройки")]
        [Tooltip("Панель, которую открывает кнопка «Настройки». Пусто — кнопка не показывается")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button settingsBackButton;

        private void Awake()
        {
            EnsureCursor();

            if (playButton != null) playButton.onClick.AddListener(Play);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(CloseSettings);

            if (telegramButton != null) telegramButton.onClick.AddListener(OpenTelegram);
            if (discordButton != null) discordButton.onClick.AddListener(OpenDiscord);
            if (youtubeButton != null) youtubeButton.onClick.AddListener(OpenYoutube);

            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (playButton != null) playButton.onClick.RemoveListener(Play);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.RemoveListener(Quit);
            if (settingsBackButton != null) settingsBackButton.onClick.RemoveListener(CloseSettings);

            if (telegramButton != null) telegramButton.onClick.RemoveListener(OpenTelegram);
            if (discordButton != null) discordButton.onClick.RemoveListener(OpenDiscord);
            if (youtubeButton != null) youtubeButton.onClick.RemoveListener(OpenYoutube);
        }

        private void EnsureCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Play()
        {
            if (string.IsNullOrEmpty(gameSceneName))
            {
                Debug.LogWarning("[MainMenu] Не задано имя игровой сцены.", this);
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(gameSceneName);
        }

        public void OpenSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(true);
        }

        public void CloseSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void OpenTelegram() => OpenUrl(telegramUrl);
        public void OpenDiscord() => OpenUrl(discordUrl);
        public void OpenYoutube() => OpenUrl(youtubeUrl);

        private void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Application.OpenURL(url);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
