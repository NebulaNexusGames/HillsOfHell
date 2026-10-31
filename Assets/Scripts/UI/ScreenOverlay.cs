using UnityEngine;
using UnityEngine.UI;

namespace CarGame.UI
{
    /// <summary>
    /// Помощник для полноэкранных экранных оверлеев (виньетки, затемнения).
    /// Оверлей обязан жить под экранным <see cref="Canvas"/>, иначе UI-элемент
    /// не рисуется. Метод находит существующий экранный канвас или создаёт
    /// отдельный с высоким порядком сортировки.
    /// </summary>
    public static class ScreenOverlay
    {
        /// <summary>Возвращает экранный Canvas, при необходимости создавая новый.</summary>
        public static Canvas FindOrCreateScreenCanvas(string name)
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate != null && candidate.renderMode == RenderMode.ScreenSpaceOverlay && candidate.isActiveAndEnabled)
                    return candidate;
            }

            return CreateScreenCanvas(name, 100);
        }

        /// <summary>
        /// Создаёт отдельный экранный Canvas с заданным порядком сортировки.
        /// Нужен оверлеям, которые обязаны быть поверх всей остальной UI:
        /// если переиспользовать чужой канвас, оверлей окажется под ним.
        /// </summary>
        public static Canvas CreateScreenCanvas(string name, int sortingOrder)
        {
            GameObject go = new GameObject(name, typeof(Canvas));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            return canvas;
        }

        /// <summary>Создаёт полноэкранный Image под указанным родителем.</summary>
        public static Image CreateFullScreenImage(Transform parent, string name, int siblingIndex)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            if (siblingIndex >= 0) rect.SetSiblingIndex(siblingIndex);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }
    }
}
