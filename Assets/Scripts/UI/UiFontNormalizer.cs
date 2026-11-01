using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.UI
{
    /// <summary>
    /// Страховка от рассинхрона шрифтов интерфейса.
    ///
    /// Сцены могут сохраниться со старым шрифтом (например, если открытая в
    /// редакторе сцена затирает правки файлов), а часть надписей UI-скрипты
    /// создают в рантайме. Из-за этого текст разъезжается: где-то новый шрифт,
    /// где-то старый. Этот проход при загрузке каждой сцены приводит все
    /// <see cref="TMP_Text"/> к одному шрифту — тому, что назначен шрифтом по
    /// умолчанию в TMP Settings (шрифт из папки Fonts), вместе с его материалом,
    /// так что во всей игре текст выглядит одинаково.
    /// </summary>
    public static class UiFontNormalizer
    {
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
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null) return;

            Material material = font.material;

            TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null) continue;

                if (text.font != font) text.font = font;
                if (material != null && text.fontSharedMaterial != material)
                    text.fontSharedMaterial = material;
            }
        }
    }
}
