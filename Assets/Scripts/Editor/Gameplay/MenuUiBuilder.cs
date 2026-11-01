using CarGame.Controls;
using CarGame.UI;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Общий сборщик экранных меню для инструментов настройки. Из одного набора
    /// хелперов собираются и главное меню, и пауза, и экран смерти — чтобы
    /// кнопки, панели и шрифты везде выглядели одинаково и правились в одном
    /// месте.
    ///
    /// Всё строится в текущей сцене обычными объектами UGUI: их видно в
    /// иерархии и можно править руками. Логику на кнопки вешает рантайм
    /// (MainMenuUI/PauseMenuUI/DeathScreenUI) — в редакторе создаётся только
    /// геометрия и ссылки.
    /// </summary>
    public static class MenuUiBuilder
    {
        public static readonly Color BackdropColor = new Color(0.03f, 0.05f, 0.08f, 1f);
        public static readonly Color PanelColor = new Color(0.06f, 0.09f, 0.13f, 0.94f);
        public static readonly Color ButtonColor = new Color(0.11f, 0.11f, 0.12f, 0.95f);
        public static readonly Color ButtonHover = new Color(0.75f, 0.76f, 0.80f, 0.95f);
        public static readonly Color TextColor = new Color(0.86f, 0.9f, 0.94f, 1f);
        public static readonly Color TitleColor = new Color(0.35f, 0.78f, 0.95f, 1f);

        public static Canvas BuildCanvas(string name, int sortingOrder)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Создать канвас " + name);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Создать EventSystem");
        }

        /// <summary>
        /// Гарантирует камеру в сцене меню. Инструмент сборки сцены (Create Main
        /// Menu Scene) начинает с пустой сцены, поэтому камеру и свет туда никто
        /// не кладёт: без камеры Game view пишет «No cameras rendering», а без
        /// AudioListener нет звука. Существующую камеру не дублируем.
        /// </summary>
        public static void EnsureCamera()
        {
            if (Object.FindFirstObjectByType<Camera>() != null) return;

            GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            Undo.RegisterCreatedObjectUndo(go, "Создать камеру");
            go.tag = "MainCamera";

            Camera camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackdropColor;
            camera.orthographic = true;

            UniversalAdditionalCameraData extra = go.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            camera.allowHDR = true;
        }

        /// <summary>
        /// Гарантирует направленный свет в сцене меню. Сцена, собранная с нуля
        /// инструментом, остаётся без источника света, и трёхмерный фон/предметы
        /// выглядят чёрными. Существующий направленный свет не трогаем.
        /// </summary>
        public static void EnsureDirectionalLight()
        {
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null && lights[i].type == LightType.Directional) return;

            GameObject go = new GameObject("Directional Light", typeof(Light));
            Undo.RegisterCreatedObjectUndo(go, "Создать направленный свет");

            Light light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.92f, 0.78f, 1f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            go.AddComponent<UniversalAdditionalLightData>();
        }

        /// <summary>
        /// Возвращает канвас с таким именем, если он уже есть в сцене,
        /// иначе создаёт новый. Нужно, чтобы повторный запуск инструмента
        /// обновлял меню, а не плодил копии.
        /// </summary>
        public static Canvas GetOrCreateCanvas(string name, int sortingOrder)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                Canvas found = existing.GetComponent<Canvas>();
                if (found != null) return found;
            }

            return BuildCanvas(name, sortingOrder);
        }

        /// <summary>Удаляет все дочерние объекты (с поддержкой Undo).</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }

        /// <summary>
        /// Возвращает компонент на объекте, добавляя его, если не найден.
        /// Повторный запуск инструмента переиспользует контроллер меню.
        /// </summary>
        public static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null) return component;

            return Undo.AddComponent<T>(target);
        }

        public static Image CreateFullscreen(Transform parent, string name, Color color, bool backdrop)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            if (backdrop) TerminalTheme.ApplyBackdrop(image);
            return image;
        }

        public static Image CreatePanel(Transform parent, string name, Vector2 position, Vector2 size, Color color, bool backdrop)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            if (backdrop) TerminalTheme.ApplyBackdrop(image);
            if (backdrop) NineSliceSkin.Apply(image);
            return image;
        }

        public static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, Vector2 position, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.color = color;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.raycastTarget = false;
            TerminalTheme.ApplyFont(label);
            return label;
        }

        public static Button CreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Image image = go.GetComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;
            TerminalTheme.ApplyButton(image);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.13f, 0.13f, 1f);
            colors.selectedColor = new Color(1f, 1f, 1f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            CreateLabel(rect, "Label", label, Vector2.zero, size * 0.92f, size.y * 0.46f, TextColor, TextAlignmentOptions.Center);
            return button;
        }

        public static Slider CreateSlider(Transform parent, string name, Vector2 position, float width, float height)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Slider));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = position;

            GameObject track = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.SetParent(rect, false);
            trackRect.anchorMin = new Vector2(0f, 0.3f);
            trackRect.anchorMax = new Vector2(1f, 0.7f);
            trackRect.offsetMin = Vector2.zero;
            trackRect.offsetMax = Vector2.zero;
            Image trackImage = track.GetComponent<Image>();
            trackImage.color = new Color(1f, 1f, 1f, 0.14f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.SetParent(rect, false);
            fillAreaRect.anchorMin = new Vector2(0f, 0.3f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.7f);
            fillAreaRect.offsetMin = new Vector2(6f, 0f);
            fillAreaRect.offsetMax = new Vector2(-16f, 0f);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.SetParent(fillAreaRect, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = ButtonHover;

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.SetParent(rect, false);
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(12f, 0f);
            handleAreaRect.offsetMax = new Vector2(-12f, 0f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.SetParent(handleAreaRect, false);
            handleRect.sizeDelta = new Vector2(24f, 0f);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = TextColor;

            Slider slider = root.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            return slider;
        }

        public static Toggle CreateToggle(Transform parent, string name, Vector2 position, float size)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = position;

            GameObject background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform bgRect = background.GetComponent<RectTransform>();
            bgRect.SetParent(rect, false);
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            Image bgImage = background.GetComponent<Image>();
            bgImage.color = Color.white;
            TerminalTheme.ApplyCell(bgImage);

            GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform checkRect = check.GetComponent<RectTransform>();
            checkRect.SetParent(bgRect, false);
            checkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;
            Image checkImage = check.GetComponent<Image>();
            checkImage.color = Color.white;

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            ColorBlock toggleColors = toggle.colors;
            toggleColors.normalColor = new Color(1f, 1f, 1f, 0f);
            toggleColors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            toggleColors.pressedColor = new Color(1f, 0.13f, 0.13f, 1f);
            toggleColors.selectedColor = new Color(1f, 1f, 1f, 1f);
            toggle.colors = toggleColors;
            toggle.isOn = true;
            return toggle;
        }

        /// <summary>
        /// Собирает готовую панель настроек с громкостью, чувствительностью и
        /// инверсией обзора, языком и кнопкой управления и вешает на неё
        /// <see cref="SettingsPanel"/>. Кнопку «Назад» возвращает наружу: её
        /// обработчик живёт в меню/паузе.
        ///
        /// Возвращается НЕПРОЗРАЧНЫЙ полноэкранный корень-«подложка» (SettingsRoot),
        /// внутри которого лежит карточка панели и скрытая панель управления.
        /// Меню/пауза переключают этот корень Показать/Скрыть — при открытых
        /// настройках ничего из-под панели не просвечивает, кнопки меню не
        /// кликаются, а вместе с листом прячется и панель управления.
        /// </summary>
        public static Image BuildSettingsPanel(Transform parent, out SettingsPanel panel, out Button back)
        {
            Image root = CreateFullscreen(parent, "SettingsRoot", BackdropColor, true);
            Image panelImage = CreatePanel(root.transform, "SettingsPanel", Vector2.zero, new Vector2(1180f, 860f), PanelColor, true);
            Transform host = panelImage.transform;

            Localize(CreateLabel(host, "Title", "НАСТРОЙКИ", new Vector2(0f, 320f), new Vector2(1100f, 96f), 72f, TitleColor, TextAlignmentOptions.Center), "НАСТРОЙКИ", "SETTINGS");

            Localize(CreateLabel(host, "VolumeLabel", "ГРОМКОСТЬ", new Vector2(-340f, 190f), new Vector2(360f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ГРОМКОСТЬ", "VOLUME");
            Slider volume = CreateSlider(host, "VolumeSlider", new Vector2(130f, 190f), 540f, 44f);
            TextMeshProUGUI volumeValue = CreateLabel(host, "VolumeValue", "100%", new Vector2(470f, 190f), new Vector2(150f, 52f), 36f, TitleColor, TextAlignmentOptions.Right);

            Localize(CreateLabel(host, "SensitivityLabel", "ЧУВСТВИТЕЛЬНОСТЬ", new Vector2(-340f, 100f), new Vector2(360f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ЧУВСТВИТЕЛЬНОСТЬ", "SENSITIVITY");
            Slider sensitivity = CreateSlider(host, "SensitivitySlider", new Vector2(130f, 100f), 540f, 44f);
            TextMeshProUGUI sensitivityValue = CreateLabel(host, "SensitivityValue", "1.00x", new Vector2(470f, 100f), new Vector2(150f, 52f), 36f, TitleColor, TextAlignmentOptions.Right);

            Localize(CreateLabel(host, "InvertLabel", "ИНВЕРТИРОВАТЬ ОБЗОР ПО Y", new Vector2(-220f, 10f), new Vector2(600f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ИНВЕРТИРОВАТЬ ОБЗОР ПО Y", "INVERT Y LOOK");
            Toggle invert = CreateToggle(host, "InvertToggle", new Vector2(320f, 10f), 48f);

            Localize(CreateLabel(host, "LanguageLabel", "ЯЗЫК", new Vector2(-300f, -80f), new Vector2(340f, 56f), 38f, TextColor, TextAlignmentOptions.Left), "ЯЗЫК", "LANGUAGE");
            Button language = CreateButton(host, "LanguageButton", "РУССКИЙ", new Vector2(300f, -80f), new Vector2(560f, 88f));
            Localize(language.GetComponentInChildren<TextMeshProUGUI>(), "РУССКИЙ", "ENGLISH");

            Button controls = CreateButton(host, "ControlsButton", "УПРАВЛЕНИЕ", new Vector2(0f, -170f), new Vector2(680f, 96f));
            Localize(controls.GetComponentInChildren<TextMeshProUGUI>(), "УПРАВЛЕНИЕ", "CONTROLS");

            Button graphics = CreateButton(host, "GraphicsButton", "ГРАФИКА", new Vector2(0f, -270f), new Vector2(680f, 96f));
            Localize(graphics.GetComponentInChildren<TextMeshProUGUI>(), "ГРАФИКА", "GRAPHICS");

            Button reset = CreateButton(host, "ResetButton", "СБРОС", new Vector2(-175f, -375f), new Vector2(300f, 84f));
            Localize(reset.GetComponentInChildren<TextMeshProUGUI>(), "СБРОС", "RESET");
            back = CreateButton(host, "BackButton", "НАЗАД", new Vector2(175f, -375f), new Vector2(300f, 84f));
            Localize(back.GetComponentInChildren<TextMeshProUGUI>(), "НАЗАД", "BACK");

            GameObject controlsRoot = BuildControlsMenu(parent);
            GameObject graphicsRoot = BuildGraphicsMenu(parent);

            panel = Undo.AddComponent<SettingsPanel>(panelImage.gameObject);
            SerializedObject so = new SerializedObject(panel);
            SetReference(so, "volumeSlider", volume);
            SetReference(so, "sensitivitySlider", sensitivity);
            SetReference(so, "invertYToggle", invert);
            SetReference(so, "resetButton", reset);
            SetReference(so, "volumeValue", volumeValue);
            SetReference(so, "sensitivityValue", sensitivityValue);
            SetReference(so, "languageButton", language);
            SetReference(so, "languageText", language.GetComponentInChildren<TextMeshProUGUI>());
            SetReference(so, "controlsButton", controls);
            SetReference(so, "controlsPanel", controlsRoot);
            SetReference(so, "graphicsButton", graphics);
            SetReference(so, "graphicsPanel", graphicsRoot);
            so.ApplyModifiedProperties();

            controlsRoot.SetActive(false);
            graphicsRoot.SetActive(false);
            return root;
        }

        /// <summary>
        /// Собирает отдельное окно графических настроек: PSX, световые блики,
        /// качество виньетки, тряска камеры, сглаживание, вертикальная
        /// синхронизация и лимит кадров. Возвращается корень, выключенный по
        /// умолчанию; слой затемнения — его сосед (как у меню управления).
        /// </summary>
        public static GameObject BuildGraphicsMenu(Transform parent)
        {
            Image dim = CreateFullscreen(parent, "GraphicsDim", new Color(0f, 0f, 0f, 0.92f), false);

            GameObject root = new GameObject("GraphicsMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Создать меню графики");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            bg.raycastTarget = true;

            GraphicsPanel panel = Undo.AddComponent<GraphicsPanel>(root);

            Image card = CreatePanel(root.transform, "Card", Vector2.zero, new Vector2(1000f, 860f), PanelColor, true);
            Transform host = card.transform;

            Localize(CreateLabel(host, "Title", "ГРАФИКА", new Vector2(0f, 340f), new Vector2(940f, 80f), 60f, TitleColor, TextAlignmentOptions.Center), "ГРАФИКА", "GRAPHICS");

            Localize(CreateLabel(host, "PsxLabel", "PSX-ЭФФЕКТ", new Vector2(-260f, 235f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "PSX-ЭФФЕКТ", "PSX EFFECT");
            Toggle psx = CreateToggle(host, "PsxToggle", new Vector2(360f, 235f), 48f);

            Localize(CreateLabel(host, "FlareLabel", "СВЕТОВЫЕ БЛИКИ", new Vector2(-260f, 157f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "СВЕТОВЫЕ БЛИКИ", "LIGHT FLARES");
            Toggle flare = CreateToggle(host, "FlareToggle", new Vector2(360f, 157f), 48f);

            Localize(CreateLabel(host, "VignetteLabel", "ВИНЬЕТКА", new Vector2(-260f, 79f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ВИНЬЕТКА", "VIGNETTE");
            Button vignette = CreateButton(host, "VignetteButton", "ВЫСОКОЕ", new Vector2(200f, 79f), new Vector2(400f, 72f));
            TMP_Text vignetteValue = vignette.GetComponentInChildren<TextMeshProUGUI>();

            Localize(CreateLabel(host, "ShakeLabel", "ТРЯСКА КАМЕРЫ", new Vector2(-260f, 1f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ТРЯСКА КАМЕРЫ", "CAMERA SHAKE");
            Toggle shake = CreateToggle(host, "ShakeToggle", new Vector2(360f, 1f), 48f);

            Localize(CreateLabel(host, "AaLabel", "СГЛАЖИВАНИЕ", new Vector2(-260f, -77f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "СГЛАЖИВАНИЕ", "ANTI-ALIASING");
            Button aa = CreateButton(host, "AaButton", "FXAA", new Vector2(200f, -77f), new Vector2(400f, 72f));
            TMP_Text aaValue = aa.GetComponentInChildren<TextMeshProUGUI>();

            Localize(CreateLabel(host, "VSyncLabel", "ВЕРТ. СИНХРОНИЗАЦИЯ", new Vector2(-260f, -155f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ВЕРТ. СИНХРОНИЗАЦИЯ", "VSYNC");
            Toggle vsync = CreateToggle(host, "VSyncToggle", new Vector2(360f, -155f), 48f);

            Localize(CreateLabel(host, "FpsLabel", "ЛИМИТ КАДРОВ", new Vector2(-260f, -233f), new Vector2(440f, 52f), 36f, TextColor, TextAlignmentOptions.Left), "ЛИМИТ КАДРОВ", "FPS LIMIT");
            Button fps = CreateButton(host, "FpsButton", "БЕЗ ОГРАНИЧЕНИЯ", new Vector2(200f, -233f), new Vector2(400f, 72f));
            TMP_Text fpsValue = fps.GetComponentInChildren<TextMeshProUGUI>();

            Button back = CreateButton(host, "BackButton", "НАЗАД", new Vector2(0f, -350f), new Vector2(300f, 80f));
            Localize(back.GetComponentInChildren<TextMeshProUGUI>(), "НАЗАД", "BACK");

            SerializedObject so = new SerializedObject(panel);
            SetReference(so, "dimLayer", dim.gameObject);
            SetReference(so, "psxToggle", psx);
            SetReference(so, "flareToggle", flare);
            SetReference(so, "vignetteButton", vignette);
            SetReference(so, "vignetteValue", vignetteValue);
            SetReference(so, "shakeToggle", shake);
            SetReference(so, "aaButton", aa);
            SetReference(so, "aaValue", aaValue);
            SetReference(so, "vsyncToggle", vsync);
            SetReference(so, "fpsButton", fps);
            SetReference(so, "fpsValue", fpsValue);
            SetReference(so, "backButton", back);
            so.ApplyModifiedProperties();

            root.SetActive(false);
            dim.gameObject.SetActive(false);
            return root;
        }

        /// <summary>
        /// Собирает верхнеуровневое меню управления: прозрачный корень, общий
        /// слой затемнения, список строк биндов со скроллом, кнопки СБРОС/НАЗАД и
        /// модальное окно переназначения. Всё создаётся в редакторе; рантайм лишь
        /// заполняет кнопки текущими клавишами и проводит переназначение.
        /// Корень и слой затемнения возвращаются выключенными.
        /// </summary>
        public static GameObject BuildControlsMenu(Transform parent)
        {
            Image dim = CreateFullscreen(parent, "ControlsDim", new Color(0f, 0f, 0f, 0.92f), false);

            GameObject root = new GameObject("ControlsMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Создать меню управления");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            bg.raycastTarget = true;

            ControlsPanel panel = Undo.AddComponent<ControlsPanel>(root);

            Localize(CreateLabel(root.transform, "Title", "УПРАВЛЕНИЕ",
                new Vector2(0f, 478f), new Vector2(1200f, 64f), 44f, TitleColor, TextAlignmentOptions.Center),
                "УПРАВЛЕНИЕ", "CONTROLS");

            GameObject scroll = new GameObject("List", typeof(RectTransform), typeof(ScrollRect));
            RectTransform scrollRect = scroll.GetComponent<RectTransform>();
            scrollRect.SetParent(root.transform, false);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(70f, 104f);
            scrollRect.offsetMax = new Vector2(-70f, -112f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(scrollRect, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            VerticalLayoutGroup group = content.GetComponent<VerticalLayoutGroup>();
            group.spacing = 8f;
            group.padding = new RectOffset(20, 20, 14, 14);
            group.childAlignment = TextAnchor.UpperCenter;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollComp = scroll.GetComponent<ScrollRect>();
            scrollComp.content = contentRect;
            scrollComp.viewport = viewportRect;
            scrollComp.horizontal = false;
            scrollComp.vertical = true;
            scrollComp.movementType = ScrollRect.MovementType.Clamped;
            scrollComp.scrollSensitivity = 44f;

            List<RowRef> rowRefs = new List<RowRef>();
            KeyBindings.Category[] categories = KeyBindings.Categories;
            for (int c = 0; c < categories.Length; c++)
            {
                if (c > 0) BuildControlsSpacer(contentRect);
                BuildControlsHeader(contentRect, categories[c]);
                GameAction[] actions = categories[c].Actions;
                for (int i = 0; i < actions.Length; i++)
                    rowRefs.Add(BuildControlsRow(contentRect, actions[i]));
            }

            Button reset = CreateButton(root.transform, "ResetButton", "СБРОС", new Vector2(-170f, -462f), new Vector2(280f, 60f));
            Localize(reset.GetComponentInChildren<TextMeshProUGUI>(), "СБРОС", "RESET");
            Button back = CreateButton(root.transform, "BackButton", "НАЗАД", new Vector2(170f, -462f), new Vector2(280f, 60f));
            Localize(back.GetComponentInChildren<TextMeshProUGUI>(), "НАЗАД", "BACK");

            GameObject rebindRoot = BuildRebindWindow(root.transform, out TMP_Text rebindAction, out Image rebindFill);

            SerializedObject so = new SerializedObject(panel);
            SetReference(so, "dimLayer", dim.gameObject);
            SetReference(so, "resetButton", reset);
            SetReference(so, "backButton", back);
            SetReference(so, "rebindWindow", rebindRoot);
            SetReference(so, "rebindActionText", rebindAction);
            SetReference(so, "rebindTimerFill", rebindFill);

            SerializedProperty rowsProp = so.FindProperty("rows");
            if (rowsProp != null)
            {
                rowsProp.arraySize = rowRefs.Count;
                for (int i = 0; i < rowRefs.Count; i++)
                {
                    SerializedProperty element = rowsProp.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("Action").intValue = (int)rowRefs[i].Action;
                    element.FindPropertyRelative("Label").objectReferenceValue = rowRefs[i].Label;
                    element.FindPropertyRelative("KeyText").objectReferenceValue = rowRefs[i].KeyText;
                    element.FindPropertyRelative("KeyButton").objectReferenceValue = rowRefs[i].KeyButton;
                }
            }

            so.ApplyModifiedProperties();

            rebindRoot.SetActive(false);
            root.SetActive(false);
            dim.gameObject.SetActive(false);
            return root;
        }

        private sealed class RowRef
        {
            public GameAction Action;
            public TMP_Text Label;
            public TMP_Text KeyText;
            public Button KeyButton;
        }

        private static void BuildControlsSpacer(RectTransform parent)
        {
            GameObject spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            RectTransform rect = spacer.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            LayoutElement element = spacer.GetComponent<LayoutElement>();
            element.minHeight = 8f;
            element.preferredHeight = 8f;
            element.flexibleWidth = 1f;
        }

        private static void BuildControlsHeader(RectTransform parent, KeyBindings.Category category)
        {
            TextMeshProUGUI label = CreateLabel(parent, "Category_" + category.Actions[0], string.Empty,
                Vector2.zero, new Vector2(400f, 32f), 21f, TitleColor, TextAlignmentOptions.Left);

            LayoutElement element = label.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 32f;
            element.preferredHeight = 32f;
            element.flexibleWidth = 1f;

            Localize(label, category.Russian, category.English);
        }

        private static RowRef BuildControlsRow(RectTransform parent, GameAction action)
        {
            GameObject rowGo = new GameObject("Row_" + action, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rowRect = rowGo.GetComponent<RectTransform>();
            rowRect.SetParent(parent, false);
            Image rowImage = rowGo.GetComponent<Image>();
            rowImage.color = new Color(0.08f, 0.12f, 0.18f, 0.9f);
            rowImage.raycastTarget = false;

            HorizontalLayoutGroup layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(14, 14, 4, 4);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            LayoutElement rowElement = rowGo.AddComponent<LayoutElement>();
            rowElement.minHeight = 46f;
            rowElement.preferredHeight = 46f;

            TextMeshProUGUI label = CreateLabel(rowRect, "Label", KeyBindings.GetDisplayName(action),
                Vector2.zero, new Vector2(400f, 34f), 17f, TextColor, TextAlignmentOptions.Left);
            LayoutElement labelElement = label.gameObject.AddComponent<LayoutElement>();
            labelElement.flexibleWidth = 1f;
            labelElement.minWidth = 160f;

            Button keyButton = CreateButton(rowRect, "KeyButton", string.Empty, Vector2.zero, new Vector2(300f, 36f));
            LayoutElement keyElement = keyButton.gameObject.AddComponent<LayoutElement>();
            keyElement.minWidth = 300f;
            keyElement.preferredWidth = 300f;
            keyElement.preferredHeight = 36f;

            return new RowRef
            {
                Action = action,
                Label = label,
                KeyText = keyButton.GetComponentInChildren<TextMeshProUGUI>(),
                KeyButton = keyButton
            };
        }

        private static GameObject BuildRebindWindow(Transform parent, out TMP_Text actionText, out Image timerFill)
        {
            GameObject root = new GameObject("RebindWindow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(root, "Создать окно переназначения");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            Image backdrop = root.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.6f);
            backdrop.raycastTarget = true;

            Image card = CreatePanel(root.transform, "Card", Vector2.zero, new Vector2(780f, 340f), PanelColor, true);

            actionText = CreateLabel(card.transform, "ActionText", string.Empty,
                new Vector2(0f, 105f), new Vector2(720f, 60f), 34f, TextColor, TextAlignmentOptions.Center);

            Localize(CreateLabel(card.transform, "PromptText", "Нажмите клавишу или кнопку мыши…",
                new Vector2(0f, 35f), new Vector2(720f, 60f), 30f, TitleColor, TextAlignmentOptions.Center),
                "Нажмите клавишу или кнопку мыши…", "Press a key or mouse button…");

            Image timerBg = CreatePanel(card.transform, "TimerBg", new Vector2(0f, -95f), new Vector2(620f, 28f), new Color(1f, 1f, 1f, 0.16f), false);

            GameObject fillGo = new GameObject("TimerFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.SetParent(timerBg.transform, false);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            timerFill = fillGo.GetComponent<Image>();
            timerFill.color = ButtonHover;
            timerFill.type = Image.Type.Filled;
            timerFill.fillMethod = Image.FillMethod.Horizontal;
            timerFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            timerFill.fillAmount = 1f;

            return root;
        }

        /// <summary>
        /// Делает надпись двуязычной: показывает текст текущего языка и меняет
        /// его при переключении через <see cref="LocalizedText"/>.
        /// </summary>
        public static void Localize(TMP_Text label, string russian, string english)
        {
            if (label == null) return;

            label.text = Localization.IsEnglish ? english : russian;

            LocalizedText loc = label.GetComponent<LocalizedText>();
            if (loc == null) loc = label.gameObject.AddComponent<LocalizedText>();
            loc.Set(russian, english);
        }

        public static void Localize(Component target, string russian, string english)
        {
            if (target == null) return;
            TMP_Text label = target as TMP_Text;
            if (label == null) label = target.GetComponent<TMP_Text>();
            Localize(label, russian, english);
        }

        public static void SetReference(SerializedObject so, string property, Object value)
        {
            SerializedProperty prop = so.FindProperty(property);
            if (prop != null) prop.objectReferenceValue = value;
        }

        public static void SetString(SerializedObject so, string property, string value)
        {
            SerializedProperty prop = so.FindProperty(property);
            if (prop != null) prop.stringValue = value;
        }

        public static void SetBool(SerializedObject so, string property, bool value)
        {
            SerializedProperty prop = so.FindProperty(property);
            if (prop != null) prop.boolValue = value;
        }
    }
}
