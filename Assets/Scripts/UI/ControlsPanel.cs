using System.Collections.Generic;
using CarGame.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Меню переназначения управления. UI собирается в редакторе инструментом
    /// Tools/CarGame/Menus/... — здесь только связи: заполнить кнопки текущими
    /// клавишами и провести переназначение.
    ///
    /// Клик по кнопке клавиши открывает модальное окно с таймером: следующее
    /// нажатие клавиши или кнопки мыши становится новой привязкой. Если за
    /// отведённое время ничего не нажать (или нажать Esc) — окно закрывается,
    /// привязка не меняется. Всё сразу сохраняется в PlayerPrefs.
    ///
    /// У самого меню своего фона нет: позади висит общий DimLayer, который
    /// затемняет то, что за меню. На время работы меню ставится пауза.
    /// </summary>
    [DisallowMultipleComponent]
    public class ControlsPanel : MonoBehaviour
    {
        /// <summary>Строка бинда: название действия и кнопка с текущей клавишей.</summary>
        [System.Serializable]
        public sealed class BindRow
        {
            public GameAction Action;
            public TMP_Text Label;
            public TMP_Text KeyText;
            public Button KeyButton;
        }

        [Header("Ссылки (заполняет инструмент сборки меню)")]
        [Tooltip("Слой затемнения позади меню. Включается вместе с меню")]
        [SerializeField] private GameObject dimLayer;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button backButton;

        [Header("Строки биндов")]
        [SerializeField] private List<BindRow> rows = new List<BindRow>();

        [Header("Окно переназначения")]
        [Tooltip("Модальное окно, скрытое по умолчанию")]
        [SerializeField] private GameObject rebindWindow;
        [Tooltip("Текст с названием переназначаемого действия")]
        [SerializeField] private TMP_Text rebindActionText;
        [Tooltip("Полоска обратного отсчёта: Image с Image Type = Filled, Fill Method = Horizontal")]
        [SerializeField] private Image rebindTimerFill;
        [Tooltip("Сколько секунд ждём нажатие клавиши, сек")]
        [SerializeField] private float rebindSeconds = 5f;

        private static readonly HashSet<string> IgnoredKeys = new HashSet<string>
        {
            "leftShift", "rightShift", "leftCtrl", "rightCtrl", "leftAlt", "rightAlt",
            "leftMeta", "rightMeta", "capsLock", "numLock", "scrollLock", "printScreen"
        };

        /// <summary>Почти непрозрачный чёрный фон: за меню не должно быть видно других окон.</summary>
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.92f);

        private bool wired;
        private bool listening;
        private BindRow listenRow;
        private GameAction listenAction;
        private float listenRemaining;
        private int listenStartFrame;
        private float savedTimeScale = 1f;

        /// <summary>Открыто ли меню управления. Пока открыто, Esc не должен снимать паузу.</summary>
        public static bool IsOpen { get; private set; }

        private void Awake()
        {
            Wire();
        }

        private void OnEnable()
        {
            IsOpen = true;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            if (dimLayer != null)
            {
                dimLayer.SetActive(true);
                Image dimImage = dimLayer.GetComponent<Image>();
                if (dimImage != null) dimImage.color = DimColor;
            }

            // Прячем остальные меню: за управлением не должно просвечивать
            // ни настроек, ни паузы, ни главного меню.
            MenuManager.HideOthers(transform, dimLayer);

            CloseRebindWindow();

            listening = false;
            listenRow = null;

            Localization.Changed += RefreshLabels;
            RefreshLabels();
        }

        private void OnDisable()
        {
            IsOpen = false;
            Localization.Changed -= RefreshLabels;

            listening = false;
            listenRow = null;
            CloseRebindWindow();

            MenuManager.Restore();

            if (dimLayer != null) dimLayer.SetActive(false);

            Time.timeScale = savedTimeScale;
        }

        private void OnDestroy()
        {
            Localization.Changed -= RefreshLabels;
        }

        private void Wire()
        {
            if (wired) return;
            wired = true;

            if (resetButton != null) resetButton.onClick.AddListener(OnResetClicked);
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);

            List<BindRow> list = rows;
            for (int i = 0; i < list.Count; i++)
            {
                BindRow row = list[i];
                if (row == null || row.KeyButton == null) continue;

                BindRow captured = row;
                row.KeyButton.onClick.AddListener(() => StartListening(captured));
            }
        }

        private void Update()
        {
            if (!listening) return;

            // Кадр, в котором кнопку нажали мышкой, пропускаем: иначе эта же
            // ЛКМ тут же засчиталась бы как новая привязка.
            if (Time.frameCount == listenStartFrame) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                StopListening();
                return;
            }

            listenRemaining -= Time.unscaledDeltaTime;
            if (rebindTimerFill != null)
                rebindTimerFill.fillAmount = Mathf.Clamp01(listenRemaining / Mathf.Max(0.01f, rebindSeconds));

            ButtonControl pressed = NextPressedControl();
            if (pressed != null)
            {
                Assign(pressed.path);
                return;
            }

            if (listenRemaining <= 0f)
                StopListening();
        }

        private void StartListening(BindRow row)
        {
            if (row == null || row.KeyButton == null) return;

            StopListening();

            listening = true;
            listenRow = row;
            listenAction = row.Action;
            listenRemaining = rebindSeconds;
            listenStartFrame = Time.frameCount;

            if (rebindActionText != null)
                rebindActionText.text = RebindTitle(row.Action);

            if (rebindTimerFill != null) rebindTimerFill.fillAmount = 1f;
            if (rebindWindow != null) rebindWindow.SetActive(true);

            RefreshLabels();
        }

        private void StopListening()
        {
            if (!listening) return;

            listening = false;
            listenRow = null;
            CloseRebindWindow();
            RefreshLabels();
        }

        private void Assign(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                StopListening();
                return;
            }

            // Если клавиша уже занята другим действием — обмениваем привязки,
            // чтобы две команды не оказались на одной кнопке.
            GameAction conflict;
            if (KeyBindings.TryFindConflict(path, listenAction, out conflict))
            {
                string displaced = KeyBindings.GetPath(listenAction);
                KeyBindings.SetPath(conflict, displaced);
            }

            KeyBindings.SetPath(listenAction, path);

            listening = false;
            listenRow = null;
            CloseRebindWindow();
            RefreshLabels();
        }

        private void CloseRebindWindow()
        {
            if (rebindWindow != null) rebindWindow.SetActive(false);
        }

        private void RefreshLabels()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                BindRow row = rows[i];
                if (row == null) continue;

                if (row.Label != null)
                    row.Label.text = KeyBindings.GetDisplayName(row.Action);

                if (row.KeyText != null)
                    row.KeyText.text = listening && row == listenRow
                        ? "…"
                        : KeyBindings.GetDisplayBinding(row.Action);
            }
        }

        private static string RebindTitle(GameAction action)
        {
            return Localization.Keys("Переназначение: ", "Rebinding: ") + KeyBindings.GetDisplayName(action);
        }

        private void OnResetClicked()
        {
            KeyBindings.ResetToDefaults();
            RefreshLabels();
        }

        private void OnBackClicked()
        {
            StopListening();
            gameObject.SetActive(false);
        }

        private static ButtonControl NextPressedControl()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                ReadOnlyArray<KeyControl> keys = keyboard.allKeys;
                for (int i = 0; i < keys.Count; i++)
                {
                    KeyControl key = keys[i];
                    if (key == null || !key.wasPressedThisFrame) continue;
                    if (IgnoredKeys.Contains(key.name)) continue;
                    return key;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                ButtonControl[] candidates =
                {
                    mouse.leftButton, mouse.rightButton, mouse.middleButton, mouse.backButton, mouse.forwardButton
                };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] != null && candidates[i].wasPressedThisFrame) return candidates[i];
                }
            }

            return null;
        }
    }
}
