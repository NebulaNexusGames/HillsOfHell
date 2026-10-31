using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Оформление терминалов: спрайты ячеек, фона и кнопок плюс шрифт для
    /// любого текста. Один ассет настраивает вид всех терминалов сразу —
    /// каждый UI при построении спрашивает активную тему.
    ///
    /// Ассет ищется автоматически в <c>Resources/TerminalTheme</c>. Если его
    /// нет, все методы просто ничего не делают, и терминалы выглядят как
    /// раньше (плоские цветные прямоугольники встроенного шрифта).
    /// </summary>
    [CreateAssetMenu(menuName = "CarGame/Terminal Theme", fileName = "TerminalTheme")]
    public class TerminalTheme : ScriptableObject
    {
        /// <summary>Рамка ячейки/кнопки в покое: белая, без заливки.</summary>
        // Альфа не 0, а 0.5: меш с нулевой альфой всех вершин CanvasRenderer
        // выбрасывает, и рамка в покое исчезает. Порог в шейдере гасит заливку
        // при 0.5, поэтому покой остаётся рамкой без заливки.
        public static readonly Color IdleCell = new Color(1f, 1f, 1f, 0.5f);

        /// <summary>Ячейка/кнопка под курсором: белая рамка и белая заливка.</summary>
        public static readonly Color HoverCell = new Color(1f, 1f, 1f, 1f);

        /// <summary>Нажатие: красная рамка и красная заливка.</summary>
        public static readonly Color PressCell = new Color(1f, 0.13f, 0.13f, 1f);

        [Header("Фон окна")]
        [Tooltip("Спрайт заднего фона терминала. Пусто — останется плоский цвет.")]
        [SerializeField] private Sprite backdropSprite;

        [Header("Ячейка предмета")]
        [Tooltip("Спрайт рамки ячейки предмета. Пусто — останется плоский цвет.")]
        [SerializeField] private Sprite cellSprite;

        [Header("Кнопка")]
        [Tooltip("Спрайт кнопки. Пусто — останется плоский цвет.")]
        [SerializeField] private Sprite buttonSprite;

        [Header("Шрифт")]
        [Tooltip("Шрифт для всего текста терминалов. Пусто — останется встроенный.")]
        [SerializeField] private TMP_FontAsset font;

        private static TerminalTheme cached;
        private static bool searched;

        /// <summary>Активная тема из Resources, либо null.</summary>
        public static TerminalTheme Active
        {
            get
            {
                if (cached == null && !searched)
                {
                    cached = Resources.Load<TerminalTheme>("TerminalTheme");
                    searched = true;
                }
                return cached;
            }
        }

        /// <summary>Перечитать ассет из Resources (после его изменения в игре).</summary>
        public static void Reload()
        {
            cached = Resources.Load<TerminalTheme>("TerminalTheme");
        }

        public static void ApplyBackdrop(Image image)
        {
            TerminalTheme theme = Active;
            if (theme == null) return;
            ApplyImage(image, theme.backdropSprite);
        }

        public static void ApplyCell(Image image)
        {
            NineSliceSkin.ApplyOutline(image);

            TerminalTheme theme = Active;
            if (theme == null) return;
            ApplyImage(image, theme.cellSprite);
        }

        public static void ApplyButton(Image image)
        {
            NineSliceSkin.ApplyOutline(image);

            TerminalTheme theme = Active;
            if (theme == null) return;
            ApplyImage(image, theme.buttonSprite);
        }

        public static void ApplyFont(TMP_Text text)
        {
            TerminalTheme theme = Active;
            if (theme == null || theme.font == null || text == null) return;
            text.font = theme.font;
        }

        private static void ApplyImage(Image image, Sprite sprite)
        {
            if (image == null || sprite == null) return;

            image.sprite = sprite;
            image.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
        }
    }
}
