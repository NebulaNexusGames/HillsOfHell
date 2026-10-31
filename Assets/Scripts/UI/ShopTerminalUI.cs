using System.Collections;
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
    /// Панель магазина терминала: счётчик Golden Bolt сверху и четыре большие
    /// кнопки с ассортиментом. Как и панель спавна, это физический дисплей,
    /// приваренный к точке экрана магазина, — не экранный оверлей.
    ///
    /// Верхняя полоса — счётчик валюты: иконка золотого болта и число баланса
    /// из общего кошелька TerminalWallet. Дальше — ассортимент магазина: над
    /// каждой кнопкой остаток на складе, под кнопкой цена. Закончился запас —
    /// иконка гаснет, кнопка красится в цвет продано, а под ней надпись
    /// SOLD OUT.
    ///
    /// Покупка — прицелом и ЛКМ, как в терминале спавна: курсор остаётся в
    /// центре экрана. Клавиши E здесь нет и не будет.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShopTerminalUI : MonoBehaviour
    {
        private sealed class Slot
        {
            public RectTransform Rect;
            public Image Frame;
            public RawImage Icon;
            public Image SoldOut;
            public TextMeshProUGUI PriceLabel;
            public TextMeshProUGUI StockLabel;
            public ShopTerminal.ShopOffer Offer;
            public bool Aimed;
            public float Flash;
            public float PriceAlert;
        }

        [Header("Магазин")]
        [Tooltip("Магазин, к которому привязана панель. Можно оставить пустым — панель сама найдёт тот, в ActiveTrigger которого стоит игрок")]
        [SerializeField] private ShopTerminal terminal;

        [Header("Камера")]
        [Tooltip("Камера, от которой панель отстраивается. Если пусто — берётся камера прицела")]
        [SerializeField] private Camera aimCamera;

        [Header("Размер панели")]
        [Tooltip("Ширина окна панели в метрах. У каждого магазина своя — подгоняется под его модель")]
        [SerializeField] private float windowWidth = TerminalLayout.WindowWidth;

        [Tooltip("Высота окна панели в метрах. У каждого магазина своя — подгоняется под его модель")]
        [SerializeField] private float windowHeight = TerminalLayout.WindowHeight;

        [Tooltip("Сколько кнопок-слотов в ряду")]
        [SerializeField] private int columns = 4;

        [Header("Положение")]
        [Tooltip("Насколько центр панели выдвинут из магазина по его локальной оси Z, метры")]
        [SerializeField] private float standOff = 0.06f;

        [Tooltip("Аварийный переворот панели, если лицевая сторона окажется не там")]
        [SerializeField] private bool flipPanel;

        [Header("Тексты")]
        [Tooltip("Заголовок над ассортиментом")]
        [SerializeField] private string titleText = "SHOP";

        [Tooltip("Подпись счётчика баланса")]
        [SerializeField] private string balanceText = "BALANCE";

        [Tooltip("Надпись под раскупленной кнопкой")]
        [SerializeField] private string soldOutText = "SOLD OUT";

        [Header("Цвета")]
        [SerializeField] private Color backdropColor = new Color(0.03f, 0.05f, 0.08f, 0.88f);
        [SerializeField] private Color frameColor = new Color(0.12f, 0.16f, 0.22f, 0.85f);
        [SerializeField] private Color hoverColor = new Color(0.35f, 0.78f, 0.95f, 0.95f);
        [SerializeField] private Color cantAffordColor = new Color(0.6f, 0.3f, 0.28f, 0.95f);
        [SerializeField] private Color soldOutColor = new Color(0.28f, 0.1f, 0.08f, 0.9f);
        [SerializeField] private Color textColor = new Color(0.86f, 0.9f, 0.94f, 1f);
        [SerializeField] private Color titleColor = new Color(0.35f, 0.78f, 0.95f, 1f);
        [SerializeField] private Color priceColor = new Color(0.98f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color stockColor = new Color(0.62f, 0.7f, 0.78f, 1f);
        [SerializeField] private Color alertColor = new Color(1f, 0.4f, 0.35f, 1f);

        [Header("Кнопка")]
        [Tooltip("Доля кнопки, занятая моделью. Больше — модель ближе к краям")]
        [SerializeField, Range(1f, 2f)] private float iconPadding = 1.06f;

        [Header("Продано")]
        [Tooltip("Спрайт, заменяющий иконку предмета, когда запас кончился. Пусто — показывается надпись soldOutText")]
        [SerializeField] private Sprite soldOutSprite;

        [Header("Обновление стока")]
        [Tooltip("Надпись на кнопке обновления ассортимента")]
        [SerializeField] private string refreshText = "REFRESH";

        [Tooltip("Цвет кнопки обновления ассортимента")]
        [SerializeField] private Color refreshColor = new Color(0.24f, 0.62f, 0.34f, 0.95f);

        [Tooltip("Цвет кнопки обновления, когда болтов не хватает")]
        [SerializeField] private Color refreshDeniedColor = new Color(0.62f, 0.3f, 0.28f, 0.95f);

        [Tooltip("Насколько иконка уже самой кнопки, доля")]
        [SerializeField, Range(0.2f, 1f)] private float iconScale = 0.92f;

        [Tooltip("Насколько велика цена под кнопкой и остаток над ней, доля от шага сетки")]
        [SerializeField, Range(0.05f, 0.4f)] private float labelRatio = 0.22f;

        [Header("Музыка панели")]
        [Tooltip("Трек, который играет, пока панель магазина на экране. Пусто — процедурный из ProceduralAudio")]
        [SerializeField] private AudioClip interfaceMusic;

        [Tooltip("Громкость трека панели магазина")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.45f;

        [Tooltip("Зацикливать трек. Снимите галочку, если это короткий фрагмент")]
        [SerializeField] private bool loopMusic = true;

        [Tooltip("Длительность нарастания и затухания, с")]
        [SerializeField] private float musicFade = 0.35f;

        private AudioSource music;
        private AudioClip generatedMusic;
        private Coroutine musicFadeRoutine;

        private readonly List<Slot> slots = new List<Slot>();

        private ShopTerminal builtFor;
        private int builtRevision = -1;

        private RectTransform refreshRect;
        private Image refreshFrame;
        private TextMeshProUGUI refreshLabel;
        private bool refreshAimed;
        private float refreshFlash;
        private float refreshAlert;

        private Transform rig;
        private RectTransform canvasRect;
        private RectTransform backdrop;
        private RectTransform content;

        private RawImage currencyIcon;
        private TextMeshProUGUI currencyAmount;

        private bool visible;
        private bool aiming;
        private PlayerController player;
        private Vector2 screenCenter;

        /// <summary>Разворот панели, посчитанный один раз: на сторону триггера.</summary>
        private Quaternion panelRotation = Quaternion.identity;

        /// <summary>Слот под прицелом, зафиксированный на текущий кадр.</summary>
        private Slot aimedSlot;

        public static ShopTerminalUI Instance { get; private set; }

        /// <summary>Панель магазина сейчас на экране.</summary>
        public static bool IsOpen => Instance != null && Instance.visible;

        /// <summary>
        /// Прицел стоит на кнопке магазина. Только тогда панель перехватывает
        /// ЛКМ (читается в PlayerFirearm и в BoltDepositor).
        /// </summary>
        public static bool AimingSlot => Instance != null && Instance.aiming;

        /// <summary>
        /// Создаёт панель, если её ещё нет. Видимость решается только входом в
        /// ActiveTrigger, поэтому вызов из Awake на неё не влияет.
        /// </summary>
        public static ShopTerminalUI Show(ShopTerminal owner)
        {
            ShopTerminalUI view = Instance;
            if (view == null)
            {
                // В сцене может уже лежать редактируемый HUD, чей OnEnable ещё не
                // отработал в этот кадр: берём его, а не плодим временную панель,
                // которая потом перехватит синглтон.
                view = Object.FindFirstObjectByType<ShopTerminalUI>(FindObjectsInactive.Include);
                if (view != null)
                {
                    Instance = view;
                }
                else
                {
                    GameObject go = new GameObject("ShopTerminalUI");
                    view = go.AddComponent<ShopTerminalUI>();
                }
            }

            if (owner != null && view != null && view.terminal == null) view.terminal = owner;
            return view;
        }

        /// <summary>
        /// Привязывает панель к магазину. Вызывается из редакторского меню:
        /// компонент лежит в сцене, поэтому его настройки видно в инспекторе.
        /// </summary>
        public void Attach(ShopTerminal owner)
        {
            terminal = owner;
            builtFor = null;
        }

        private void OnEnable()
        {
            // Последний включённый — синглтон: если в сцене лежит редактируемый
            // HUD, он перекрывает временную панель, созданную Show из Awake.
            Instance = this;
            TerminalWallet.Changed += OnWalletChanged;

            // Ячейка магазина: белая рамка без заливки, белая заливка под
            // курсором, красная вспышка при покупке. Цвета задаются кодом, а не
            // сериализацией: у сценовых панелей осталась старая синяя палитра.
            frameColor = TerminalTheme.IdleCell;
            hoverColor = TerminalTheme.HoverCell;

            // Процедурный трек синтезируем заранее, как TerminalUI: генерация
            // двух секунд звука заметна, если оставить её на момент показа панели.
            if (interfaceMusic == null && generatedMusic == null) generatedMusic = CarGame.Audio.ProceduralAudio.ShopLoop();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            TerminalWallet.Changed -= OnWalletChanged;
        }

        private void OnWalletChanged()
        {
            if (currencyAmount != null) currencyAmount.text = TerminalWallet.Balance.ToString();
        }

        private void Update()
        {
            if (ResolveCamera() == null) return;

            HandleDebugKeys();

            screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            player = FindFirstObjectByType<PlayerController>();

            // Переключаемся на тот магазин, в ActiveTrigger которого игрок стоит.
            ShopTerminal nearest = FindNearestShop();
            if (nearest != null) terminal = nearest;

            bool inside = terminal != null && terminal.PlayerInsideActive;
            if (!inside && terminal != null && terminal.ActiveTrigger == null) inside = InRange(terminal);

            if (!inside)
            {
                aiming = false;
                aimedSlot = null;
                SetVisible(false);
                return;
            }

            if (builtFor != terminal || rig == null || builtRevision != terminal.Revision) Build();

            SetVisible(true);
            if (!visible) return;

            UpdateRig();

            // Прицел и клик — одно значение на кадр, чтобы подсвеченное и
            // нажатое всегда совпадали (см. TerminalUI).
            aimedSlot = AimedSlot();
            refreshAimed = RefreshAimed();

            UpdateSlots();
            UpdateRefreshButton();

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

        private void HandleDebugKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!KeyBindings.WasPressedThisFrame(GameAction.TerminalExit)) return;

            flipPanel = !flipPanel;
            ResolveFacing();
            Debug.Log("[ShopTerminalUI] Панель развёрнута " + (flipPanel ? "ОТ" : "К") + " триггеру. F1 — перевернуть.", this);
        }

        private void SetVisible(bool on)
        {
            if (visible == on) return;

            visible = on;
            if (rig != null) rig.gameObject.SetActive(on);
            if (terminal != null) terminal.SetPowered(on);

            // Музыка живёт ровно вместе с панелью. SetVisible — единственное
            // место, где видно событие показа/скрытия: проверка visible == on
            // выше отсекает повторы, и трек не перезапускается каждый кадр.
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

            if (generatedMusic == null) generatedMusic = CarGame.Audio.ProceduralAudio.ShopLoop();
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

            // OnDisable гасит музыку уже выключенного HUD, а корутину на
            // неактивном объекте запустить нельзя. Меняем громкость сразу.
            if (!isActiveAndEnabled)
            {
                source.volume = target;
                if (target <= 0.0001f && source.isPlaying) source.Stop();
                return;
            }

            musicFadeRoutine = StartCoroutine(FadeRoutine(source, target));
        }

        private IEnumerator FadeRoutine(AudioSource source, float target)
        {
            float duration = Mathf.Max(0.01f, musicFade);
            float from = source.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(from, target, elapsed / duration);
                yield return null;
            }

            source.volume = target;

            if (target <= 0f) source.Stop();
            musicFadeRoutine = null;
        }

        private void HandleClick()
        {
            if (terminal != null && refreshAimed)
            {
                if (terminal.TryRefresh()) refreshFlash = 0.35f;
                else refreshAlert = 0.4f;
                return;
            }

            Slot slot = aimedSlot;
            if (slot == null && Cursor.visible) slot = SlotUnderPointer();
            if (slot == null) return;

            ShopTerminal.ShopOffer offer = slot.Offer;
            if (offer == null || offer.Item == null || offer.Stock <= 0) return;
            if (terminal == null) return;

            if (terminal.Buy(offer)) slot.Flash = 0.35f;
            else slot.PriceAlert = 0.4f;
        }

        /// <summary>Прицел (или курсор) стоит на кнопке обновления ассортимента.</summary>
        private bool RefreshAimed()
        {
            if (refreshRect == null || !refreshRect.gameObject.activeInHierarchy) return false;

            Vector2 point = screenCenter;
            Mouse mouse = Mouse.current;
            if (Cursor.visible && mouse != null) point = mouse.position.ReadValue();

            return RectTransformUtility.RectangleContainsScreenPoint(refreshRect, point, aimCamera);
        }

        private void UpdateRefreshButton()
        {
            if (refreshFrame == null) return;

            float delta = Time.deltaTime;
            int cost = terminal != null ? terminal.RefreshCost : 0;

            bool affordable = TerminalWallet.Balance >= cost;
            Color baseColor = affordable ? refreshColor : refreshDeniedColor;
            Color color = refreshAimed ? Color.Lerp(baseColor, hoverColor, 0.6f) : baseColor;

            if (refreshFlash > 0f)
            {
                refreshFlash -= delta;
                color = Color.Lerp(color, TerminalTheme.PressCell, Mathf.Clamp01(refreshFlash / 0.35f));
            }
            else if (refreshAlert > 0f)
            {
                refreshAlert -= delta;
                color = Color.Lerp(color, alertColor, Mathf.Clamp01(refreshAlert / 0.4f));
            }

            refreshFrame.color = color;
        }

        private Slot AimedSlot()
        {
            return SlotAt(screenCenter);
        }

        private Slot SlotAt(Vector2 point)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (slot.Rect == null || !slot.Rect.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(slot.Rect, point, aimCamera)) continue;

                return slot;
            }

            return null;
        }

        private Slot SlotUnderPointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return null;

            return SlotAt(mouse.position.ReadValue());
        }

        private void UpdateSlots()
        {
            aiming = aimedSlot != null;
            float delta = Time.deltaTime;
            int balance = TerminalWallet.Balance;

            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                ShopTerminal.ShopOffer offer = slot.Offer;
                if (offer == null || offer.Item == null) continue;

                bool soldOut = offer.Stock <= 0;
                bool hovered = ReferenceEquals(slot, aimedSlot);

                if (slot.Flash > 0f)
                {
                    slot.Flash -= delta;
                    float pulse = Mathf.Clamp01(slot.Flash / 0.35f);
                    slot.Frame.color = Color.Lerp(hovered ? hoverColor : frameColor, TerminalTheme.PressCell, pulse);
                    ApplySlotSoldOutState(slot, soldOut);
                    slot.PriceLabel.text = SoldOutPriceText(soldOut, offer.Item.Price.ToString());
                    slot.PriceLabel.color = soldOut ? soldOutColor : priceColor;
                    slot.StockLabel.text = soldOut ? string.Empty : "x" + offer.Stock;
                    continue;
                }

                if (slot.PriceAlert > 0f)
                {
                    slot.PriceAlert -= delta;
                    slot.PriceLabel.text = SoldOutPriceText(soldOut, offer.Item.Price.ToString());
                    slot.PriceLabel.color = soldOut ? soldOutColor : alertColor;
                    continue;
                }

                if (soldOut)
                {
                    ApplySlotSoldOutState(slot, true);
                    slot.Frame.color = soldOutColor;
                    slot.PriceLabel.text = SoldOutPriceText(true, offer.Item.Price.ToString());
                    slot.PriceLabel.color = soldOutColor;
                    slot.StockLabel.text = string.Empty;
                    slot.Aimed = false;
                    continue;
                }

                ApplySlotSoldOutState(slot, false);

                slot.StockLabel.text = "x" + offer.Stock;

                if (slot.PriceLabel != null)
                {
                    slot.PriceLabel.text = offer.Item.Price.ToString();
                    slot.PriceLabel.color = priceColor;
                }

                if (slot.Frame == null || slot.Aimed == hovered)
                {
                    if (slot.PriceLabel != null && balance < offer.Item.Price)
                    {
                        slot.Frame.color = hovered ? cantAffordColor : frameColor;
                        slot.PriceLabel.color = alertColor;
                    }
                    continue;
                }

                slot.Aimed = hovered;
                slot.Frame.color = hovered ? hoverColor : frameColor;
            }
        }

        /// <summary>
        /// Показывает либо иконку предмета, либо спрайт «продано» вместо неё.
        /// Если спрайт не назначен, иконка просто гаснет — надпись возьмёт на
        /// себя ценник (см. SoldOutPriceText).
        /// </summary>
        private void ApplySlotSoldOutState(Slot slot, bool soldOut)
        {
            if (slot == null) return;

            if (slot.Icon != null) slot.Icon.enabled = !soldOut;

            if (slot.SoldOut == null) return;

            if (soldOutSprite != null) slot.SoldOut.sprite = soldOutSprite;
            slot.SoldOut.enabled = soldOut && soldOutSprite != null;
        }

        /// <summary>
        /// Текст ценника для распроданной позиции: при спрайте «продано» ценник
        /// пуст, иначе показывает надпись soldOutText.
        /// </summary>
        private string SoldOutPriceText(bool soldOut, string price)
        {
            if (!soldOut) return price;
            return soldOutSprite != null ? string.Empty : TerminalTexts.L(soldOutText);
        }

        private bool InRange(ShopTerminal candidate)
        {
            Vector3 origin = player != null ? player.transform.position : aimCamera.transform.position;
            return Vector3.Distance(origin, candidate.transform.position) <= candidate.ShowDistance;
        }

        private ShopTerminal FindNearestShop()
        {
            ShopTerminal[] all = FindObjectsByType<ShopTerminal>(FindObjectsSortMode.None);
            if (all == null || all.Length == 0) return null;

            Vector3 origin = player != null ? player.transform.position : aimCamera.transform.position;

            ShopTerminal best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                ShopTerminal candidate = all[i];
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
            ClearSlots();

            if (ResolveCamera() == null) return;

            EnsureRig();
            ResolveFacing();
            builtFor = terminal;
            builtRevision = terminal != null ? terminal.Revision : -1;

            if (terminal == null) return;

            // Копия ассортимента: при Build список пересобирается, а панель может
            // перестроиться, пока игрок стоит внутри триггера.
            List<ShopTerminal.ShopOffer> offers = new List<ShopTerminal.ShopOffer>();
            for (int i = 0; i < terminal.Offers.Count; i++) offers.Add(terminal.Offers[i]);
            int offerCount = offers.Count;

            int cols = Mathf.Max(1, columns);
            int rows = Mathf.Max(1, Mathf.CeilToInt(offerCount / (float)cols));

            // Кнопки занимают один ряд из четырёх, поэтому шаг сетки большой и
            // сами кнопки получаются крупными, а не мелкими, как в терминале.
            const float refreshUnits = 0.7f;
            float rowUnits = 0.9f + 0.2f + rows + 0.2f + refreshUnits;

            // Размер окна задан полями самой панели (windowWidth/windowHeight) —
            // у каждого магазина он свой, чтобы окно село на его модель. Шаг
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

            float y = contentH * 0.5f;

            // Верхняя строка: баланс в левом верхнем углу, заголовок — справа.
            CreateHeaderRow(y, pitch, contentW);
            y -= pitch * 1.0f;

            for (int i = 0; i < offerCount; i++)
            {
                int col = i % cols;
                int row = i / cols;

                float x = -contentW * 0.5f + pitch * (col + 0.5f);
                float slotY = y - pitch * (row + 0.5f);

                CreateShopSlot(offers[i], new Vector2(x, slotY), pitch);
            }

            // Нижний ряд — кнопка платного обновления ассортимента.
            y -= rows * pitch;
            y -= pitch * 0.2f;
            CreateRefreshButton(0f, y - refreshUnits * pitch * 0.5f,
                Mathf.Max(0.01f, contentW - pitch * 0.6f), refreshUnits * pitch * 0.8f);
        }

        /// <summary>
        /// Верхняя строка панели: слева баланс в формате «иконка валюты + число»,
        /// справа заголовок магазина. Баланс всегда в левом верхнем углу, чтобы
        /// его было видно, даже когда панель обрезается по краю экрана.
        /// </summary>
        private void CreateHeaderRow(float y, float pitch, float width)
        {
            float pad = pitch * 0.32f;
            float iconSide = pitch * 0.5f;
            float amountW = pitch * 1.25f;

            float iconX = -width * 0.5f + pad + iconSide * 0.5f;
            float amountX = iconX + iconSide * 0.5f + pad * 0.6f + amountW * 0.5f;

            GameObject iconGo = new GameObject("CurrencyIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            iconGo.layer = gameObject.layer;

            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(content, false);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSide, iconSide);
            iconRect.anchoredPosition = new Vector2(iconX, y);

            currencyIcon = iconGo.GetComponent<RawImage>();
            currencyIcon.color = Color.white;
            currencyIcon.raycastTarget = false;

            ItemPreviewService previews = ItemPreviewService.Instance;
            if (previews != null)
            {
                Texture2D texture = previews.Get(ItemDefs.GoldenBolt, iconPadding);
                if (texture != null) currencyIcon.texture = texture;
            }

            TextMeshProUGUI amount = CreateLabel(
                "CurrencyAmount", TerminalWallet.Balance.ToString(), amountW, pitch * 0.5f,
                amountX, y, priceColor, null);
            amount.alignment = TextAlignmentOptions.Left;
            currencyAmount = amount;

            float titleW = width * 0.3f;
            CreateLabel("Title", TerminalTexts.L(titleText), titleW, pitch * 0.5f,
                width * 0.5f - pad - titleW * 0.5f, y, titleColor, null);
        }

        /// <summary>Кнопка платного обновления ассортимента — надпись с ценой в болтах.</summary>
        private void CreateRefreshButton(float x, float y, float width, float height)
        {
            GameObject go = new GameObject("RefreshButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(content, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);

            refreshFrame = go.GetComponent<Image>();
            refreshFrame.color = refreshColor;
            refreshFrame.raycastTarget = false;
            TerminalTheme.ApplyButton(refreshFrame);

            float cost = terminal != null ? terminal.RefreshCost : 0;
            string label = cost > 0 ? TerminalTexts.L(refreshText) + "  " + cost : TerminalTexts.L(refreshText);
            refreshLabel = CreateLabel("Label", label, width * 0.94f, height * 0.7f, 0f, 0f, textColor, rect);

            refreshRect = rect;
            refreshFlash = 0f;
            refreshAlert = 0f;
        }

        private void CreateShopSlot(ShopTerminal.ShopOffer offer, Vector2 position, float pitch)
        {
            GameObject rootGo = new GameObject("ShopSlot", typeof(RectTransform));
            rootGo.layer = gameObject.layer;

            RectTransform rect = rootGo.GetComponent<RectTransform>();
            rect.SetParent(content, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(pitch, pitch);
            rect.anchoredPosition = position;

            RectMask2D clip = rootGo.AddComponent<RectMask2D>();
            clip.softness = Vector2Int.zero;

            float stockH = pitch * labelRatio;
            float priceH = pitch * labelRatio;
            float frameSide = pitch * 0.86f;

            GameObject frameGo = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frameGo.layer = gameObject.layer;

            RectTransform frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.SetParent(rect, false);
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.sizeDelta = new Vector2(frameSide, frameSide);
            frameRect.anchoredPosition = new Vector2(0f, pitch * 0.02f);

            Image frame = frameGo.GetComponent<Image>();
            frame.color = frameColor;
            frame.raycastTarget = false;
            TerminalTheme.ApplyCell(frame);

            float iconSide = Mathf.Max(0.001f, frameSide * iconScale);

            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            iconGo.layer = gameObject.layer;

            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(iconSide, iconSide);
            iconRect.anchoredPosition = new Vector2(0f, pitch * 0.02f);

            RawImage icon = iconGo.GetComponent<RawImage>();
            icon.color = Color.white;
            icon.raycastTarget = false;

            // Спрайт «продано» лежит поверх иконки и включается вместо неё,
            // когда запас кончился. Настраивается в инспекторе панели.
            GameObject soldOutGo = new GameObject("SoldOut", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            soldOutGo.layer = gameObject.layer;

            RectTransform soldOutRect = soldOutGo.GetComponent<RectTransform>();
            soldOutRect.SetParent(rect, false);
            soldOutRect.anchorMin = new Vector2(0.5f, 0.5f);
            soldOutRect.anchorMax = new Vector2(0.5f, 0.5f);
            soldOutRect.sizeDelta = new Vector2(iconSide, iconSide);
            soldOutRect.anchoredPosition = new Vector2(0f, pitch * 0.02f);

            Image soldOut = soldOutGo.GetComponent<Image>();
            soldOut.sprite = soldOutSprite;
            soldOut.color = Color.white;
            soldOut.raycastTarget = false;
            soldOut.enabled = false;

            TextMeshProUGUI stock = CreateLabel(
                "Stock", offer != null && offer.Item != null ? "x" + offer.Stock : string.Empty,
                pitch * 0.95f, stockH, 0f, pitch * 0.5f - stockH * 0.5f + pitch * 0.01f, stockColor, rect);

            TextMeshProUGUI price = CreateLabel(
                "Price", offer != null && offer.Item != null ? offer.Item.Price.ToString() : string.Empty,
                pitch * 0.98f, priceH, 0f, -pitch * 0.5f + priceH * 0.5f, priceColor, rect);

            Slot slot = new Slot
            {
                Rect = rect,
                Frame = frame,
                Icon = icon,
                SoldOut = soldOut,
                StockLabel = stock,
                PriceLabel = price,
                Offer = offer
            };

            slots.Add(slot);

            if (offer != null && offer.Item != null)
            {
                ItemPreviewService previews = ItemPreviewService.Instance;
                if (previews != null)
                {
                    Texture2D texture = previews.Get(offer.Item, iconPadding);
                    if (texture != null) icon.texture = texture;
                }
            }
        }

        private TextMeshProUGUI CreateLabel(string name, string text, float width, float height, float x, float y, Color color, RectTransform parent)
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
            TerminalTheme.ApplyFont(label);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = maxFont * 0.25f;
            label.fontSizeMax = maxFont;
            label.fontSize = maxFont;
            label.overflowMode = TextOverflowModes.Truncate;

            return label;
        }

        /// <summary>Панель — часть магазина: цепляется к нему и берёт его слой.</summary>
        private void EnsureRig()
        {
            if (rig != null) return;

            GameObject rigGo = new GameObject("ShopPanel");
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

        private void ClearSlots()
        {
            slots.Clear();
            currencyIcon = null;
            currencyAmount = null;
            refreshRect = null;
            refreshFrame = null;
            refreshLabel = null;
            refreshAimed = false;
            refreshFlash = 0f;
            refreshAlert = 0f;

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

        private void OnDestroy()
        {
            if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
            if (Instance == this) Instance = null;
        }
    }
}