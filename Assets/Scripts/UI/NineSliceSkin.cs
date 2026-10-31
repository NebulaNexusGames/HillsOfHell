using UnityEngine;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Накладывает материал с 9-слайс шейдером на окна, ячейки и кнопки, чтобы
    /// одна текстура тянулась под любой размер без искажения углов.
    ///
    /// Материал лежит в <c>Resources/UI/NineSliceWindow</c> и подгружается один
    /// раз. Если материала нет, все вызовы просто ничего не делают — интерфейс
    /// остаётся плоским, как раньше.
    /// </summary>
    public static class NineSliceSkin
    {
        private const string WindowPath = "UI/NineSliceWindow";
        private const string OutlinePath = "UI/NineSliceButton";

        private static Material cachedWindow;
        private static bool searchedWindow;
        private static Material cachedOutline;
        private static bool searchedOutline;

        private static readonly int TintFrameId = Shader.PropertyToID("_TintFrame");

        /// <summary>
        /// Материал окна: рамка берёт цвет текстуры, интерьер — цвет вершины.
        /// Им оформляют окна и полосы здоровья, где рамка не должна крашиться
        /// состоянием наведения.
        /// </summary>
        public static Material Material
        {
            get
            {
                if (!searchedWindow)
                {
                    cachedWindow = Resources.Load<Material>(WindowPath);

                    // Не запоминаем неудачу: если ассет ещё не импортирован,
                    // следующий вызов попробует снова. Иначе первый промах
                    // навсегда оставил бы интерфейс без материала окна.
                    searchedWindow = cachedWindow != null;
                }

                return cachedWindow;
            }
        }

        /// <summary>
        /// Материал кнопки/ячейки: рамка красится цветом вершины (наведение —
        /// белым, нажатие — красным), а интерьер включается альфой вершины.
        /// Берём ассет <c>Resources/UI/NineSliceButton</c>; если его нет —
        /// делаем рантайм-копию материала окна. В обоих случаях гарантируем
        /// <c>_TintFrame = 1</c>.
        /// </summary>
        public static Material OutlineMaterial
        {
            get
            {
                if (!searchedOutline)
                {
                    cachedOutline = Resources.Load<Material>(OutlinePath);

                    if (cachedOutline == null)
                    {
                        Material source = Material;

                        if (source != null)
                        {
                            cachedOutline = new Material(source);
                            cachedOutline.name = "NineSliceButton (runtime)";
                        }
                    }

                    // Запоминаем только удачный результат: промах не должен
                    // навсегда лишить кнопки рамки. Если ни ассета, ни материала
                    // окна ещё нет, следующий вызов попробует снова.
                    if (cachedOutline != null)
                    {
                        if (cachedOutline.HasProperty(TintFrameId)
                            && cachedOutline.GetFloat(TintFrameId) <= 0f)
                        {
                            cachedOutline.SetFloat(TintFrameId, 1f);
                        }

                        searchedOutline = true;
                    }
                }

                return cachedOutline;
            }
        }

        /// <summary>Ставит материал окна на графику, если он есть.</summary>
        public static void Apply(Graphic graphic)
        {
            Apply(graphic, Material);
        }

        /// <summary>Ставит материал кнопки/ячейки на графику, если он есть.</summary>
        public static void ApplyOutline(Graphic graphic)
        {
            Apply(graphic, OutlineMaterial);

            // Рамку рисует фрагментный шейдер, а альфа вершин в покое равна нулю.
            // CanvasRenderer по умолчанию выбрасывает меш, у которого альфа
            // вершин около нуля, и тогда никакой рамки не видно. Запрещаем
            // отсечение: меш должен рисоваться всегда, даже без заливки.
            if (graphic != null && graphic.canvasRenderer != null)
                graphic.canvasRenderer.cullTransparentMesh = false;
        }

        private static void Apply(Graphic graphic, Material material)
        {
            if (graphic == null) return;
            if (material == null) return;
            if (graphic.material != material) graphic.material = material;
        }
    }
}
