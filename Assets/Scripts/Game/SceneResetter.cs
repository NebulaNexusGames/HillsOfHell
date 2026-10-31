using System.Collections.Generic;
using CarGame.Combat;
using CarGame.Player;
using CarGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.Game
{
    /// <summary>
    /// Гарантирует, что после любой повторной загрузки игровой сцены (прямой
    /// рестарт из игры, экран смерти, главное меню) сцена выглядит и работает
    /// ровно так же, как при ПЕРВОМ запуске.
    ///
    /// Ключевой принцип: Unity при SceneManager.LoadScene сама пересобирает
    /// сцену точно из файла .unity — это и есть эталон первого запуска.
    /// Поэтому здесь НЕТ никакого снимка состояния и тем более уничтожения
    /// «чужих» объектов. Есть только один страховочный проход сразу после
    /// загрузки, который чинит то, что не может восстановить файл сцены, —
    /// рантайм-состояние, живущее вне сериализованных полей:
    ///
    /// 1. Управление игрока. Флаги controlEnabled/lookEnabled и курсор —
    ///    приватные рантайм-поля и статика UiState; если предыдущий забег
    ///    закончился со открытым меню/смертью, в новой сцене они остались бы
    ///    заблокированными («игрок ничего не может»). Здесь управление
    ///    всегда возвращается в состояние первого запуска: полный контроль,
    ///    запертый курсор, полная шкала здоровья.
    ///
    /// 2. Панели терминалов (HUD). rig «TerminalPanel» создаётся кодом и его
    ///    позиция вычисляется каждый кадр из ScreenPoint терминала. Еслиrig
    ///    попал в снимок/восстановление или панель пересоздана кодом с дефолт-
    ///    ными полями, HUD «съезжает» с места. Здесь перед любым захватом
    ///    эталонов сбрасываются статические флаги TerminalUI, а все существующие
    ///    панели принудительно перепривязываются к своим терминалам — ровно
    ///    как на первом кадре первой загрузки.
    ///
    /// Никакие объекты не удаляются и не копируются; трансформы и
    /// сериализуемые поля компонентов не перезаписываются — их уже вернул
    /// к эталону сам движок при загрузке сцены из файла.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneResetter : MonoBehaviour
    {
        private const string HostName = "[SceneResetter]";

        [Header("Версия скрипта")]
        [SerializeField] private string scriptVersion = "1.4.0";

        [Tooltip("Сцены, за которыми следит сброс. Пусто = любые загружаемые сцены.")]
        [SerializeField] private List<string> managedScenes = new List<string> { "SampleScene" };

        private static SceneResetter instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Единственный хост-объект живёт всю игру между загрузками сцен.
            if (instance != null) return;
            GameObject go = new GameObject(HostName);
            DontDestroyOnLoad(go);
            go.AddComponent<SceneResetter>();
        }

        private void OnEnable()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private bool IsManaged(string sceneName)
        {
            if (managedScenes == null || managedScenes.Count == 0) return true;
            for (int i = 0; i < managedScenes.Count; i++)
                if (!string.IsNullOrEmpty(managedScenes[i]) && managedScenes[i] == sceneName)
                    return true;
            return false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsManaged(scene.name)) return;

            // Статические флаги UI переживают LoadScene. Возвращаем их в
            // состояние «первого запуска» ДО того, как игровые скрипты успеют
            // их прочитать: иначе TerminalUI навсегда зафиксирует старый эталон
            // панели, а UiState.menuBlocked оставит игрока без управления.
            TerminalUI.AllowRebase();
            UiState.ResetMenuState();

            StartCoroutine(RestorePlayabilityNextFrame());
        }

        private System.Collections.IEnumerator RestorePlayabilityNextFrame()
        {
            // Ждём один кадр: к этому моменту все Awake()/Start() freshly
            // loaded сцены отработали — стартовая логика создала панели,
            // PlayerRespawn зафиксировал стартовую позицию. Дальше только
            // подстраховка поверх корректного состояния из файла сцены.
            yield return null;

            RestorePlayerControl();
            ReattachTerminalPanels();
        }

        /// <summary>
        /// Возвращает игроку полный функционал первого запуска: движение,
        /// обзор, ЛКМ/ПКМ (рука), инвентарь, запертый курсор, полное здоровье.
        /// </summary>
        private static void RestorePlayerControl()
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            player.SetControlEnabled(true);
            player.SetLookEnabled(true);

            Health health = player.GetComponent<Health>();
            if (health != null) health.ResetToFull();

            // Курсор — как при свежем запуске (lockCursor=true в сцене).
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// Перепривязывает существующие панели терминалов к терминалам текущей
        /// сцены. Вызывается ровно один раз после загрузки — в этот момент
        /// терминалы ещё никого не «питят» (Update включит видимость только
        /// когда игрок войдёт в зону), поэтому SetVisible(false) безопасен, а
        /// следующий Update построит rig заново и посадит его точно на место,
        /// как при первом запуске.
        /// </summary>
        private static void ReattachTerminalPanels()
        {
            TerminalUI[] panels = Object.FindObjectsByType<TerminalUI>(FindObjectsInactive.Include);
            for (int i = 0; i < panels.Length; i++)
            {
                TerminalUI panel = panels[i];
                if (panel == null) continue;
                panel.RebindAfterSceneLoad();
            }

            // Страховка на случай, если хост-объект сцены был активен и в
            // предыдущем забеге: sceneLoaded тогда не переснимал эталон панели.
            // Теперь снимок берётся с freshly loaded объекта из файла сцены —
            // ровно таким, каким терминал настроен в проекте.
            TerminalUI.CaptureFromSceneObject();
        }
    }
}
