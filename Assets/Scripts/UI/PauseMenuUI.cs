using CarGame.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Пауза в игровой сцене. Esc открывает и закрывает панель.
    ///
    /// На паузе время останавливается (Time.timeScale = 0), курсор
    /// освобождается, управление игроком отключается. Кнопки: Продолжить,
    /// Настройки (панель, если назначена), Главное меню, Выход.
    ///
    /// Панель строит инструмент Tools/CarGame/Menus/Create Pause Menu.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("Сцены")]
        [Tooltip("Сцена главного меню")]
        [SerializeField] private string menuSceneName = "Menu";

        [Header("Панель")]
        [Tooltip("Корень панели паузы (то, что скрывается/показывается). Пусто — берётся этот объект")]
        [SerializeField] private GameObject panel;

        [Header("Кнопки")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private Button quitButton;

        [Header("Обратная связь")]
        [Tooltip("Ссылка, которую открывает кнопка «Сообщить об ошибке»")]
        [SerializeField] private string reportUrl = "https://";
        [SerializeField] private Button reportButton;

        [Header("Настройки")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private Button settingsBackButton;

        private bool paused;

        public bool IsPaused => paused;

        /// <summary>
        /// Пауза открыта у кого угодно. Статический флаг, чтобы UiState и рука
        /// видели меню паузы, не разыскивая его компонент.
        /// </summary>
        public static bool AnyPaused { get; private set; }

        private void Awake()
        {
            AnyPaused = false;

            if (panel == null) panel = gameObject;

            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (menuButton != null) menuButton.onClick.AddListener(ToMainMenu);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (reportButton != null) reportButton.onClick.AddListener(ReportBug);
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(CloseSettings);

            if (settingsPanel != null) settingsPanel.SetActive(false);
            panel.SetActive(false);
        }

        private void OnDestroy()
        {
            AnyPaused = false;

            if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (menuButton != null) menuButton.onClick.RemoveListener(ToMainMenu);
            if (quitButton != null) quitButton.onClick.RemoveListener(Quit);
            if (reportButton != null) reportButton.onClick.RemoveListener(ReportBug);
            if (settingsBackButton != null) settingsBackButton.onClick.RemoveListener(CloseSettings);
        }

        private void Update()
        {
            // Пока открыто меню управления или графики, Esc там работает,
            // а не снимает паузу.
            if (ControlsPanel.IsOpen || GraphicsPanel.IsOpen) return;

            if (!KeyBindings.WasPressedThisFrame(GameAction.Pause)) return;

            // Инвентарь и пауза не открываются одновременно: кто открыт первым,
            // тот и главнее. Пока открыт инвентарь, Esc его панель не трогает.
            if (!paused && InventoryUI.AnyOpen) return;

            if (paused) Resume();
            else Pause();
        }

        public void Pause()
        {
            if (paused) return;
            paused = true;
            AnyPaused = true;

            Time.timeScale = 0f;

            // Обзор, движение и курсор — единый «эталон» меню в UiState. Он
            // запоминает состояние ровно один раз при входе и возвращает его,
            // когда закрылось последнее меню, поэтому пауза поверх инвентаря (и
            // наоборот) не путает, каким управление было до входа.
            UiState.EnterMenu();

            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (panel != null) panel.SetActive(true);
        }

        public void Resume()
        {
            if (!paused) return;
            paused = false;
            AnyPaused = false;

            Time.timeScale = 1f;

            // Возвращаем управление и курсор, только если не осталось других
            // открытых МЕНЮ (инвентарь, настройки). Терминал — это нацеленная на
            // прицел панель, она не отбирает у игрока камеру, поэтому проверяем
            // именно MenuOpen, а не «любой интерфейс» (UiState.AnyOpen).
            UiState.LeaveMenu();

            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (panel != null) panel.SetActive(false);
        }

        public void OpenSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(true);
        }

        public void CloseSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void ToMainMenu()
        {
            Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (string.IsNullOrEmpty(menuSceneName))
            {
                Debug.LogWarning("[PauseMenu] Не задано имя сцены меню.", this);
                return;
            }

            SceneManager.LoadScene(menuSceneName);
        }

        public void Quit()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void ReportBug()
        {
            if (string.IsNullOrWhiteSpace(reportUrl)) return;
            Application.OpenURL(reportUrl);
        }
    }
}
