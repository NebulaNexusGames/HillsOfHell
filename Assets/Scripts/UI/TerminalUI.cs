using System;
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
    [DisallowMultipleComponent]
    public class TerminalUI : MonoBehaviour
    {
        private enum UiState
        {
            Main,
            Section
        }

        private sealed class Slot
        {
            public RectTransform Rect;
            public Image Frame;
            public RawImage Icon;
            public ItemDef Item;
            public bool Aimed;
            public float Flash;
        }

        private sealed class ButtonSlot
        {
            public RectTransform Rect;
            public Image Frame;
            public TextMeshProUGUI Label;
            public System.Action OnClick;
            public bool Aimed;
        }

        [Header("Терминал")]
        [SerializeField] private SpawnTerminal terminal;

        [Header("Камера")]
        [SerializeField] private Camera aimCamera;

        [Header("Размер панели")]
        [SerializeField] private float windowWidth = TerminalLayout.WindowWidth * 1.16f;
        [SerializeField] private float windowHeight = TerminalLayout.WindowHeight * 1.02f;
        [SerializeField] private int columns = 5;

        [Header("Положение")]
        [SerializeField] private float standOff = 0.06f;
        [SerializeField] private bool flipPanel;

        [Header("Тексты")]
        [SerializeField] private string titleText = "SPAWN";
        [SerializeField] private string normalText = "НОРМАЛЬНЫЕ ПРЕДМЕТЫ";
        [SerializeField] private string adminText = "АДМИНСКИЕ ПРЕДМЕТЫ";
        [SerializeField] private string backText = "НАЗАД";

        [Header("Цвета")]
        [SerializeField] private Color backdropColor = new Color(0.03f, 0.05f, 0.08f, 0.88f);
        [SerializeField] private Color frameColor = new Color(0.12f, 0.16f, 0.22f, 0.85f);
        [SerializeField] private Color hoverColor = new Color(0.35f, 0.78f, 0.95f, 0.95f);
        [SerializeField] private Color textColor = new Color(0.9f, 0.93f, 0.98f, 1f);
        [SerializeField] private Color titleColor = new Color(0.4f, 0.82f, 1f, 1f);
        [SerializeField] private Color activeColor = new Color(0.5f, 0.9f, 0.4f, 0.95f);
        [SerializeField] private Color arrowColor = new Color(0.9f, 0.93f, 0.98f, 1f);

        [Header("Ячейка")]
        [SerializeField] private bool showCaptions = true;
        [SerializeField, Range(1f, 2f)] private float iconPadding = 1.06f;
        [SerializeField, Range(0.2f, 1f)] private float iconScale = 0.94f;
        [SerializeField, Range(0.05f, 0.6f)] private float captionRatio = 0.26f;

        private static readonly string[] NormalSectionNames =
        {
            "ОРУЖИЕ/ИНСТРУМЕНТЫ",
            "РАСХОДНИКИ",
            "ПОСТРОЙКИ",
            "ВАЛЮТА",
            "ПРЕДМЕТЫ ДЛЯ КРАФТА"
        };

        // Админский режим — те же страницы, что и в обычном разделе:
        // листаются стрелками < >, заголовок сверху, сетка по центру.
        private static readonly string[] AdminSectionNames =
        {
            "МОБЫ",
            "ОБЪЕКТЫ",
            "АДМИНСКИЕ ИНСТРУМЕНТЫ"
        };

        private readonly List<Slot> slots = new List<Slot>();
        private readonly List<ButtonSlot> buttons = new List<ButtonSlot>();

        private SpawnTerminal builtFor;
        private UiState uiState = UiState.Main;
        private bool isAdminMode;
        private int normalSectionIndex;
        private int adminSectionIndex;

        private Transform rig;
        private RectTransform canvasRect;
        private RectTransform backdrop;
        private RectTransform content;
        private RectTransform topBar;
        private RectTransform bottomBar;

        private TextMeshProUGUI sectionTitleLabel;
        private ButtonSlot leftArrow;
        private ButtonSlot rightArrow;
        private ButtonSlot backButton;
        private ButtonSlot countMinus;
        private ButtonSlot countPlus;
        private TextMeshProUGUI countLabel;

        private bool visible;
        private bool aiming;
        private PlayerController player;
        private Vector2 screenCenter;

        private Quaternion panelRotation = Quaternion.identity;
        private Slot aimedSlot;
        private ButtonSlot aimedButton;

        [Header("Музыка панели")]
        [SerializeField] private AudioClip interfaceMusic;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.45f;
        [SerializeField] private bool loopMusic = true;
        [SerializeField] private float musicFade = 0.35f;

        private AudioSource music;
        private AudioClip generatedMusic;
        private Coroutine musicFadeRoutine;

        public static TerminalUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.visible;
        public static bool AimingSlot => Instance != null && Instance.aiming;

        // Настройки панели, какие они в файле сцены (настрока автора).
        // При перезапуске игры (главное меню / смерть) старая панель уничтожена и
        // Show() пересоздаёт её кодом — такие «самозванцы» получают дефолтные
        // значения полей C# (окно больше экрана, процедурная музыка вместо темы),
        // а не то, что настроено в инспекторе сцены. Первый созданный кодом
        // экземпляр фиксирует снимок своих полей (до любого кодового изменения)
        // в статическом поле; все последующие пересоздания сбрасываются к нему.
        [Serializable]
        private struct PanelSnapshot
        {
            public float windowWidth;
            public float windowHeight;
            public int columns;
            public AudioClip interfaceMusic;
            public float musicVolume;
            public bool loopMusic;
            public float musicFade;
            public string titleText;
            public string normalText;
            public string adminText;
            public string backText;
            public Color backdropColor;
            public Color frameColor;
            public Color hoverColor;
            public Color textColor;
            public Color titleColor;
            public Color activeColor;
            public Color arrowColor;
            public bool showCaptions;
            public float iconPadding;
            public float iconScale;
            public float captionRatio;
            public float standOff;
            public bool flipPanel;
        }

        private static PanelSnapshot sceneDefaults;
        private static bool sceneDefaultsCaptured;

        // Флаг: этот экземпляр создан кодом (Show), а не лежит в сцене. Такие
        // панели обязаны выглядеть ровно как их прототип из проекта.
        private bool createdAtRuntime;

        /// <summary>
        /// Снимает эталон настроек панели с первого экземпляра, загруженного из
        /// файла сцены Unity (т.е. именно таким, каким его настроил автор в
        /// инспекторе). Вызывается один раз за игру из SceneResetter до того,
        /// как код успеет что-либо изменить. Все последующие пересоздания панели
        /// после рестарта сцены сбрасываются к этому эталону.
        /// </summary>
        public static void CaptureFromSceneObject()
        {
            TerminalUI view = Instance;
            if (view == null || view.createdAtRuntime) return; // эталон снимается только со сценного объекта
            if (sceneDefaultsCaptured) return;
            sceneDefaultsCaptured = true;
            sceneDefaults = new PanelSnapshot
            {
                windowWidth = view.windowWidth,
                windowHeight = view.windowHeight,
                columns = view.columns,
                interfaceMusic = view.interfaceMusic,
                musicVolume = view.musicVolume,
                loopMusic = view.loopMusic,
                musicFade = view.musicFade,
                titleText = view.titleText,
                normalText = view.normalText,
                adminText = view.adminText,
                backText = view.backText,
                backdropColor = view.backdropColor,
                frameColor = view.frameColor,
                hoverColor = view.hoverColor,
                textColor = view.textColor,
                titleColor = view.titleColor,
                activeColor = view.activeColor,
                arrowColor = view.arrowColor,
                showCaptions = view.showCaptions,
                iconPadding = view.iconPadding,
                iconScale = view.iconScale,
                captionRatio = view.captionRatio,
                standOff = view.standOff,
                flipPanel = view.flipPanel,
            };
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (createdByCodePending)
            {
                createdAtRuntime = true;
                createdByCodePending = false;
            }
            if (createdAtRuntime) ApplySceneDefaults();
            frameColor = TerminalTheme.IdleCell;
            hoverColor = TerminalTheme.HoverCell;
            if (interfaceMusic == null && generatedMusic == null) generatedMusic = ProceduralAudio.TerminalLoop();
        }

        // Мост между Show() и Awake(): AddComponent вызывает Awake немедленно,
        // поэтому сигнал о «ручном» создании передаём статическим флагом.
        private static bool createdByCodePending;

        /// <summary>Сбросить панель к эталону из проекта (см. EnsureSceneDefaults).</summary>
        private void ApplySceneDefaults()
        {
            if (!sceneDefaultsCaptured)
            {
                // Эталон ещё не зафиксирован: первый созданный кодом экземпляр
                // снимает свои значения (они равны инициализаторам полей/префабу).
                sceneDefaultsCaptured = true;
                sceneDefaults = new PanelSnapshot
                {
                    windowWidth = windowWidth,
                    windowHeight = windowHeight,
                    columns = columns,
                    interfaceMusic = interfaceMusic,
                    musicVolume = musicVolume,
                    loopMusic = loopMusic,
                    musicFade = musicFade,
                    titleText = titleText,
                    normalText = normalText,
                    adminText = adminText,
                    backText = backText,
                    backdropColor = backdropColor,
                    frameColor = frameColor,
                    hoverColor = hoverColor,
                    textColor = textColor,
                    titleColor = titleColor,
                    activeColor = activeColor,
                    arrowColor = arrowColor,
                    showCaptions = showCaptions,
                    iconPadding = iconPadding,
                    iconScale = iconScale,
                    captionRatio = captionRatio,
                    standOff = standOff,
                    flipPanel = flipPanel,
                };
                return;
            }
            PanelSnapshot d = sceneDefaults;
            windowWidth = d.windowWidth;
            windowHeight = d.windowHeight;
            columns = d.columns;
            interfaceMusic = d.interfaceMusic;
            musicVolume = d.musicVolume;
            loopMusic = d.loopMusic;
            musicFade = d.musicFade;
            titleText = d.titleText;
            normalText = d.normalText;
            adminText = d.adminText;
            backText = d.backText;
            backdropColor = d.backdropColor;
            frameColor = d.frameColor;
            hoverColor = d.hoverColor;
            textColor = d.textColor;
            titleColor = d.titleColor;
            activeColor = d.activeColor;
            arrowColor = d.arrowColor;
            showCaptions = d.showCaptions;
            iconPadding = d.iconPadding;
            iconScale = d.iconScale;
            captionRatio = d.captionRatio;
            standOff = d.standOff;
            flipPanel = d.flipPanel;
            // Предустановленная тема не задана в проекте — только тогда допустима
            // процедурная генерация; иначе панель обязана играть клип автора.
            if (interfaceMusic != null) generatedMusic = null;
        }

        public static TerminalUI Show(SpawnTerminal owner)
        {
            TerminalUI view = Instance;
            if (view == null)
            {
                GameObject go = new GameObject("TerminalUI");
                if (owner != null) go.transform.SetParent(owner.transform, false);
                // Сигнал для Awake(): панель создана кодом (например, после
                // рестарта игры из главного меню или после смерти) — при
                // создании она обязана сброситься к виду из проекта.
                createdByCodePending = true;
                view = go.AddComponent<TerminalUI>();
                createdByCodePending = false;
            }
            else if (owner != null)
            {
                if (view.transform.parent != owner.transform) view.transform.SetParent(owner.transform, false);
            }
            if (owner != null && view.terminal == null) view.terminal = owner;
            return view;
        }

        public void Attach(SpawnTerminal owner) { terminal = owner; if (owner != null && transform.parent != owner.transform) transform.SetParent(owner.transform, false); builtFor = null; }

        private void Update()
        {
            if (ResolveCamera() == null) return;
            HandleDebugKeys();

            screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            player = FindFirstObjectByType<PlayerController>();

            SpawnTerminal nearest = FindNearestTerminal();
            if (nearest != null) terminal = nearest;

            bool inside = terminal != null && terminal.PlayerInsideActive;
            if (!inside && terminal != null && terminal.ActiveTrigger == null) inside = InRange(terminal);

            if (!inside)
            {
                aiming = false;
                aimedSlot = null;
                aimedButton = null;
                SetVisible(false);
                return;
            }

            if (builtFor != terminal || rig == null)
            {
                bool wasAdmin = isAdminMode;
                if (terminal != null && terminal.Mode == SpawnTerminal.TerminalMode.Admin) isAdminMode = true;
                else isAdminMode = false;
                if (wasAdmin != isAdminMode) uiState = UiState.Main;
                Build();
            }

            SetVisible(true);
            if (!visible) return;

            UpdateRig();
            aimedSlot = AimedSlot();
            aimedButton = AimedButton();
            UpdateSlots();
            UpdateButtons();

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) HandleClick();
        }

        private void UpdateRig() { if (rig == null || terminal == null) return; AttachToTerminal(); }

        private void AttachToTerminal()
        {
            Transform point = terminal.ScreenPoint;
            if (point == null) point = terminal.transform;
            if (rig.parent != terminal.transform) rig.SetParent(terminal.transform, false);
            rig.localPosition = terminal.transform.InverseTransformPoint(point.position);
            rig.localPosition += point.localRotation * new Vector3(0f, 0f, standOff);
            Vector3 lossy = terminal.transform.lossyScale;
            rig.localScale = new Vector3(1f/Mathf.Max(0.0001f,Mathf.Abs(lossy.x)),1f/Mathf.Max(0.0001f,Mathf.Abs(lossy.y)),1f/Mathf.Max(0.0001f,Mathf.Abs(lossy.z)));
            rig.rotation = panelRotation;
        }

        private void ResolveFacing()
        {
            Vector3 dir = Vector3.zero;
            Transform trigger = terminal != null && terminal.ActiveTrigger != null ? terminal.ActiveTrigger.transform : null;
            if (trigger != null)
            {
                Vector3 forward = trigger.forward; forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f) dir = forward.normalized;
            }
            if (dir.sqrMagnitude <= 0.0001f && terminal != null)
            {
                Vector3 forward = terminal.transform.forward; forward.y = 0f;
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
            if (KeyBindings.WasPressedThisFrame(GameAction.TerminalExit))
            {
                flipPanel = !flipPanel;
                ResolveFacing();
            }
        }

        private void SetVisible(bool on)
        {
            if (visible == on) return;
            visible = on;
            if (rig != null) rig.gameObject.SetActive(on);
            if (terminal != null) terminal.SetPowered(on);
            if (on) StartMusic(); else { StopMusic(); }
        }

        private AudioSource EnsureMusic() { AudioSource source = TerminalSpeaker.Ensure(this, ref music); if (source != null) source.loop = loopMusic; return source; }
        private AudioClip ResolveMusicClip() { if (interfaceMusic != null) return interfaceMusic; if (generatedMusic == null) generatedMusic = ProceduralAudio.TerminalLoop(); return generatedMusic; }
        private void StartMusic() { AudioClip clip = ResolveMusicClip(); if (clip == null) return; AudioSource source = EnsureMusic(); if (source == null) return; source.loop = loopMusic; source.clip = clip; TerminalSpeaker.PlaceAt(source, terminal != null ? terminal.transform : null); if (!source.isPlaying) source.Play(); FadeMusic(source, Mathf.Clamp01(musicVolume)); }
        private void StopMusic() { if (music == null || !music.isPlaying) return; FadeMusic(music, 0f); }
        private void FadeMusic(AudioSource source, float target) { if (musicFadeRoutine != null) { StopCoroutine(musicFadeRoutine); musicFadeRoutine = null; } if (!isActiveAndEnabled) { source.volume = target; if (target <= 0.0001f && source.isPlaying) source.Stop(); return; } musicFadeRoutine = StartCoroutine(FadeRoutine(source, target)); }
        private IEnumerator FadeRoutine(AudioSource source, float target) { float duration = Mathf.Max(0.01f, musicFade); float from = source.volume; float elapsed = 0f; while (elapsed < duration) { elapsed += Time.unscaledDeltaTime; source.volume = Mathf.Lerp(from, target, elapsed/duration); yield return null; } source.volume = target; if (target <= 0f) source.Stop(); musicFadeRoutine = null; }

        private void HandleClick()
        {
            if (aimedButton != null && aimedButton.OnClick != null) { aimedButton.OnClick(); return; }
            Slot slot = aimedSlot;
            if (slot == null && Cursor.visible) slot = SlotUnderPointer();
            if (slot != null) Spawn(slot);
        }

        private Slot AimedSlot() { return SlotAt(screenCenter); }
        private ButtonSlot AimedButton() { return ButtonAt(screenCenter); }
        private Slot SlotAt(Vector2 point) { for (int i=0;i<slots.Count;i++){ Slot s=slots[i]; if(s.Rect==null||!s.Rect.gameObject.activeInHierarchy) continue; if(RectTransformUtility.RectangleContainsScreenPoint(s.Rect,point,aimCamera)) return s; } return null; }
        private ButtonSlot ButtonAt(Vector2 point) { for (int i=0;i<buttons.Count;i++){ ButtonSlot b=buttons[i]; if(b.Rect==null||!b.Rect.gameObject.activeInHierarchy) continue; if(RectTransformUtility.RectangleContainsScreenPoint(b.Rect,point,aimCamera)) return b; } return null; }
        private Slot SlotUnderPointer() { Mouse mouse = Mouse.current; if(mouse==null)return null; return SlotAt(mouse.position.ReadValue()); }

        private void Spawn(Slot slot)
        {
            // Мобы из терминала не спавнятся: на странице "Мобы" лежат только
            // спавн-кубы, моб появляется от их броска (см. PlayerHand).
            if (terminal == null || slot == null || slot.Item == null) return;
            if (terminal.SpawnItem(slot.Item)) slot.Flash = 0.35f;
        }

        private void UpdateSlots()
        {
            aiming = aimedSlot != null;
            float delta = Time.deltaTime;
            for (int i=0;i<slots.Count;i++)
            {
                Slot slot = slots[i];
                bool hovered = ReferenceEquals(slot, aimedSlot);
                if (slot.Flash > 0f && slot.Frame != null)
                {
                    slot.Flash -= delta;
                    float pulse = Mathf.Clamp01(slot.Flash/0.35f);
                    slot.Frame.color = Color.Lerp(hovered?hoverColor:frameColor, TerminalTheme.PressCell, pulse);
                    continue;
                }
                if (slot.Frame == null || slot.Aimed == hovered) continue;
                slot.Aimed = hovered;
                slot.Frame.color = hovered ? hoverColor : frameColor;
            }
        }

        private void UpdateButtons()
        {
            for (int i=0;i<buttons.Count;i++)
            {
                ButtonSlot b = buttons[i];
                bool hovered = ReferenceEquals(b, aimedButton);
                if (b.Frame == null || b.Aimed == hovered) continue;
                b.Aimed = hovered;
                b.Frame.color = hovered ? hoverColor : frameColor;
                if (b.Label != null) b.Label.color = hovered ? Color.white : textColor;
            }
        }

        private bool InRange(SpawnTerminal candidate) { Vector3 origin = player!=null?player.transform.position:aimCamera.transform.position; return Vector3.Distance(origin,candidate.transform.position)<=candidate.ShowDistance; }
        private SpawnTerminal FindNearestTerminal()
        {
            SpawnTerminal[] all = FindObjectsByType<SpawnTerminal>(FindObjectsSortMode.None);
            if (all==null||all.Length==0)return null;
            Vector3 origin = player!=null?player.transform.position:aimCamera.transform.position;
            SpawnTerminal best=null; float bestDist=float.MaxValue;
            for (int i=0;i<all.Length;i++)
            {
                SpawnTerminal c = all[i]; if(c==null)continue;
                bool active = c.PlayerInsideActive || (c.ActiveTrigger==null && InRange(c));
                if(!active)continue;
                float d = Vector3.Distance(origin,c.transform.position);
                if(d>=bestDist)continue;
                bestDist=d; best=c;
            }
            return best;
        }

        private void Build()
        {
            ClearAll();
            if (ResolveCamera()==null)return;
            EnsureRig();
            ResolveFacing();
            builtFor = terminal;
            if (terminal == null) return;

            if (terminal != null && terminal.Mode == SpawnTerminal.TerminalMode.Admin) isAdminMode = true;
            else isAdminMode = false;

            if (isAdminMode)
            {
                BuildAdminLayout();
            }
            else
            {
                if (uiState == UiState.Main) BuildMainMenu();
                else BuildSection();
            }

            if (terminal != null && countLabel != null) countLabel.text = terminal.CurrentCount.ToString();
        }

        private void BuildMainMenu()
        {
            if (content == null || terminal == null) return;
            float pitch = Mathf.Max(0.01f, windowWidth / 7.2f);
            float y = windowHeight * 0.22f;
            float w = windowWidth * 0.72f;
            float h = pitch * 1.05f;

            CreateLargeButton(0f, y, w, h, normalText, () => {
                uiState = UiState.Section;
                normalSectionIndex = 0;
                terminal.SetCategory(MapSectionToCategory(normalSectionIndex));
                builtFor = null;
            }, frameColor);

            CreateLargeButton(0f, y - h * 1.35f, w, h, adminText, () => {
                if (terminal == null) return;
                terminal.SetMode(SpawnTerminal.TerminalMode.Admin);
                isAdminMode = true;
                adminSectionIndex = 0;
                uiState = UiState.Main;
                builtFor = null;
            }, frameColor);

            BuildCountBar();
        }

        private void BuildAdminLayout()
        {
            if (content == null || terminal == null) return;
            // Админский режим — страницы "Мобы" и "Объекты", оформленные
            // точно так же, как страницы обычного раздела (заголовок сверху,
            // сетка предметов, стрелки < > и кнопки внизу).
            adminSectionIndex = Mathf.Clamp(adminSectionIndex, 0, AdminSectionNames.Length - 1);
            BuildSectionPage(AdminSectionNames[adminSectionIndex], MapAdminSectionToCategory(adminSectionIndex));
        }

        private void BuildSection()
        {
            if (content == null || terminal == null) return;
            string sectionName = normalSectionIndex >= 0 && normalSectionIndex < NormalSectionNames.Length ? NormalSectionNames[normalSectionIndex] : GetCatName(terminal.CurrentCategory);
            BuildSectionPage(sectionName, MapSectionToCategory(normalSectionIndex));
        }

        private void BuildSectionPage(string sectionName, SpawnTerminal.TerminalCategory category)
        {
            if (content == null || terminal == null) return;
            float pitch = Mathf.Max(0.01f, windowWidth / 8.5f);
            float titleY = windowHeight * 0.5f - pitch * 0.9f;
            sectionTitleLabel = CreateLabel("SectionTitle", sectionName, windowWidth * 0.72f, pitch * 1.05f, 0f, titleY, titleColor, content, TextAlignmentOptions.Center);

            terminal.SetCategory(category);
            BuildContent();
            BuildBottomControls();
        }

        private void ChangeSection(int delta)
        {
            if (terminal == null) return;
            if (isAdminMode)
            {
                int count = AdminSectionNames.Length;
                adminSectionIndex = (adminSectionIndex + delta + count) % count;
                terminal.SetCategory(MapAdminSectionToCategory(adminSectionIndex));
            }
            else
            {
                int count = NormalSectionNames.Length;
                normalSectionIndex = (normalSectionIndex + delta + count) % count;
                terminal.SetCategory(MapSectionToCategory(normalSectionIndex));
            }
            builtFor = null;
        }

        private SpawnTerminal.TerminalCategory MapSectionToCategory(int index)
        {
            switch (index)
            {
                case 0: return SpawnTerminal.TerminalCategory.WeaponsTools;
                case 1: return SpawnTerminal.TerminalCategory.Consumables;
                case 2: return SpawnTerminal.TerminalCategory.Building;
                case 3: return SpawnTerminal.TerminalCategory.Currency;
                case 4: return SpawnTerminal.TerminalCategory.Craft;
                default: return SpawnTerminal.TerminalCategory.WeaponsTools;
            }
        }

        private SpawnTerminal.TerminalCategory MapAdminSectionToCategory(int index)
        {
            switch (index)
            {
                case 0: return SpawnTerminal.TerminalCategory.Mobs;
                case 1: return SpawnTerminal.TerminalCategory.Objects;
                case 2: return SpawnTerminal.TerminalCategory.AdminTools;
                default: return SpawnTerminal.TerminalCategory.Mobs;
            }
        }

        private void BuildCountBar()
        {
            BuildBottomControls();
        }

        private void BuildBottomControls()
        {
            if (content == null || terminal == null) return;
            // Всё вписано в окно относительно его центра: "Назад" — левее
            // центра, счётчик и стрелки — правее центра, к краям — с запасом,
            // чтобы ничего не выходило за пределы и не слипалось.
            float pitch = Mathf.Max(0.01f, windowWidth / 8.5f);
            float margin = pitch * 0.3f;
            float gap = pitch * 0.3f;
            float bottomY = -windowHeight * 0.5f + pitch * 1.05f;
            float btnH = pitch * 0.9f;
            float rightEdge = windowWidth * 0.5f - margin;

            float backW = pitch * 2.0f;
            float backX = -windowWidth * 0.5f + margin + backW * 0.5f;
            backButton = CreateButtonUI(backX, bottomY, backW, btnH, backText, () => {
                if (isAdminMode)
                {
                    if (terminal != null) terminal.SetMode(SpawnTerminal.TerminalMode.Normal);
                    isAdminMode = false;
                    adminSectionIndex = 0;
                    uiState = UiState.Main;
                    builtFor = null;
                    return;
                }
                uiState = UiState.Main;
                builtFor = null;
            }, frameColor);

            float minusW = pitch * 0.85f;
            float plusW = pitch * 0.85f;
            float labelW = pitch * 1.3f;
            float countW = minusW + gap + labelW + gap + plusW;
            float countX = Mathf.Max(rightEdge - countW, gap);
            countMinus = CreateButtonUI(countX + minusW * 0.5f, bottomY, minusW, btnH, "-", () => {
                if (terminal == null) return;
                terminal.AdjustCount(-1);
                if (countLabel != null) countLabel.text = terminal.CurrentCount.ToString();
            }, frameColor);
            countLabel = CreateLabel("Count", terminal.CurrentCount.ToString(), labelW, btnH, countX + minusW + gap + labelW * 0.5f, bottomY, textColor, content, TextAlignmentOptions.Center);
            countPlus = CreateButtonUI(countX + minusW + gap + labelW + gap + plusW * 0.5f, bottomY, plusW, btnH, "+", () => {
                if (terminal == null) return;
                terminal.AdjustCount(1);
                if (countLabel != null) countLabel.text = terminal.CurrentCount.ToString();
            }, frameColor);

            if (uiState == UiState.Section || isAdminMode)
            {
                float arrowW = pitch * 0.95f;
                float pageY = bottomY + pitch * 1.25f;
                // Стрелки: левая — в левой половине окна, правая — в правой,
                // чтобы листать страницы не сдвигали управление в одну сторону.
                float rightArrowX = rightEdge - arrowW * 0.5f;
                float leftArrowX = -rightEdge + arrowW * 0.5f;
                rightArrow = CreateArrow(rightArrowX, pageY, arrowW, arrowW, ">", () => ChangeSection(1));
                leftArrow = CreateArrow(leftArrowX, pageY, arrowW, arrowW, "<", () => ChangeSection(-1));
            }
        }

        private void BuildBottomBar()
        {
        }

        private void BuildContent()
        {
            if (content == null || terminal == null) return;
            ItemDef[] items = System.Array.Empty<ItemDef>();

            SpawnTerminal.CategoryEntry entry = null;
            SpawnTerminal.CategoryEntry[] cats = terminal.Categories;
            if (cats != null)
            {
                for (int i = 0; i < cats.Length; i++)
                {
                    if (cats[i] == null) continue;
                    if (cats[i].category == terminal.CurrentCategory)
                    {
                        entry = cats[i];
                        break;
                    }
                }
            }

            if (entry != null)
            {
                items = entry.items ?? System.Array.Empty<ItemDef>();
            }
            else
            {
                // Категории ещё не построены (autoFill выключен): показываем пусто.
                items = System.Array.Empty<ItemDef>();
            }

            // Мобы в сетке не показываются: на странице "Мобы" лежат только
            // спавн-кубы, поэтому сетка везде строится одинаково.
            items = FilterWorldItems(items);
            int itemCount = items != null ? items.Length : 0;
            int itemCols = Mathf.Max(1, columns);
            int itemRows = Mathf.CeilToInt(itemCount/(float)itemCols);

            float topOffset = windowHeight * 0.5f - Mathf.Max(0.01f, windowWidth / 9f) * 1.6f;
            float bottomOffset = -windowHeight * 0.5f + Mathf.Max(0.01f, windowWidth / 9f) * 1.6f;
            float availableHeight = topOffset - bottomOffset - 0.4f;

            float pitch = Mathf.Max(0.01f, Mathf.Min(windowWidth/Mathf.Max(1,itemCols), availableHeight/Mathf.Max(1f,itemRows)));
            float contentW = itemCols*pitch;
            float y = topOffset - pitch * 0.1f;

            for (int i=0;i<itemCount;i++)
            {
                int col = i%itemCols; int row = i/itemCols;
                float x = -contentW*0.5f + pitch*(col+0.5f);
                float slotY = y - pitch*(row+0.5f);
                CreateItemSlot(items[i], new Vector2(x,slotY), pitch);
            }
        }

        private static ItemDef[] FilterWorldItems(ItemDef[] all)
        {
            if (all==null) return System.Array.Empty<ItemDef>();
            ItemWorldCatalog catalog = ItemWorldCatalog.Instance;
            if (catalog==null) return all;
            List<ItemDef> kept = new List<ItemDef>(all.Length);
            for (int i=0;i<all.Length;i++) if (all[i]!=null && catalog.Has(all[i])) kept.Add(all[i]);
            return kept.ToArray();
        }

        private void CreateItemSlot(ItemDef def, Vector2 pos, float pitch)
        {
            if (def==null)return;
            string caption = showCaptions ? def.DisplayName : string.Empty;
            CreateSlot(pos, caption, pitch, def);
            Slot slot = null;
            for (int i=slots.Count-1;i>=0;i--) { if (slots[i].Item == def) { slot = slots[i]; break; } }
            if (slot != null) ApplyPreview(slot, "item:"+def.Id, ()=>ItemPreviewService.Instance!=null?ItemPreviewService.Instance.Get(def, iconPadding):null);
        }

        private void ApplyPreview(Slot slot, string key, System.Func<Texture2D> fetch) { ItemPreviewService p = ItemPreviewService.Instance; if(p==null||slot.Icon==null)return; Texture2D t = fetch(); if(t!=null) slot.Icon.texture = t; }

        private void CreateSlot(Vector2 pos, string caption, float pitch, ItemDef item)
        {
            GameObject root = new GameObject("Slot", typeof(RectTransform));
            root.layer = gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(content,false); rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(pitch,pitch); rect.anchoredPosition=pos;
            root.AddComponent<RectMask2D>().softness=Vector2Int.zero;

            bool withCaption = !string.IsNullOrEmpty(caption);
            float captionH = withCaption ? pitch*captionRatio : 0f;

            GameObject frameGo = new GameObject("Frame", typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            frameGo.layer = gameObject.layer;
            RectTransform frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.SetParent(rect,false); frameRect.anchorMin=frameRect.anchorMax=new Vector2(0.5f,0.5f); frameRect.sizeDelta=new Vector2(pitch*0.94f,pitch*0.94f); frameRect.anchoredPosition = withCaption? new Vector2(0f,pitch*0.03f):Vector2.zero;
            Image frameImg = frameGo.GetComponent<Image>(); frameImg.color=frameColor; frameImg.raycastTarget=false; TerminalTheme.ApplyCell(frameImg);

            float side = Mathf.Max(0.001f, withCaption ? Mathf.Min(pitch*iconScale, pitch-captionH-pitch*0.14f) : Mathf.Min(pitch*iconScale,pitch*0.98f));
            GameObject iconGo = new GameObject("Icon", typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));
            iconGo.layer = gameObject.layer;
            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.SetParent(rect,false); iconRect.anchorMin=iconRect.anchorMax=new Vector2(0.5f,0.5f); iconRect.sizeDelta=new Vector2(side,side); iconRect.anchoredPosition = withCaption? new Vector2(0f, captionH*0.5f+pitch*0.04f):Vector2.zero;
            RawImage iconImg = iconGo.GetComponent<RawImage>(); iconImg.color=Color.white; iconImg.raycastTarget=false;

            Slot slot = new Slot{Rect=rect, Frame=frameImg, Icon=iconImg, Item=item};
            slots.Add(slot);
            if (withCaption) CreateLabel("Caption", caption, pitch*0.98f, captionH, 0f, -pitch*0.5f+captionH*0.5f, textColor, rect, TextAlignmentOptions.Center);
        }

        private ButtonSlot CreateButton(float x,float y,float w,float h,string text,System.Action onClick,Color col)
        {
            GameObject root = new GameObject("Btn", typeof(RectTransform));
            root.layer = gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(content,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(w,h); rect.anchoredPosition=new Vector2(x,y);
            GameObject frameGo = new GameObject("Frame", typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            frameGo.layer = gameObject.layer;
            RectTransform fr = frameGo.GetComponent<RectTransform>(); fr.SetParent(rect,false); fr.anchorMin=fr.anchorMax=new Vector2(0.5f,0.5f); fr.sizeDelta=new Vector2(w*0.98f,h*0.9f); fr.anchoredPosition=Vector2.zero;
            Image fimg = frameGo.GetComponent<Image>(); fimg.color=col; fimg.raycastTarget=false; TerminalTheme.ApplyCell(fimg);
            TextMeshProUGUI lbl = CreateLabel("Label", text, w*0.92f, h*0.72f, 0f,0f, textColor, rect, TextAlignmentOptions.Center);
            ButtonSlot bs = new ButtonSlot{Rect=rect, Frame=fimg, Label=lbl, OnClick=onClick};
            buttons.Add(bs);
            return bs;
        }

        private TextMeshProUGUI CreateLabel(string name,string text,float width,float height,float x,float y,Color color,RectTransform parent)
        {
            return CreateLabel(name,text,width,height,x,y,color,parent,TextAlignmentOptions.Center);
        }

        private TextMeshProUGUI CreateLabel(string name,string text,float width,float height,float x,float y,Color color,RectTransform parent, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));
            go.layer = gameObject.layer;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent!=null?parent:content,false); rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(width,height); rect.anchoredPosition=new Vector2(x,y);
            float maxFont = Mathf.Max(0.005f, height*0.95f);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text; label.color=color; TerminalTheme.ApplyFont(label); label.alignment=alignment; label.raycastTarget=false;
            label.enableAutoSizing=true; label.fontSizeMin=maxFont*0.4f; label.fontSizeMax=maxFont*1.8f; label.fontSize=maxFont*1.5f; label.overflowMode=TextOverflowModes.Overflow;
            return label;
        }

        private string GetCatName(SpawnTerminal.TerminalCategory c)
        {
            switch(c){
                case SpawnTerminal.TerminalCategory.All: return "ВСЕ";
                case SpawnTerminal.TerminalCategory.Consumables: return "РАСХОДНИКИ";
                case SpawnTerminal.TerminalCategory.Currency: return "ВАЛЮТА";
                case SpawnTerminal.TerminalCategory.WeaponsTools: return "ОРУЖИЕ/ИНСТР";
                case SpawnTerminal.TerminalCategory.Building: return "ПОСТРОЙКИ";
                case SpawnTerminal.TerminalCategory.Craft: return "КРАФТ";
                case SpawnTerminal.TerminalCategory.Mobs: return "МОБЫ";
                case SpawnTerminal.TerminalCategory.Objects: return "ОБЪЕКТЫ";
                case SpawnTerminal.TerminalCategory.AdminTools: return "АДМИНСКИЕ ИНСТРУМЕНТЫ";
                default: return c.ToString();
            }
        }

        private void EnsureRig()
        {
            if (rig != null) return;
            GameObject rigGo = new GameObject("TerminalPanel");
            rig = rigGo.transform;
            rigGo.layer = terminal!=null?terminal.gameObject.layer:gameObject.layer;
            if (terminal != null) rig.SetParent(terminal.transform,false);
            else if (transform.parent != null) rig.SetParent(transform.parent,false);
            else rig.SetParent(null,false);

            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.transform.SetParent(rig,false);
            canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(windowWidth, windowHeight);

            GameObject backdropGo = new GameObject("Backdrop", typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            backdrop = backdropGo.GetComponent<RectTransform>(); backdrop.SetParent(canvasRect,false); backdrop.anchorMin=backdrop.anchorMax=new Vector2(0.5f,0.5f); backdrop.sizeDelta = new Vector2(windowWidth, windowHeight);
            Image bi = backdropGo.GetComponent<Image>(); bi.color=backdropColor; bi.raycastTarget=false; TerminalTheme.ApplyBackdrop(bi); NineSliceSkin.Apply(bi);

            GameObject topGo = new GameObject("TopBar", typeof(RectTransform));
            topGo.layer = rigGo.layer; topBar = topGo.GetComponent<RectTransform>(); topBar.SetParent(canvasRect,false); topBar.anchorMin=topBar.anchorMax=new Vector2(0.5f,0.5f);

            GameObject botGo = new GameObject("BottomBar", typeof(RectTransform));
            botGo.layer = rigGo.layer; bottomBar = botGo.GetComponent<RectTransform>(); bottomBar.SetParent(canvasRect,false); bottomBar.anchorMin=bottomBar.anchorMax=new Vector2(0.5f,0.5f);

            GameObject contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.layer = rigGo.layer; content = contentGo.GetComponent<RectTransform>(); content.SetParent(canvasRect,false); content.anchorMin=content.anchorMax=new Vector2(0.5f,0.5f); content.sizeDelta = new Vector2(windowWidth, windowHeight);

            rigGo.SetActive(visible);
        }

        private void ClearAll()
        {
            slots.Clear();
            buttons.Clear();
            sectionTitleLabel = null;
            leftArrow = null;
            rightArrow = null;
            backButton = null;
            countMinus = null;
            countPlus = null;
            countLabel = null;
            if (content==null)return;
            for (int i=content.childCount-1;i>=0;i--)
            {
                Transform c=content.GetChild(i);
                if(c==null)continue;
                GameObject go = c.gameObject;
                go.SetActive(false);
                Destroy(go);
            }
        }

        private void ClearSlots() { slots.Clear(); }
        private void ClearButtons() { buttons.Clear(); }

        private ButtonSlot CreateButtonOld(float x,float y,float w,float h,string text,System.Action onClick,Color col)
        {
            return CreateButtonUI(x,y,w,h,text,onClick,col);
        }

        private ButtonSlot CreateButtonUI(float x,float y,float w,float h,string text,System.Action onClick,Color col)
        {
            GameObject root = new GameObject("Btn", typeof(RectTransform));
            root.layer = gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(content,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(w,h); rect.anchoredPosition=new Vector2(x,y);
            GameObject frameGo = new GameObject("Frame", typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            frameGo.layer = gameObject.layer;
            RectTransform fr = frameGo.GetComponent<RectTransform>(); fr.SetParent(rect,false); fr.anchorMin=fr.anchorMax=new Vector2(0.5f,0.5f); fr.sizeDelta=new Vector2(w*0.98f,h*0.9f); fr.anchoredPosition=Vector2.zero;
            Image fimg = frameGo.GetComponent<Image>(); fimg.color=col; fimg.raycastTarget=false; TerminalTheme.ApplyCell(fimg);
            TextMeshProUGUI lbl = CreateLabel("Label", text, w*0.92f, h*0.72f, 0f,0f, textColor, rect, TextAlignmentOptions.Center);
            ButtonSlot bs = new ButtonSlot{Rect=rect, Frame=fimg, Label=lbl, OnClick=onClick};
            buttons.Add(bs);
            return bs;
        }

        private ButtonSlot CreateLargeButton(float x,float y,float w,float h,string text,System.Action onClick,Color col)
        {
            GameObject root = new GameObject("MenuBtn", typeof(RectTransform));
            root.layer = gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(content,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(w,h); rect.anchoredPosition=new Vector2(x,y);
            GameObject frameGo = new GameObject("Frame", typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            frameGo.layer = gameObject.layer;
            RectTransform fr = frameGo.GetComponent<RectTransform>(); fr.SetParent(rect,false); fr.anchorMin=fr.anchorMax=new Vector2(0.5f,0.5f); fr.sizeDelta=new Vector2(w*0.995f,h*0.94f); fr.anchoredPosition=Vector2.zero;
            Image fimg = frameGo.GetComponent<Image>(); fimg.color=col; fimg.raycastTarget=false; TerminalTheme.ApplyButton(fimg);
            TextMeshProUGUI lbl = CreateLabel("Label", text, w*0.96f, h*0.86f, 0f,0f, textColor, rect, TextAlignmentOptions.Center);
            ButtonSlot bs = new ButtonSlot{Rect=rect, Frame=fimg, Label=lbl, OnClick=onClick};
            buttons.Add(bs);
            return bs;
        }

        private ButtonSlot CreateArrow(float x,float y,float w,float h,string text,System.Action onClick)
        {
            GameObject root = new GameObject("Arrow", typeof(RectTransform));
            root.layer = gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(content,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f); rect.sizeDelta=new Vector2(w,h); rect.anchoredPosition=new Vector2(x,y);
            GameObject frameGo = new GameObject("Frame", typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            frameGo.layer = gameObject.layer;
            RectTransform fr = frameGo.GetComponent<RectTransform>(); fr.SetParent(rect,false); fr.anchorMin=fr.anchorMax=new Vector2(0.5f,0.5f); fr.sizeDelta=new Vector2(w*0.92f,h*0.92f); fr.anchoredPosition=Vector2.zero;
            Image fimg = frameGo.GetComponent<Image>(); fimg.color=frameColor; fimg.raycastTarget=false; TerminalTheme.ApplyCell(fimg);
            TextMeshProUGUI lbl = CreateLabel("Label", text, w*0.9f, h*0.9f, 0f,0f, arrowColor, rect, TextAlignmentOptions.Center);
            ButtonSlot bs = new ButtonSlot{Rect=rect, Frame=fimg, Label=lbl, OnClick=onClick};
            buttons.Add(bs);
            return bs;
        }

        private Camera ResolveCamera()
        {
            if (aimCamera!=null)return aimCamera;
            PlayerInteractor interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor!=null && interactor.AimCamera!=null) return aimCamera=interactor.AimCamera;
            return aimCamera=Camera.main;
        }

        private void OnDestroy() { if (musicFadeRoutine!=null) StopCoroutine(musicFadeRoutine); if (Instance==this) Instance=null; }
    }
}
