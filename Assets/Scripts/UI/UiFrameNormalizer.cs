using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Держит рамку 9-слайса на кнопках интерфейса всегда, а не только под
    /// курсором. Сцены меню — авторские объекты, собранные до появления
    /// материала рамки, поэтому ссылки на него в них нет: без этого прохода
    /// кнопки паузы, главного меню, настроек и остальных экранов остаются
    /// плоскими. Проход при загрузке каждой сцены ставит материал кнопки на
    /// графику всех кнопок и переключателей; фоны окон оформляются отдельно.
    ///
    /// Вид кнопки: заливки нет, видна только рамка. Под курсором заливка и
    /// рамка белые, при нажатии — красные. Цвет вершины несёт и цвет рамки, и
    /// заливку: (r,g,b) — цвет, альфа — количество заливки.
    /// </summary>
    public static class UiFrameNormalizer
    {
        // Покой не ровно прозрачный (0), а 0.5: меш с нулевой альфой всех
        // вершин CanvasRenderer выбрасывает, и вместе с заливкой пропадает
        // обводка. Покой 0.5 заливки не даёт (её гасит порог в шейдере), но
        // меш остаётся живым, поэтому рамка видна без наведения.
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.5f);
        private static readonly Color ButtonHover = new Color(1f, 1f, 1f, 1f);
        private static readonly Color ButtonPressed = new Color(1f, 0.13f, 0.13f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Apply();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Apply();
        }

        private static void Apply()
        {
            Material outline = NineSliceSkin.OutlineMaterial;
            Material window = NineSliceSkin.Material;
            int tintId = Shader.PropertyToID("_TintFrame");
            Debug.Log($"[UiFrame] scene={SceneManager.GetActiveScene().name} " +
                      $"outline={(outline == null ? "NULL" : outline.name + "/" + outline.shader.name + (outline.HasProperty(tintId) ? " tint=" + outline.GetFloat(tintId) : ""))} " +
                      $"window={(window == null ? "NULL" : window.name + "/" + window.shader.name)}");

            if (outline == null) return;

            Button[] buttons = Object.FindObjectsByType<Button>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                NineSliceSkin.ApplyOutline(buttons[i].targetGraphic);
                StyleButton(buttons[i]);
            }

            Toggle[] toggles = Object.FindObjectsByType<Toggle>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < toggles.Length; i++)
            {
                NineSliceSkin.ApplyOutline(toggles[i].targetGraphic);
                StyleGraphic(toggles[i].targetGraphic);
                StyleToggle(toggles[i]);
            }

            Image[] images = Object.FindObjectsByType<Image>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < images.Length; i++)
            {
                string name = images[i].gameObject.name;
                if (name == "Card" || name == "SettingsPanel")
                {
                    NineSliceSkin.Apply(images[i]);
                }
            }

            LogDiagnostics(buttons);
        }

        private static void LogDiagnostics(Button[] buttons)
        {
            if (buttons == null || buttons.Length == 0) return;

            Button button = buttons[0];
            Graphic graphic = button.targetGraphic;
            if (graphic == null) { Debug.LogWarning("[UiFrameDiag] targetGraphic == NULL"); return; }

            Material material = graphic.material;
            CanvasRenderer cr = graphic.canvasRenderer;

            string frameAlpha = material != null && material.HasProperty("_FrameAlpha")
                ? material.GetFloat("_FrameAlpha").ToString("0.###") : "-";
            string frameColor = material != null && material.HasProperty("_FrameColor")
                ? material.GetColor("_FrameColor").ToString() : "-";
            string tint = material != null && material.HasProperty("_TintFrame")
                ? material.GetFloat("_TintFrame").ToString("0.###") : "-";
            string border = material != null && material.HasProperty("_Border")
                ? material.GetVector("_Border").ToString() : "-";
            Texture sliceTex = material != null && material.HasProperty("_SliceTex")
                ? material.GetTexture("_SliceTex") : null;

            Debug.Log($"[UiFrameDiag] button='{button.name}' graphic='{graphic.name}' " +
                      $"graphicColor={graphic.color} normal={button.colors.normalColor} " +
                      $"highlighted={button.colors.highlightedColor} transition={button.transition} " +
                      $"mat='{(material != null ? material.name : "null")}' " +
                      $"frameAlpha={frameAlpha} frameColor={frameColor} tint={tint} " +
                      $"border={border} sliceTex={(sliceTex != null ? sliceTex.name : "NULL")} " +
                      $"crColor={(cr != null ? cr.GetColor().ToString() : "null")} " +
                      $"crCull={(cr != null ? cr.cull.ToString() : "null")} " +
                      $"crCullTransparent={(cr != null ? cr.cullTransparentMesh.ToString() : "null")}");
        }

        /// <summary>
        /// Кнопка без заливки: заливка включается состоянием. Цвет вершины
        /// умножается на цвет состояния, поэтому лицо кнопки — белое, а альфа
        /// состояния задаёт количество заливки (0 в покое, 1 при наведении).
        /// </summary>
        private static void StyleButton(Button button)
        {
            if (button == null) return;

            StyleGraphic(button.targetGraphic);

            // Меш кнопки — всегда белый: рамку и заливку красит цвет состояния
            // (ColorBlock), который умножается на цвет вершины. Если оставить
            // в меше тёмный цвет из сцены, рамка в покое будет тёмной; если
            // альфу 0 — меш отсечётся и рамка пропадёт.
            if (button.targetGraphic != null)
                button.targetGraphic.color = Color.white;

            ColorBlock colors = button.colors;
            colors.normalColor = ButtonIdle;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.selectedColor = ButtonHover;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.25f);
            button.colors = colors;

            // Меняем цвета — меш перестраивается. Рамку рисует фрагментный
            // шейдер, и при нулевой альфе вершин меш нельзя отсекать, иначе
            // рамка в покое исчезнет. Подтверждаем флаг уже после смены цвета.
            if (button.targetGraphic != null && button.targetGraphic.canvasRenderer != null)
                button.targetGraphic.canvasRenderer.cullTransparentMesh = false;
        }

        private static void StyleToggle(Toggle toggle)
        {
            if (toggle == null) return;

            ColorBlock colors = toggle.colors;
            colors.normalColor = ButtonIdle;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.selectedColor = ButtonHover;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.25f);
            toggle.colors = colors;
        }

        private static void StyleGraphic(Graphic graphic)
        {
            if (graphic is Image image && image.sprite == null)
                image.color = Color.white;
        }
    }
}
