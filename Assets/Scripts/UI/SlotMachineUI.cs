using System.Collections.Generic;
using CarGame.Audio;
using CarGame.Controls;
using CarGame.Items;
using CarGame.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CarGame.UI
{
/// <summary>
    /// Панель автомата: ставка, три барабана и результат. Как и панели других
    /// терминалов, это физический дисплей, приваренный к точке экрана автомата,
    /// — не экранный оверлей. Никаких кнопок: барабаны крутит рычаг (клавиша E,
    /// см. SlotMachineLever), а панель только показывает.
    ///
    /// Барабаны — три «окна» (RawImage + RectMask2D) на одной полосе-текстуре:
    /// каждый динамик показывает ровно одну ячейку через uvRect. Полоса
    /// собирается один раз на автомат и состоит ТОЛЬКО из 2D-иконок — спрайтов
    /// (winSprite/luckUpSprite/lossSprite/boomSprite, назначаются в инспекторе —
    /// см. настройки HUD), а если спрайт не назначен, иконка рисуется
    /// процедурно. Никаких предметов или 3D-превью в слотах нет.
    ///
    /// Вращение — дискретное: барабан быстро щёлкает по случайным иконкам и
    /// замедляется квадратично (eased = 1-(1-t)^2), пока не встанет на целевую
    /// иконку. Цель панель не выбирает: автомат уже бросил исход (аннигиляция,
    /// джекпот, удвоение, удача, проигрыш) в TryPullLever, панель лишь
    /// раскручивает барабаны к иконкам PendingOutcome: аннигиляция — три «бума»,
    /// джекпот — три иконки джекпота, удвоение — две иконки джекпота + одна
    /// проигрыша, удача — три иконки LuckUp, проигрыш — три иконки проигрыша.
    /// После остановки панель зовёт terminal.FinishSpin() — так выплата и исход
    /// всегда согласованы.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotMachineUI : MonoBehaviour
    {
        private sealed class Reel
        {
            public RectTransform Window;
            public RectMask2D Mask;
            public RawImage Image;
            public float Timer;
            public float Duration;
            public int Target;
            public bool Done;
        }

        [Header("Автомат")]
        [Tooltip("Автомат, к которому привязана панель. Пусто — панель сама найдёт тот, в ActiveTrigger которого стоит игрок")]
        [SerializeField] private SlotMachineTerminal terminal;

        [Header("Камера")]
        [Tooltip("Камера, от которой панель отстраивается. Если пусто — берётся камера прицела")]
        [SerializeField] private Camera aimCamera;

        [Header("Размер панели")]
        [Tooltip("Ширина окна панели в метрах. У каждого автомата своя — подгоняется под его модель")]
        [SerializeField] private float windowWidth = TerminalLayout.WindowWidth;

        [Tooltip("Высота окна панели в метрах. У каждого автомата своя — подгоняется под его модель")]
        [SerializeField] private float windowHeight = TerminalLayout.WindowHeight;

        [Header("Положение")]
        [Tooltip("Насколько центр панели выдвинут из автомата по его локальной оси Z, метры")]
        [SerializeField] private float standOff = 0.06f;

        [Tooltip("Аварийный переворот панели, если лицевая сторона окажется не там")]
        [SerializeField] private bool flipPanel;

        [Header("Тексты")]
        [Tooltip("Заголовок автомата")]
        [SerializeField] private string titleText = "SLOTS";

        [Tooltip("Подпись ставки")]
        [SerializeField] private string betText = "BET";

        [Tooltip("Предупреждение обратного отсчёта")]
        [SerializeField] private string countdownText = "DANGER";

        [Tooltip("Подпись джекпота")]
        [SerializeField] private string jackpotText = "JACKPOT!";

        [Tooltip("Подпись удвоения ставки")]
        [SerializeField] private string winText = "DOUBLE!";

        [Tooltip("Подпись проигрыша")]
        [SerializeField] private string loseText = "NO LUCK";

        [Header("Цвета")]
        [SerializeField] private Color backdropColor = new Color(0.03f, 0.05f, 0.08f, 0.88f);
        [SerializeField] private Color frameColor = new Color(0.12f, 0.16f, 0.22f, 0.85f);
        [SerializeField] private Color textColor = new Color(0.86f, 0.9f, 0.94f, 1f);
        [SerializeField] private Color titleColor = new Color(0.35f, 0.78f, 0.95f, 1f);
        [SerializeField] private Color goldColor = new Color(0.98f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color winColor = new Color(0.42f, 0.9f, 0.5f, 1f);
        [SerializeField] private Color warnColor = new Color(1f, 0.35f, 0.3f, 1f);

        [Header("Барабаны")]
        [Tooltip("Доля окна барабана от шага сетки")]
        [SerializeField, Range(0.5f, 0.95f)] private float windowScale = 0.84f;

        [Tooltip("Сколько случайных проскоков до целевого символа")]
        [SerializeField, Range(3, 24)] private int spinSteps = 16;

        [Tooltip("Длительность вращения первого барабана, с")]
        [SerializeField, Range(0.5f, 3f)] private float spinDuration = 1.6f;

        [Tooltip("На сколько секунд дольше вращается каждый следующий барабан")]
        [SerializeField, Range(0f, 1f)] private float spinStagger = 0.6f;

        [Header("Звук вращения")]
        [Tooltip("Трек вращения барабанов, зацикленный. Пусто — процедурный из ProceduralAudio")]
        [SerializeField] private AudioClip spinClip;

        [Tooltip("Громкость трека вращения барабанов")]
        [SerializeField, Range(0f, 1f)] private float spinVolume = 0.5f;

        [Header("Музыка HUD")]
        [Tooltip("Фоновый трек панели автомата. Пусто — процедурный луп из ProceduralAudio")]
        [SerializeField] private AudioClip interfaceMusic;

        [Tooltip("Громкость фонового трека панели")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.4f;

        [Tooltip("Зацикливать фоновый трек")]
        [SerializeField] private bool loopMusic = true;

        [Tooltip("Длительность нарастания и затухания трека, с")]
        [SerializeField] private float musicFade = 0.35f;

        [Tooltip("До какой доли громкости приглушается музыка, пока автомат играет звук")]
        [SerializeField, Range(0f, 1f)] private float duckLevel = 0.25f;

        [Tooltip("Сколько музыка остаётся приглушённой после последнего звука автомата, с")]
        [SerializeField] private float duckHold = 0.25f;

        [Header("Иконки барабанов (2D-спрайты)")]
        [Tooltip("Спрайт иконки джекпота (3 иконки = 7x ставку). Пусто — процедурная семёрка")]
        [SerializeField] private Sprite winSprite;

        [Tooltip("Спрайт иконки удачи (3 иконки = +удача к X2 и джекпоту). Пусто — процедурный трилистник")]
        [SerializeField] private Sprite luckUpSprite;

        [Tooltip("Спрайт иконки проигрыша (3 иконки = ставка сгорает). Пусто — процедурный серый крест")]
        [SerializeField] private Sprite lossSprite;
        

        [Tooltip("Спрайт «бума» для аннигиляции (обратный отсчёт). Пусто — процедурный красный крест")]
        [SerializeField] private Sprite boomSprite;

        [Tooltip("Подпись бонуса удачи в блоке ставки")]
        [SerializeField] private string luckText = "LUCK";

        private AudioSource spinSource;
        private AudioClip generatedSpin;

        private AudioSource music;
        private AudioClip generatedMusic;
        private float musicVolumeCurrent;
        private float duckUntil;
        private int lastSfxCount;

        private readonly List<Reel> reels = new List<Reel>();

        private SlotMachineTerminal builtFor;

        private Texture2D stripTexture;
        private int stripCell = 256;
        private int winRow = -1;
        private int luckRow = -1;
        private int lossRow = -1;
        private int boomRow = -1;

        private Transform rig;
        private RectTransform canvasRect;
        private RectTransform backdrop;
        private RectTransform content;

        private TextMeshProUGUI betLabel;
        private TextMeshProUGUI resultLabel;

        private RectTransform countdownRoot;
        private TextMeshProUGUI countdownLabel;

        private bool visible;
        private bool spinPlaying;
        private bool resultShown;
        private int lastBet = -1;

        private PlayerController player;

        /// <summary>Разворот панели, посчитанный один раз: на сторону триггера.</summary>
        private Quaternion panelRotation = Quaternion.identity;

        public static SlotMachineUI Instance { get; private set; }

        /// <summary>Панель автомата сейчас на экране.</summary>
        public static bool IsOpen => Instance != null && Instance.visible;

        /// <summary>
        /// Создаёт панель, если её ещё нет. Видимость решается только входом в
        /// ActiveTrigger, поэтому вызов из Awake на неё не влияет.
        /// </summary>
        public static SlotMachineUI Show(SlotMachineTerminal owner)
        {
            SlotMachineUI view = Instance;
            if (view == null)
            {
                view = Object.FindFirstObjectByType<SlotMachineUI>(FindObjectsInactive.Include);
                if (view != null)
                {
                    Instance = view;
                }
                else
                {
                    GameObject go = new GameObject("SlotMachineUI");
                    view = go.AddComponent<SlotMachineUI>();
                }
            }

            if (owner != null && view != null && view.terminal == null) view.terminal = owner;
            return view;
        }

        /// <summary>
        /// Привязывает панель к автомату. Вызывается из редакторского меню:
        /// компонент лежит в сцене, поэтому его настройки видно в инспекторе.
        /// </summary>
        public void Attach(SlotMachineTerminal owner)
        {
            terminal = owner;
            builtFor = null;
        }

        private void OnEnable()
        {
            Instance = this;

            // Ячейки автомата: белая рамка без заливки — как у остальных ячеек.
            frameColor = TerminalTheme.IdleCell;

            // Треки синтезируем заранее: генерация двух секунд звука заметна,
            // если оставить её на момент показа панели.
            if (spinClip == null && generatedSpin == null) generatedSpin = CarGame.Audio.ProceduralAudio.SlotMachineSpin();
            if (interfaceMusic == null && generatedMusic == null) generatedMusic = CarGame.Audio.ProceduralAudio.SlotMachineLoop();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            StopSpinLoop();
            StopMusic();
        }

        private void Update()
        {
            if (ResolveCamera() == null) return;

            UpdateMusic(Time.unscaledDeltaTime);
            HandleDebugKeys();

            player = FindFirstObjectByType<PlayerController>();

            // Переключаемся на тот автомат, в ActiveTrigger которого игрок стоит.
            SlotMachineTerminal nearest = FindNearestSlot();
            if (nearest != null) terminal = nearest;

            bool inside = terminal != null && terminal.PlayerInsideActive;
            if (!inside && terminal != null && terminal.ActiveTrigger == null) inside = InRange(terminal);

            if (!inside || terminal == null)
            {
                SetVisible(false);
                return;
            }

            if (builtFor != terminal || rig == null) Build();

            SetVisible(true);
            if (!visible) return;

            UpdateRig();
            UpdateBetLabel();
            UpdateSpin();
            UpdateResultLabel();
            UpdateCountdown();
        }

        /// <summary>Панель — часть автомата: цепляется к нему и берёт его слой.</summary>
        private void EnsureRig()
        {
            if (rig != null) return;

            GameObject rigGo = new GameObject("SlotPanel");
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

        /// <summary>Разворот — ровно как в TerminalUI: -Z панели смотрит на триггер.</summary>
        private void ResolveFacing()
        {
            Vector3 dir = Vector3.zero;

            Transform trigger = terminal.ActiveTrigger != null ? terminal.ActiveTrigger.transform : null;
            if (trigger != null)
            {
                Vector3 forward = trigger.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f) dir = forward.normalized;
            }

            if (dir.sqrMagnitude <= 0.0001f)
            {
                Vector3 forward = terminal.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f) dir = forward.normalized;
            }

            if (dir.sqrMagnitude <= 0.0001f) dir = Vector3.forward;

            Vector3 back = flipPanel ? dir : -dir;
            panelRotation = Quaternion.LookRotation(back, Vector3.up);
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

        private void HandleDebugKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!KeyBindings.WasPressedThisFrame(GameAction.TerminalExit)) return;

            flipPanel = !flipPanel;
            ResolveFacing();
            Debug.Log("[SlotMachineUI] Панель развёрнута " + (flipPanel ? "ОТ" : "К") + " триггеру. F1 — перевернуть.", this);
        }

        private void SetVisible(bool on)
        {
            if (visible == on) return;

            visible = on;
            if (rig != null) rig.gameObject.SetActive(on);

            if (!on)
            {
                StopSpinLoop();
                UiState.OnTerminalClosed();
            }
        }

        private void Build()
        {
            ClearReels();

            if (ResolveCamera() == null) return;

            EnsureRig();
            ResolveFacing();
            builtFor = terminal;

            if (terminal == null) return;

            BuildStrip();

            // Вертикаль: заголовок, ставка, барабаны, результат.
            float rowUnits = 1f + 0.7f + 1.4f + 1f + 0.2f;

            // Размер окна задан полями самой панели (windowWidth/windowHeight) —
            // у каждого автомата он свой, чтобы окно село на его модель. Шаг
            // сетки выводится так, чтобы барабаны и надписи вписались целиком.
            float pitch = Mathf.Max(0.01f, Mathf.Min(
                windowWidth / (3f * windowScale + 0.3f),
                windowHeight / Mathf.Max(1f, rowUnits)));
            float height = windowHeight;
            float window = pitch * windowScale;
            float width = windowWidth;
            float contentW = 3f * window + 2f * pitch * 0.15f;
            float contentH = rowUnits * pitch;

            canvasRect.sizeDelta = new Vector3(width, height, 1f);
            backdrop.sizeDelta = new Vector2(width, height);

            float titleY = contentH * 0.5f - pitch * 0.5f;
            float betY = titleY - pitch * 0.7f;
            float windowY = betY - pitch * 0.5f - window * 0.5f;
            float resultY = windowY - window * 0.5f - pitch * 0.55f;

            CreateLabel("Title", TerminalTexts.L(titleText), width * 0.6f, pitch * 0.5f, 0f, titleY, titleColor);
            CreateLabel("Bet", TerminalTexts.L(betText) + " 0", width * 0.9f, pitch * 0.5f, 0f, betY, goldColor);
            betLabel = FindChild(content, "Bet").GetComponent<TextMeshProUGUI>();

            lastBet = -1;
            resultShown = false;

            for (int i = 0; i < 3; i++)
            {
                float x = -contentW * 0.5f + window * 0.5f + i * (window + pitch * 0.15f);
                CreateWindow(x, windowY, window);
            }
            for (int i = 0; i < reels.Count; i++)
            {
                SetReelCell(reels[i], i < boomRow ? i : boomRow);
            }

            resultLabel = CreateLabel("Result", string.Empty, width * 0.7f, pitch * 0.4f, 0f, resultY, winColor);
            resultLabel.gameObject.SetActive(false);

            var countdownRootGo = new GameObject("Countdown", typeof(RectTransform));
            countdownRootGo.layer = gameObject.layer;
            countdownRoot = countdownRootGo.GetComponent<RectTransform>();
            countdownRoot.SetParent(content, false);
            countdownRoot.anchorMin = new Vector2(0.5f, 0.5f);
            countdownRoot.anchorMax = new Vector2(0.5f, 0.5f);
            countdownRoot.sizeDelta = new Vector2(width, height);
            countdownRoot.anchoredPosition = Vector2.zero;

            countdownLabel = CreateLabel("CountdownText", TerminalTexts.L(countdownText), width * 0.7f, pitch * 0.5f, 0f, -pitch * 0.3f, warnColor, countdownRoot);
            countdownRootGo.gameObject.SetActive(false);
        }

        private TextMeshProUGUI CreateLabel(string name, string text, float width, float height, float x, float y, Color color, RectTransform parent = null)
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
            label.alignment = TextAlignmentOptions.Center;
            TerminalTheme.ApplyFont(label);
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = maxFont * 0.25f;
            label.fontSizeMax = maxFont;
            label.fontSize = maxFont;
            label.overflowMode = TextOverflowModes.Truncate;

            return label;
        }

        private Reel CreateWindow(float x, float y, float size)
        {
            GameObject rootGo = new GameObject("Reel" + reels.Count, typeof(RectTransform));
            rootGo.layer = gameObject.layer;

            RectTransform rect = rootGo.GetComponent<RectTransform>();
            rect.SetParent(content, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(x, y);

            RectMask2D mask = rootGo.AddComponent<RectMask2D>();
            mask.softness = Vector2Int.zero;

            GameObject frameGo = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGo.layer = gameObject.layer;

            RectTransform frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.SetParent(rect, false);
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.sizeDelta = new Vector2(size, size);

            Image frame = frameGo.GetComponent<Image>();
            frame.color = frameColor;
            frame.raycastTarget = false;
            TerminalTheme.ApplyCell(frame);

            float iconSide = Mathf.Max(0.001f, size * 0.92f);

            GameObject stripGo = new GameObject("Strip", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            stripGo.layer = gameObject.layer;

            RectTransform stripRect = stripGo.GetComponent<RectTransform>();
            stripRect.SetParent(rect, false);
            stripRect.anchorMin = new Vector2(0.5f, 0.5f);
            stripRect.anchorMax = new Vector2(0.5f, 0.5f);
            stripRect.sizeDelta = new Vector2(iconSide, iconSide);

            RawImage image = stripGo.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            if (stripTexture != null) image.texture = stripTexture;

            Reel reel = new Reel
            {
                Window = rect,
                Mask = mask,
                Image = image,
                Done = true
            };
            reels.Add(reel);
            return reel;
        }

        /// <summary>
        /// Полоса барабанов — ровно четыре 2D-иконки: джекпот, удача, проигрыш,
        /// «бум». Спрайт из настроек HUD (winSprite/luckUpSprite/lossSprite/
        /// boomSprite), если не назначен — процедурная иконка. Никаких предметов
        /// и 3D-превью в слотах нет. Собирается один раз на автомат; панель лишь
        /// режет её uvRect на ячейки.
        /// </summary>
        private void BuildStrip()
        {
            int cellW = 256;
            int cellH = 256;

            List<Texture2D> cells = new List<Texture2D>(4);

            Texture2D win = SpriteCell(winSprite, cellW, cellH);
            if (win == null) win = BuildWinTexture(cellW, cellH);
            cells.Add(win);
            winRow = 0;

            Texture2D luck = SpriteCell(luckUpSprite, cellW, cellH);
            if (luck == null) luck = BuildLuckTexture(cellW, cellH);
            cells.Add(luck);
            luckRow = 1;

            Texture2D loss = SpriteCell(lossSprite, cellW, cellH);
            if (loss == null) loss = BuildLossTexture(cellW, cellH);
            cells.Add(loss);
            lossRow = 2;

            Texture2D boom = SpriteCell(boomSprite, cellW, cellH);
            if (boom == null) boom = BuildBoomTexture(cellW, cellH);
            cells.Add(boom);
            boomRow = 3;

            stripCell = Mathf.Max(1, cellW);

            if (stripTexture != null) Destroy(stripTexture);
            stripTexture = new Texture2D(cellW, cellH * cells.Count, TextureFormat.RGBA32, false);
            stripTexture.name = "SlotMachineStrip" + (builtFor != null ? "_" + builtFor.name : "");
            stripTexture.filterMode = FilterMode.Bilinear;

            for (int r = 0; r < cells.Count; r++)
            {
                stripTexture.SetPixels32(0, r * cellH, cellW, cellH, cells[r].GetPixels32());
            }

            stripTexture.Apply(false);

            for (int i = 0; i < reels.Count; i++)
            {
                if (reels[i].Image == null) continue;
                reels[i].Image.texture = stripTexture;
                SetReelCell(reels[i], i < boomRow ? i : boomRow);
            }
        }

        /// <summary>
        /// Ячейка из спрайта. Кропается по rect спрайта. Текстуры без права
        /// чтения (а такие почти все импортированные спрайты) читаются через
        /// RenderTexture-копию, так что установленная иконка всегда попадает на
        /// барабан. Если даже это не вышло — null (хозяин возьмёт процедурную).
        /// </summary>
        private Texture2D SpriteCell(Sprite sprite, int w, int h)
        {
            if (sprite == null) return null;

            Texture2D source = sprite.texture;
            if (source == null) return null;

            Color32[] full = ReadablePixels(source);
            if (full == null || full.Length == 0) return null;

            Rect rect = sprite.rect;
            int sx = Mathf.Max(0, Mathf.FloorToInt(rect.x));
            int sy = Mathf.Max(0, Mathf.FloorToInt(rect.y));
            int sw = Mathf.Min(Mathf.Max(1, Mathf.FloorToInt(rect.width)), Mathf.Max(1, source.width - sx));
            int sh = Mathf.Min(Mathf.Max(1, Mathf.FloorToInt(rect.height)), Mathf.Max(1, source.height - sy));

            Color32[] pixels = new Color32[sw * sh];
            for (int y = 0; y < sh; y++)
            {
                int row = (sy + y) * source.width + sx;
                for (int x = 0; x < sw; x++) pixels[y * sw + x] = full[row + x];
            }
            if (sw == w && sh == h)
            {
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }

            return ScalePixels(pixels, sw, sh, w, h);
        }

        /// <summary>
        /// Пиксели текстуры в любом случае: читаемая текстура возвращает их
        /// сразу, нечитаемая прогоняется через RenderTexture (Graphics.Blit +
        /// ReadPixels) — так спрайт-иконка работает без включённого
        /// Read/Write у импортированного изображения.
        /// </summary>
        private Color32[] ReadablePixels(Texture2D source)
        {
            if (source == null) return null;

            try
            {
                if (source.isReadable) return source.GetPixels32();
            }
            catch (UnityException)
            {
                // Падает и на «читаемой»: идём через копию.
            }

            if (source.width <= 0 || source.height <= 0) return null;
            if (!SystemInfo.supportsRenderTextures) return null;

            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
                copy.Apply(false);
                return copy.GetPixels32();
            }
            catch (UnityException)
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Масштаб пикселей до ячейки полосы (ближайший сосед).</summary>
        private Texture2D ScalePixels(Color32[] pixels, int sw, int sh, int w, int h)
        {
            Color32[] scaled = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int py = Mathf.Min(sh - 1, y * sh / h);
                int row = py * sw;
                for (int x = 0; x < w; x++)
                {
                    int px = Mathf.Min(sw - 1, x * sw / w);
                    scaled[y * w + x] = pixels[row + px];
                }
            }

            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(scaled);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>Общий рисователь креста: фон + косой крест по всей клетке.</summary>
        private Texture2D BuildCrossTexture(int w, int h, Color32 background, Color32 cross)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            Color32[] pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = background;

            int left = Mathf.Max(1, w / 4);
            int right = w - left;
            int top = h - left;
            int bottom = left;
            float span = Mathf.Max(1f, right - left);
            float thickness = Mathf.Max(0.03f, w / 10f / span);

            for (int y = bottom; y < top; y++)
            {
                float v = (y - bottom) / (float)Mathf.Max(1, top - bottom);
                for (int px = left; px < right; px++)
                {
                    float u = (px - left) / span;
                    float d1 = Mathf.Abs(u - v);
                    float d2 = Mathf.Abs((1f - u) - v);
                    if (d1 < thickness || d2 < thickness) pixels[y * w + px] = cross;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Иконка «бума»: красный крест на тёмном фоне.</summary>
        private Texture2D BuildBoomTexture(int w, int h)
        {
            return BuildCrossTexture(w, h, new Color32(26, 18, 18, 255), new Color32(214, 66, 50, 255));
        }

        /// <summary>Иконка проигрыша: серый крест на тёмном фоне — отлично от красного «бума».</summary>
        private Texture2D BuildLossTexture(int w, int h)
        {
            return BuildCrossTexture(w, h, new Color32(24, 26, 30, 255), new Color32(96, 104, 112, 255));
        }

        /// <summary>Иконка джекпота: золотая семёрка на тёмном фоне.</summary>
        private Texture2D BuildWinTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            Color32[] pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(24, 26, 22, 255);

            Color32 seven = new Color32(238, 200, 74, 255);
            int thick = Mathf.Max(2, w / 10);

            int bandTop = Mathf.RoundToInt(h * 0.74f);
            int bandBottom = Mathf.RoundToInt(h * 0.56f);
            int bandLeft = Mathf.RoundToInt(w * 0.22f);
            int bandRight = w - bandLeft;

            for (int y = bandBottom; y <= bandTop && y < h; y++)
            {
                for (int px = bandLeft; px < bandRight; px++)
                {
                    pixels[y * w + px] = seven;
                }
            }

            int xTop = bandRight;
            int yTop = bandBottom;
            int xBot = Mathf.RoundToInt(w * 0.46f);
            int yBot = Mathf.RoundToInt(h * 0.18f);
            float span = Mathf.Max(1f, xBot - xTop);
            for (int px = xBot; px <= xTop; px++)
            {
                float t = (px - xTop) / span;
                int yc = yTop + Mathf.RoundToInt((yBot - yTop) * t);
                for (int dy = -thick; dy <= thick; dy++)
                {
                    int yy = yc + dy;
                    if (yy >= 0 && yy < h) pixels[yy * w + px] = seven;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Иконка удачи: четыре зелёных лепестка на тёмном фоне.</summary>
        private Texture2D BuildLuckTexture(int w, int h)
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            Color32[] pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(20, 28, 22, 255);

            Color32 leaf = new Color32(74, 214, 110, 255);
            int cx = w / 2;
            int cy = h / 2;
            int r = Mathf.Max(2, w / 5);
            int rr = r * r;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float ang = (Mathf.PI / 2f) * k;
                        int lx = cx + (int)Mathf.Round(Mathf.Cos(ang) * r);
                        int ly = cy + (int)Mathf.Round(Mathf.Sin(ang) * r);
                        int dx = x - lx;
                        int dy = y - ly;
                        if (dx * dx + dy * dy <= rr)
                        {
                            pixels[y * w + x] = leaf;
                            break;
                        }
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private int StripRows
        {
            get { return boomRow >= 0 ? boomRow + 1 : 0; }
        }

        private void SetReelCell(Reel reel, int row)
        {
            if (reel == null || reel.Image == null || stripTexture == null) return;
            if (row < 0) row = 0;

            float cell = Mathf.Max(1, stripCell);
            float height = Mathf.Max(0.001f, stripTexture.height);
            reel.Image.uvRect = new Rect(0f, (row * cell) / height, 1f, cell / height);
        }

        /// <summary>
        /// Раскрутка началась: цели барабанов — по исходу автомата. Панель не
        /// выбирает исход, она его показывает: аннигиляция — три «бума», джекпот —
        /// три иконки джекпота, удвоение — две иконки джекпота + одна проигрыша,
        /// удача — три иконки LuckUp, проигрыш — три иконки проигрыша.
        /// </summary>
        private void ResolveTargets(SlotMachineOutcome outcome, ItemDef winDef, List<Reel> targetReels)
        {
            int win = winRow;
            int luck = luckRow;
            int loss = lossRow;
            int boom = boomRow;

            if (outcome == SlotMachineOutcome.Jackpot)
            {
                for (int i = 0; i < targetReels.Count; i++) targetReels[i].Target = win;
                return;
            }

            if (outcome == SlotMachineOutcome.LuckUp)
            {
                for (int i = 0; i < targetReels.Count; i++) targetReels[i].Target = luck;
                return;
            }

            if (outcome == SlotMachineOutcome.Annihilation)
            {
                for (int i = 0; i < targetReels.Count; i++) targetReels[i].Target = boom;
                return;
            }

            if (outcome == SlotMachineOutcome.Double)
            {
                targetReels[0].Target = win;
                targetReels[1].Target = win;
                targetReels[2].Target = loss;
                return;
            }

            for (int i = 0; i < targetReels.Count; i++) targetReels[i].Target = loss;
        }

        private int RandomRow(int exclude, int rows)
        {
            if (rows <= 1) return 0;
            int row;
            do
            {
                row = Random.Range(0, rows);
            } while (row == exclude);
            return row;
        }

        private void UpdateSpin()
        {
            if (terminal == null) return;

            if (!terminal.IsSpinning)
            {
                if (spinPlaying)
                {
                    spinPlaying = false;
                    StopSpinLoop();
                }
                return;
            }

            if (!spinPlaying) BeginSpin();
            else AdvanceReels(Time.deltaTime);
        }

        private void BeginSpin()
        {
            if (terminal == null || stripTexture == null || reels.Count == 0) return;

            spinPlaying = true;
            resultShown = false;

            ResolveTargets(terminal.PendingOutcome, terminal.PendingWinDef, reels);

            for (int i = 0; i < reels.Count; i++)
            {
                Reel reel = reels[i];
                reel.Timer = 0f;
                reel.Duration = Mathf.Max(0.3f, spinDuration + i * spinStagger);
                reel.Done = false;
            }

            StartSpinLoop();
        }

        /// <summary>
        /// Дискретное вращение: барабан щёлкает по случайным символам и
        /// замедляется квадратично (eased = 1-(1-t)^2), пока не встанет на цель.
        /// Последние два шага целевые — символ не может «проскочить» мимо.
        /// </summary>
        private void AdvanceReels(float delta)
        {
            if (reels.Count == 0) return;

            int steps = Mathf.Max(2, spinSteps);
            bool allDone = true;

            for (int i = 0; i < reels.Count; i++)
            {
                Reel reel = reels[i];
                if (reel.Done) continue;

                reel.Timer += delta;
                float progress = Mathf.Clamp01(reel.Timer / Mathf.Max(0.1f, reel.Duration));
                float eased = 1f - (1f - progress) * (1f - progress);
                int step = Mathf.Min(steps, Mathf.FloorToInt(eased * steps));

                if (step >= steps - 2)
                {
                    reel.Done = true;
                    SetReelCell(reel, reel.Target);
                }
                else
                {
                    SetReelCell(reel, RandomRow(reel.Target, StripRows));
                }

                allDone &= reel.Done;
            }

            if (allDone)
            {
                spinPlaying = false;
                StopSpinLoop();
                if (terminal != null) terminal.FinishSpin(); 
            }
        }

        private void UpdateBetLabel()
        {
            if (betLabel == null || terminal == null) return;

            int total = terminal.TotalBetUnits;
            if (total == lastBet) return;
            lastBet = total;

            string body = TerminalTexts.L(betText) + " " + total;
            IReadOnlyList<SlotMachineTerminal.BetEntry> entries = terminal.Bets;
            int shown = Mathf.Min(entries.Count, 5);
            for (int i = 0; i < shown; i++)
            {
                if (entries[i] == null || entries[i].Def == null) continue;
                body += "\n" + entries[i].Amount + " x " + entries[i].Def.DisplayName;
            }

            int luck = terminal.LuckBoost;
            if (luck > 0) body += "\n" + TerminalTexts.L(luckText) + " x" + luck; 

            betLabel.text = body;
        }

        private void UpdateResultLabel()
        {
            if (resultLabel == null || terminal == null) return;

            if (terminal.Annihilating)
            {
                resultLabel.gameObject.SetActive(false);
                return;
            }

            if (resultShown) return;

            string result = terminal.LastResult;
            if (string.IsNullOrEmpty(result))
            {
                resultLabel.gameObject.SetActive(false);
                return;
            }

            resultShown = true;
            resultLabel.gameObject.SetActive(true);
            resultLabel.text = TerminalTexts.L(result);

            if (terminal.PendingOutcome == SlotMachineOutcome.LuckUp)
            {
                resultLabel.color = titleColor;
            }
            else if (terminal.PendingOutcome != SlotMachineOutcome.Loss)
            {
                resultLabel.color = result == TerminalTexts.L(winText) ? winColor : goldColor;
            }
        }

        private void UpdateCountdown()
        {
            if (countdownRoot == null || terminal == null) return;

            if (!terminal.Annihilating)
            {
                countdownRoot.gameObject.SetActive(false);
                return;
            }

            countdownRoot.gameObject.SetActive(true);
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, terminal.AnnihilationRemaining));
            if (countdownLabel == null) return;
            string localized = TerminalTexts.L(countdownText) + "\n" + seconds;
            if (countdownLabel.text != localized)
                countdownLabel.text = localized;
        }

        private void StartSpinLoop()
        {
            AudioClip clip = ResolveSpinClip();
            if (clip == null) return;

            AudioSource source = EnsureSpinSource();
            source.loop = true;
            source.clip = clip;
            source.volume = spinVolume;

            if (!source.isPlaying) source.Play();
        }

        private void StopSpinLoop()
        {
            if (spinSource == null) return;
            spinSource.Stop();
        }

        private AudioClip ResolveSpinClip()
        {
            if (spinClip != null) return spinClip;
            if (generatedSpin == null) generatedSpin = CarGame.Audio.ProceduralAudio.SlotMachineSpin();
            return generatedSpin;
        }

        private AudioSource EnsureSpinSource()
        {
            if (spinSource != null) return spinSource;

            spinSource = GetComponent<AudioSource>();
            if (spinSource == null) spinSource = gameObject.AddComponent<AudioSource>();

            spinSource.playOnAwake = false;
            spinSource.spatialBlend = 0f;
            return spinSource;
        }

        /// <summary>
        /// Фоновая музыка панели и ducking. Трек звучит, пока панель на экране,
        /// и приглушается на duckLevel каждый раз, когда автомат играет свой
        /// звук (рычаг, джекпот, выигрыш, удача, проигрыш, тик, депозит): панель
        /// следит за SfxCount автомата. Через duckHold после последнего звука
        /// музыка плавно возвращается к полной громкости. Громкость ведём вручную,
        /// чтобы fade показа/скрытия и ducking не спорили за AudioSource.
        /// </summary>
        private void UpdateMusic(float delta)
        {
            if (terminal != null && terminal.SfxCount != lastSfxCount)
            {
                lastSfxCount = terminal.SfxCount;
                duckUntil = Time.unscaledTime + Mathf.Max(0f, duckHold);
            }

            float target = visible ? Mathf.Clamp01(musicVolume) : 0f;
            if (Time.unscaledTime < duckUntil) target *= Mathf.Clamp01(duckLevel);

            if (target <= 0f && music == null) return;

            AudioSource source = EnsureMusic();
            if (target > 0f && !source.isPlaying)
            {
                AudioClip clip = ResolveMusicClip();
                if (clip != null)
                {
                    source.clip = clip;
                    TerminalSpeaker.PlaceAt(source, terminal != null ? terminal.transform : null);
                    source.Play();
                }
            }

            float speed = Mathf.Max(0.0001f, Mathf.Clamp01(musicVolume) / Mathf.Max(0.01f, musicFade));
            musicVolumeCurrent = Mathf.MoveTowards(musicVolumeCurrent, target, speed * delta);
            source.volume = musicVolumeCurrent;

            if (target <= 0f && musicVolumeCurrent <= 0.0005f && source.isPlaying) source.Stop();
        }

        private void StopMusic()
        {
            if (music == null) return;
            music.Stop();
            musicVolumeCurrent = 0f;
        }

        /// <summary>
        /// Отдельный источник-динамик: у панели уже есть AudioSource под трек
        /// вращения барабанов, и музыка не должна делить его с ним. Источник
        /// звучит из точки терминала и проходит через фильтры радио.
        /// </summary>
        private AudioSource EnsureMusic()
        {
            AudioSource source = TerminalSpeaker.Ensure(this, ref music, "SlotMusic");
            if (source != null) source.loop = loopMusic;
            return source;
        }

        private AudioClip ResolveMusicClip()
        {
            if (interfaceMusic != null) return interfaceMusic;
            if (generatedMusic == null) generatedMusic = CarGame.Audio.ProceduralAudio.SlotMachineLoop();
            return generatedMusic;
        }

        private bool InRange(SlotMachineTerminal candidate)
        {
            Vector3 origin = player != null ? player.transform.position : aimCamera.transform.position;
            return Vector3.Distance(origin, candidate.transform.position) <= candidate.ShowDistance;
        }

        private SlotMachineTerminal FindNearestSlot()
        {
            SlotMachineTerminal[] all = FindObjectsByType<SlotMachineTerminal>(FindObjectsSortMode.None);
            if (all == null || all.Length == 0) return null;

            Vector3 origin = player != null ? player.transform.position : aimCamera.transform.position;

            SlotMachineTerminal best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                SlotMachineTerminal candidate = all[i];
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

        private void ClearReels()
        {
            reels.Clear();
            betLabel = null;
            resultLabel = null;
            countdownRoot = null;
            countdownLabel = null;

            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Transform child = content.GetChild(i);
                if (child == null) continue;

                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private RectTransform FindChild(RectTransform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name) return parent.GetChild(i) as RectTransform;
            }
            return null;
        }

        private Camera ResolveCamera()
        {
            if (aimCamera != null) return aimCamera;

            PlayerInteractor interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor != null && interactor.AimCamera != null) return aimCamera = interactor.AimCamera;

            return aimCamera = Camera.main;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}