using System.Collections;
using System.Collections.Generic;
using CarGame.Audio;
using CarGame.Controls;
using CarGame.Items;
using CarGame.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace CarGame.UI
{
    /// <summary>
    /// Панель терминала-обменника (переработчика): иконки всех положенных
    /// предметов, их количество и суммарная стоимость, а внизу — кнопки
    /// ПОДТВЕРДИТЬ и ОТМЕНИТЬ. Подтверждение превращает содержимое буфера в
    /// золотые болты, отмена выбрасывает предметы физическими копиями из точки
    /// выдачи.
    ///
    /// Панель висит на ScreenPoint терминала в мировом пространстве (как HUD
/// магазина) и ловит ЛКМ сама: прицел на кнопке — панель нажимается, а
        /// PlayerFirearm в это время не стреляет (флаг AimingButton).
    ///
    /// Панель сама находит терминал, в ActiveTrigger которого стоит игрок.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExchangerTerminalUI : MonoBehaviour
    {
        private sealed class Slot
        {
            public RectTransform Rect;
            public RawImage Icon;
            public TextMeshProUGUI PriceLabel;
            public TextMeshProUGUI CountLabel;
        }

        private enum ButtonKind
        {
            Confirm,
            Cancel
        }

        private sealed class PanelButton
        {
            public RectTransform Rect;
            public Image Frame;
            public TextMeshProUGUI Label;
            public ButtonKind Kind;
            public float Flash;
        }

        [Header("Терминал")]
        [Tooltip("Терминал, к которому привязана панель. Пусто — панель сама найдёт тот, в ActiveTrigger которого стоит игрок")]
        [SerializeField] private ExchangerTerminal terminal;

        [Header("Камера")]
        [Tooltip("Камера, от которой панель отстраивается. Если пусто — берётся камера прицела")]
        [SerializeField] private Camera aimCamera;

        [Header("Размер панели")]
        [Tooltip("Ширина окна панели в метрах. У каждого обменника своя — подгоняется под его модель")]
        [SerializeField] private float windowWidth = TerminalLayout.WindowWidth;

        [Tooltip("Высота окна панели в метрах. У каждого обменника своя — подгоняется под его модель")]
        [SerializeField] private float windowHeight = TerminalLayout.WindowHeight;

        [Tooltip("Сколько слотов в ряду")]
        [SerializeField] private int columns = 4;

        [Header("Положение")]
        [Tooltip("Насколько центр панели выдвинут из терминала по его локальной оси Z, метры")]
        [SerializeField] private float standOff = 0.06f;

        [Tooltip("Аварийный переворот панели")]
        [SerializeField] private bool flipPanel;

        [Header("Тексты")]
        [SerializeField] private string titleText = "EXCHANGER";
        [SerializeField] private string totalText = "TOTAL";
        [SerializeField] private string confirmText = "CONFIRM";
        [SerializeField] private string cancelText = "CANCEL";
        [SerializeField] private string emptyText = "EMPTY";

        [Header("Цвета")]
        [SerializeField] private Color backdropColor = new Color(0.03f, 0.05f, 0.08f, 0.88f);
        [SerializeField] private Color frameColor = new Color(0.12f, 0.16f, 0.22f, 0.85f);
        [SerializeField] private Color hoverColor = new Color(0.35f, 0.78f, 0.95f, 0.95f);
        [SerializeField] private Color confirmColor = new Color(0.24f, 0.62f, 0.34f, 0.95f);
        [SerializeField] private Color cancelColor = new Color(0.62f, 0.3f, 0.28f, 0.95f);
        [SerializeField] private Color disabledColor = new Color(0.18f, 0.2f, 0.24f, 0.85f);
        [SerializeField] private Color textColor = new Color(0.86f, 0.9f, 0.94f, 1f);
        [SerializeField] private Color titleColor = new Color(0.35f, 0.78f, 0.95f, 1f);
        [SerializeField] private Color priceColor = new Color(0.98f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color countColor = new Color(0.62f, 0.7f, 0.78f, 1f);
        [SerializeField] private Color emptyColor = new Color(0.5f, 0.55f, 0.6f, 1f);

        [Header("Слот")]
        [Tooltip("Доля кнопки-слота под иконку предмета")]
        [SerializeField] private float iconPadding = 1.0f;

        [Header("Музыка интерфейса")]
        [Tooltip("Музыка, играющая, пока панель на экране. Пусто — процедурный цикл из ProceduralAudio")]
        [SerializeField] private AudioClip interfaceMusic;

        [Tooltip("Зацикливать музыку панели")]
        [SerializeField] private bool loopMusic = true;

        [Tooltip("Громкость музыки панели")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.4f;

        [Tooltip("Длительность нарастания и затухания музыки, с")]
        [SerializeField] private float musicFade = 0.35f;

        private AudioSource music;
        private AudioClip generatedMusic;
        private Coroutine musicFadeRoutine;

        private ExchangerTerminal builtFor;
        private int builtRevision = -1;

        private Transform rig;
        private RectTransform canvasRect;
        private RectTransform backdrop;
        private RectTransform content;

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<PanelButton> buttons = new List<PanelButton>();
        private TextMeshProUGUI totalLabel;
        private TextMeshProUGUI emptyLabel;

        private bool visible;
        private bool aimingButton;
        private PlayerController player;
        private Vector2 screenCenter;

        private Quaternion panelRotation = Quaternion.identity;
        private PanelButton aimedButton;

        public static ExchangerTerminalUI Instance { get; private set; }

        /// <summary>Панель обменника сейчас на экране.</summary>
        public static bool IsOpen => Instance != null && Instance.visible;

        /// <summary>
        /// Прицел стоит на кнопке панели обменника. Тогда панель перехватывает
        /// ЛКМ (читается в PlayerFirearm).
        /// </summary>
        public static bool AimingButton => Instance != null && Instance.aimingButton;

        /// <summary>Создаёт панель, если её ещё нет.</summary>
        public static ExchangerTerminalUI Show(ExchangerTerminal owner)
        {
            ExchangerTerminalUI view = Instance;
            if (view == null)
            {
                view = Object.FindFirstObjectByType<ExchangerTerminalUI>(FindObjectsInactive.Include);
                if (view != null)
                {
                    Instance = view;
                }
                else
                {
                    GameObject go = new GameObject("ExchangerTerminalUI");
                    view = go.AddComponent<ExchangerTerminalUI>();
                }
            }

            if (owner != null && view != null && view.terminal == null) view.terminal = owner;
            return view;
        }

        /// <summary>Привязывает панель к терминалу. Вызывается из редакторского меню.</summary>
        public void Attach(ExchangerTerminal owner)
        {
            terminal = owner;
            builtFor = null;
            builtRevision = -1;
        }

        private void OnEnable()
        {
            Instance = this;

            // Кнопки обмена: белая рамка без заливки в покое, белая заливка под
            // курсором, красная вспышка при нажатии — как у остальных кнопок.
            frameColor = TerminalTheme.IdleCell;
            hoverColor = TerminalTheme.HoverCell;

            // Процедурный трек синтезируем заранее, как магазин: генерация
            // двух секунд звука заметна, если оставить её на момент показа.
            if (interfaceMusic == null && generatedMusic == null)
                generatedMusic = CarGame.Audio.ProceduralAudio.ExchangeLoop();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            StopMusic();
        }

        private void OnDestroy()
        {
            if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (ResolveCamera() == null) return;

            HandleDebugKeys();

            screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            player = FindFirstObjectByType<PlayerController>();

            // Переключаемся на тот терминал, в ActiveTrigger которого игрок стоит.
            ExchangerTerminal nearest = FindNearestExchanger();
            if (nearest != null) terminal = nearest;

            bool inside = terminal != null && terminal.PlayerInsideActive;
            if (!inside && terminal != null && terminal.ActiveTrigger == null) inside = InRange(terminal);

            if (!inside)
            {
                aimingButton = false;
                aimedButton = null;
                SetVisible(false);
                return;
            }

            if (builtFor != terminal || rig == null || builtRevision != terminal.Revision) Build();

            SetVisible(true);
            if (!visible) return;

            UpdateRig();

            // Прицел и клик — одно значение на кадр, чтобы подсвеченное и
            // нажатое всегда совпадали.
            aimedButton = AimedButton();

            UpdateButtons();

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) HandleClick();
        }

        private void UpdateRig()
        {
            if (rig == null || terminal == null) return;

            Transform point = terminal.ScreenPoint;
            if (point == null) point = terminal.transform;

            if (rig.parent != terminal.transform) rig.SetParent(terminal.transform, false);

            rig.localPosition = terminal.transform.InverseTransformPoint(point.position);
            rig.localPosition += point.localRotation * new Vector3(0f, 0f, standOff);

            Vector3 lossy = terminal.transform.lossyScale;
            rig.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

            rig.rotation = panelRotation;
        }

        /// <summary>Разворот — ровно как в панелях спавна и магазина: -Z смотрит на триггер.</summary>
        private void ResolveFacing()
        {
            Vector3 dir = Vector3.zero;

            Transform trigger = terminal != null && terminal.ActiveTrigger != null ? terminal.ActiveTrigger.transform : null;
            if (trigger != null)
            {
                Vector3 forward = trigger.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f) dir = forward.normalized;
            }

            if (dir.sqrMagnitude <= 0.0001f && terminal != null)
            {
                Vector3 forward = terminal.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f) dir = forward.normalized;
            }

            if (dir.sqrMagnitude <= 0.0001f) dir = Vector3.forward;

            Vector3 back = flipPanel ? dir : -dir;
            panelRotation = Quaternion.LookRotation(back, Vector3.up);
        }

        private void HandleDebugKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!KeyBindings.WasPressedThisFrame(GameAction.TerminalExit)) return;

            flipPanel = !flipPanel;
            ResolveFacing();
            Debug.Log("[ExchangerTerminalUI] Панель развёрнута " + (flipPanel ? "ОТ" : "К") + " триггеру. F1 — перевернуть.", this);
        }

        private void SetVisible(bool on)
        {
            if (visible == on) return;

            visible = on;
            if (rig != null) rig.gameObject.SetActive(on);
            if (terminal != null) terminal.SetPowered(on);

            // Музыка живёт ровно вместе с панелью: SetVisible — единственное
            // место, где видно показ/скрытие, а проверка visible == on выше
            // отсекает повторы, чтобы трек не перезапускался каждый кадр.
            if (on) StartMusic();
            else
            {
                StopMusic();
                UiState.OnTerminalClosed();
            }
        }

        /// <summary>
        /// Динамик терминала: отдельный дочерний объект, звучит из точки панели
        /// и проходит через фильтры радио.
        /// </summary>
        private AudioSource EnsureMusic()
        {
            AudioSource source = TerminalSpeaker.Ensure(this, ref music);
            if (source != null) source.loop = loopMusic;
            return source;
        }

        private AudioClip ResolveMusicClip()
        {
            if (interfaceMusic != null) return interfaceMusic;

            if (generatedMusic == null) generatedMusic = CarGame.Audio.ProceduralAudio.ExchangeLoop();
            return generatedMusic;
        }

        private void StartMusic()
        {
            AudioClip clip = ResolveMusicClip();
            if (clip == null) return;

            AudioSource source = EnsureMusic();
            if (source == null) return;

            source.loop = loopMusic;
            source.clip = clip;
            TerminalSpeaker.PlaceAt(source, terminal != null ? terminal.transform : null);

            if (!source.isPlaying) source.Play();
            FadeMusic(source, Mathf.Clamp01(musicVolume));
        }

        private void StopMusic()
        {
            if (music == null || !music.isPlaying) return;

            FadeMusic(music, 0f);
        }

        private void FadeMusic(AudioSource source, float target)
        {
            if (musicFadeRoutine != null)
            {
                StopCoroutine(musicFadeRoutine);
                musicFadeRoutine = null;
            }

            // OnDisable гасит музыку, когда HUD уже выключен, и запускать
            // корутину на неактивном объекте нельзя — Unity сыплет
            // "Coroutine couldn't be started...". В этом случае меняем
            // громкость сразу, без плавного затухания.
            if (!isActiveAndEnabled)
            {
                source.volume = target;
                if (target <= 0.0001f && source.isPlaying) source.Stop();
                return;
            }

            musicFadeRoutine = StartCoroutine(FadeRoutine(source, target));
        }

        private System.Collections.IEnumerator FadeRoutine(AudioSource source, float target)
        {
            float from = source.volume;
            float duration = Mathf.Max(0.01f, musicFade);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            source.volume = target;
            if (target <= 0.0001f && source.isPlaying) source.Stop();
            musicFadeRoutine = null;
        }

        private void HandleClick()
        {
            PanelButton button = aimedButton;
            if (button == null && Cursor.visible) button = ButtonUnderPointer();
            if (button == null) return;
            if (terminal == null || terminal.Count == 0) return;

            bool done = button.Kind == ButtonKind.Confirm ? terminal.Confirm() : terminal.Cancel();
            if (done) button.Flash = 0.35f;
        }

        private PanelButton AimedButton()
        {
            return ButtonAt(screenCenter);
        }

        private PanelButton ButtonAt(Vector2 point)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                PanelButton button = buttons[i];
                if (button.Rect == null || !button.Rect.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(button.Rect, point, aimCamera)) continue;
                return button;
            }

            return null;
        }

        private PanelButton ButtonUnderPointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return null;
            return ButtonAt(mouse.position.ReadValue());
        }

        private void UpdateButtons()
        {
            aimingButton = aimedButton != null;

            bool hasItems = terminal != null && terminal.Count > 0;
            float delta = Time.deltaTime;

            for (int i = 0; i < buttons.Count; i++)
            {
                PanelButton button = buttons[i];
                if (button.Frame == null) continue;

                Color baseColor = hasItems ? TerminalTheme.IdleCell : new Color(0.5f, 0.5f, 0.5f, 0.5f);

                bool hovered = ReferenceEquals(button, aimedButton) && hasItems;

                Color color = hovered ? TerminalTheme.HoverCell : baseColor;

                if (button.Flash > 0f)
                {
                    button.Flash -= delta;
                    float pulse = Mathf.Clamp01(button.Flash / 0.35f);
                    color = Color.Lerp(color, TerminalTheme.PressCell, pulse);
                }

                button.Frame.color = color;
            }
        }

        private bool InRange(ExchangerTerminal candidate)
        {
            Vector3 origin = player != null ? player.transform.position : aimCamera.transform.position;
            return Vector3.Distance(origin, candidate.transform.position) <= candidate.ShowDistance;
        }

        private ExchangerTerminal FindNearestExchanger()
        {
            ExchangerTerminal[] all = FindObjectsByType<ExchangerTerminal>(FindObjectsSortMode.None);
            if (all == null || all.Length == 0) return null;

            Vector3 origin = player != null ? player.transform.position : aimCamera.transform.position;

            ExchangerTerminal best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                ExchangerTerminal candidate = all[i];
                if (candidate == null) continue;

                bool active = candidate.PlayerInsideActive ||
                              (candidate.ActiveTrigger == null && InRange(candidate));
                if (!active) continue;

                float distance = Vector3.Distance(origin, candidate.transform.position);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private void Build()
        {
            ClearContent();

            if (ResolveCamera() == null) return;

            EnsureRig();
            ResolveFacing();
            builtFor = terminal;
            builtRevision = terminal != null ? terminal.Revision : -1;

            if (terminal == null) return;

            IReadOnlyList<ExchangerTerminal.Pending> items = terminal.PendingItems;
            int count = items.Count;

            int cols = Mathf.Max(1, columns);
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));

            const float titleUnits = 0.9f;
            const float totalUnits = 0.6f;
            const float buttonUnits = 1.0f;
            const float gapUnits = 0.15f;

            float rowUnits = titleUnits + gapUnits + rows + gapUnits + totalUnits + gapUnits + buttonUnits;

            // Размер окна задан полями самой панели (windowWidth/windowHeight) —
            // у каждого обменника он свой, чтобы окно село на его модель. Шаг
            // сетки выводится так, чтобы содержимое вписалось в окно целиком.
            float pitch = Mathf.Max(0.01f, Mathf.Min(
                windowWidth / cols,
                windowHeight / Mathf.Max(1f, rowUnits)));
            float width = windowWidth;
            float height = windowHeight;
            float contentW = cols * pitch;
            float contentH = rowUnits * pitch;

            canvasRect.sizeDelta = new Vector3(width, height, 1f);
            backdrop.sizeDelta = new Vector2(width, height);

            float pad = pitch * 0.3f;
            float y = contentH * 0.5f;

            // Заголовок.
            CreateLabel("Title", TerminalTexts.L(titleText), width * 0.7f, titleUnits * pitch * 0.7f,
                0f, y - titleUnits * pitch * 0.5f, titleColor, content, TextAlignmentOptions.Center);
            y -= (titleUnits + gapUnits) * pitch;

            // Сетка иконок предметов.
            if (count == 0)
            {
                emptyLabel = CreateLabel("Empty", TerminalTexts.L(emptyText), width * 0.9f, pitch * 0.6f,
                    0f, y - pitch * 0.5f, emptyColor, content, TextAlignmentOptions.Center);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;

                    float x = -contentW * 0.5f + pitch * (col + 0.5f);
                    float slotY = y - pitch * (row + 0.5f);

                    CreateItemSlot(items[i], new Vector2(x, slotY), pitch);
                }
            }
            y -= (rows + gapUnits) * pitch;

            // Суммарная стоимость.
            totalLabel = CreateLabel("Total", TerminalTexts.L(totalText) + ": " + terminal.TotalValue.ToString(),
                width * 0.9f, totalUnits * pitch * 0.7f, 0f, y - totalUnits * pitch * 0.5f,
                priceColor, content, TextAlignmentOptions.Center);
            y -= (totalUnits + gapUnits) * pitch;

            // Кнопки подтверждения и отмены.
            float buttonW = Mathf.Max(0.01f, (contentW - pad * 4f) * 0.5f);
            float by = y - buttonUnits * pitch * 0.5f;

            CreateButton(ButtonKind.Confirm, TerminalTexts.L(confirmText), -buttonW * 0.5f - pad * 0.5f, by, buttonW, buttonUnits * pitch);
            CreateButton(ButtonKind.Cancel, TerminalTexts.L(cancelText), buttonW * 0.5f + pad * 0.5f, by, buttonW, buttonUnits * pitch);
        }

        private void CreateButton(ButtonKind kind, string text, float x, float y, float width, float height)
        {
            float frameH = height * 0.7f;

            GameObject frameGo = new GameObject(kind.ToString(), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGo.layer = gameObject.layer;

            RectTransform rect = frameGo.GetComponent<RectTransform>();
            rect.SetParent(content, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, frameH);
            rect.anchoredPosition = new Vector2(x, y);

            Image frame = frameGo.GetComponent<Image>();
            frame.color = kind == ButtonKind.Confirm ? confirmColor : cancelColor;
            frame.raycastTarget = false;
            TerminalTheme.ApplyButton(frame);

            TextMeshProUGUI label = CreateLabel("Label", text, width * 0.94f, frameH * 0.7f,
                0f, 0f, textColor, rect, TextAlignmentOptions.Center);

            PanelButton button = new PanelButton
            {
                Rect = rect,
                Frame = frame,
                Label = label,
                Kind = kind
            };

            buttons.Add(button);
        }

        private void CreateItemSlot(ExchangerTerminal.Pending item, Vector2 position, float pitch)
        {
            GameObject rootGo = new GameObject("ItemSlot", typeof(RectTransform));
            rootGo.layer = gameObject.layer;

            RectTransform rect = rootGo.GetComponent<RectTransform>();
            rect.SetParent(content, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(pitch, pitch);
            rect.anchoredPosition = position;

            float frameSide = pitch * 0.86f;
            float labelH = pitch * 0.2f;

            GameObject frameGo = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGo.layer = gameObject.layer;

            RectTransform frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.SetParent(rect, false);
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.sizeDelta = new Vector2(frameSide, frameSide);
            frameRect.anchoredPosition = new Vector2(0f, pitch * 0.04f);

            Image frame = frameGo.GetComponent<Image>();
            frame.color = frameColor;
            frame.raycastTarget = false;
            TerminalTheme.ApplyCell(frame);

            float iconSide = Mathf.Max(0.001f, frameSide * 0.8f);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            iconGo.layer = gameObject.layer;

            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSide, iconSide);
            iconRect.anchoredPosition = new Vector2(0f, pitch * 0.04f);

            RawImage icon = iconGo.GetComponent<RawImage>();
            icon.color = Color.white;
            icon.raycastTarget = false;

            if (item != null && item.Def != null)
            {
                ItemPreviewService previews = ItemPreviewService.Instance;
                if (previews != null)
                {
                    Texture2D texture = previews.Get(item.Def, iconPadding);
                    if (texture != null) icon.texture = texture;
                }
            }

            TextMeshProUGUI count = CreateLabel(
                "Count", item != null ? "x" + item.Amount : string.Empty,
                pitch * 0.9f, labelH, 0f, pitch * 0.5f - labelH * 0.5f, countColor, rect, TextAlignmentOptions.Center);

            TextMeshProUGUI price = CreateLabel(
                "Price", item != null ? item.Value.ToString() : string.Empty,
                pitch * 0.9f, labelH, 0f, -pitch * 0.5f + labelH * 0.5f, priceColor, rect, TextAlignmentOptions.Center);

            Slot slot = new Slot
            {
                Rect = rect,
                Icon = icon,
                PriceLabel = price,
                CountLabel = count
            };

            slots.Add(slot);
        }

        private TextMeshProUGUI CreateLabel(string name, string text, float width, float height, float x, float y,
            Color color, RectTransform parent, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent != null ? parent : content, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);

            float maxFont = Mathf.Max(0.005f, height * 0.8f);

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = color;
            label.alignment = alignment;
            TerminalTheme.ApplyFont(label);
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = maxFont * 0.25f;
            label.fontSizeMax = maxFont;
            label.fontSize = maxFont;
            label.overflowMode = TextOverflowModes.Truncate;

            return label;
        }

        /// <summary>Панель — часть терминала: цепляется к нему и берёт его слой.</summary>
        private void EnsureRig()
        {
            if (rig != null) return;

            GameObject rigGo = new GameObject("ExchangerPanel");
            rig = rigGo.transform;
            rigGo.layer = terminal != null ? terminal.gameObject.layer : gameObject.layer;
            rig.SetParent(terminal != null ? terminal.transform : null, false);

            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.transform.SetParent(rig, false);

            canvasRect = canvasGo.GetComponent<RectTransform>();

            GameObject backdropGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backdrop = backdropGo.GetComponent<RectTransform>();
            backdrop.SetParent(canvasRect, false);
            backdrop.anchorMin = new Vector2(0.5f, 0.5f);
            backdrop.anchorMax = new Vector2(0.5f, 0.5f);

            Image backdropImage = backdropGo.GetComponent<Image>();
            backdropImage.color = backdropColor;
            backdropImage.raycastTarget = false;
            TerminalTheme.ApplyBackdrop(backdropImage);
            NineSliceSkin.Apply(backdropImage);

            GameObject contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.layer = rigGo.layer;

            content = contentGo.GetComponent<RectTransform>();
            content.SetParent(canvasRect, false);
            content.anchorMin = new Vector2(0.5f, 0.5f);
            content.anchorMax = new Vector2(0.5f, 0.5f);

            rigGo.SetActive(visible);
        }

        private void ClearContent()
        {
            slots.Clear();
            buttons.Clear();
            totalLabel = null;
            emptyLabel = null;

            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Transform child = content.GetChild(i);
                if (child == null) continue;

                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private Camera ResolveCamera()
        {
            if (aimCamera != null) return aimCamera;

            PlayerInteractor interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor != null && interactor.AimCamera != null) return aimCamera = interactor.AimCamera;

            return aimCamera = Camera.main;
        }
    }
}