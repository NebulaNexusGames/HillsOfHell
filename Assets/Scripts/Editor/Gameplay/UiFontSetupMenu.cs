using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Приводит ВЕСЬ интерфейс игры к единому виду: один шрифт TMP из папки Fonts
    /// и одна рамка 9-слайса на кнопках.
    ///
    /// Проходит по всем сценам проекта (меню, смерть, игровая) и всем префабам,
    /// ставит шрифт каждому <see cref="TMP_Text"/> и материал рамки каждой кнопке
    /// и переключателю, сохраняет результат, а также назначает шрифт по умолчанию
    /// в TMP Settings — чтобы и любой текст, созданный в рантайме, появлялся тем
    /// же шрифтом. Фоны окон рамку намеренно не получают: она на них бесполезна.
    ///
    /// Инструменты идемпотентны: повторный запуск ничего не портит и добивает
    /// объекты, которые появились позже. Это страховка от того, что открытая
    /// сцена в редакторе может затереть правки файлов на диске.
    /// </summary>
    public static class UiFontSetupMenu
    {
        private const string FontAssetPath = "Assets/Fonts/Classic-Console-Neue-Regular SDF.asset";
        private const string FrameMaterialPath = "Assets/Resources/UI/NineSliceWindow.mat";
        private const string ButtonMaterialPath = "Assets/Resources/UI/NineSliceButton.mat";

        [MenuItem("Tools/CarGame/UI/Apply Font To All UI")]
        public static void ApplyFontToAllUi()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                Debug.LogError("[UI] Не найден шрифт: " + FontAssetPath);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            SetDefaultFont(font);

            int changed = 0;
            int scenesTouched = WalkAllUi(root =>
            {
                int count = ApplyFontInChildren(root, font);
                changed += count;
                return count > 0;
            });

            Debug.Log("[UI] Шрифт \"" + font.name + "\" применён ко всему UI. " +
                      "Сцен изменено: " + scenesTouched + ", текстов обновлено: " + changed + ".");
        }

        [MenuItem("Tools/CarGame/UI/Apply Frame Skin To All UI")]
        public static void ApplyFrameToAllUi()
        {
            Material window = AssetDatabase.LoadAssetAtPath<Material>(FrameMaterialPath);
            Material button = AssetDatabase.LoadAssetAtPath<Material>(ButtonMaterialPath);
            if (window == null || button == null)
            {
                Debug.LogError("[UI] Не найден материал рамки: " + FrameMaterialPath);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            int changed = 0;
            int scenesTouched = WalkAllUi(root =>
            {
                int count = ApplyFrameInChildren(root, window, button);
                changed += count;
                return count > 0;
            });

            Debug.Log("[UI] Рамка применена к кнопкам и окнам UI. " +
                      "Сцен изменено: " + scenesTouched + ", правок: " + changed + ".");
        }

        private static void SetDefaultFont(TMP_FontAsset font)
        {
            TMP_Settings settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings == null)
            {
                Debug.LogWarning("[UI] Не найден ассет TMP Settings — шрифт по умолчанию не задан.");
                return;
            }

            SerializedObject serialized = new SerializedObject(settings);
            SerializedProperty property = serialized.FindProperty("m_defaultFontAsset");
            if (property == null) return;

            property.objectReferenceValue = font;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// Прогоняет посетителя по каждому корню каждой сцены и каждого префаба.
        /// Возвращает число изменённых сцен. Посетитель возвращает true, если
        /// что-то поменял.
        /// </summary>
        private static int WalkAllUi(Func<GameObject, bool> visit)
        {
            int scenesTouched = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".unity")) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;

                foreach (GameObject root in scene.GetRootGameObjects())
                    if (visit(root)) changed = true;

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    scenesTouched++;
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);

                if (visit(root)) PrefabUtility.SaveAsPrefabAsset(root, path);

                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return scenesTouched;
        }

        private static int ApplyFontInChildren(GameObject root, TMP_FontAsset font)
        {
            int changed = 0;

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == font) continue;

                text.font = font;
                EditorUtility.SetDirty(text);
                changed++;
            }

            return changed;
        }

        private static int ApplyFrameInChildren(GameObject root, Material window, Material button)
        {
            int changed = 0;

            // Кнопки и переключатели получают материал кнопки: рамка красится
            // состоянием (наведение — белым, нажатие — красным), заливка
            // включается альфой вершины.
            foreach (Button btn in root.GetComponentsInChildren<Button>(true))
                if (ApplyFrame(btn.targetGraphic, button)) changed++;

            foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true))
                if (ApplyFrame(toggle.targetGraphic, button)) changed++;

            // Карточки окон получают материал окна: рамка берёт цвет текстуры,
            // интерьер — цвет панели. Полноэкранные подложки не трогаем.
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                string name = image.gameObject.name;
                if (name == "Card" || name == "SettingsPanel")
                    if (ApplyFrame(image, window)) changed++;
            }

            return changed;
        }

        private static bool ApplyFrame(Graphic graphic, Material frame)
        {
            if (graphic == null || frame == null) return false;
            if (graphic.material == frame) return false;

            graphic.material = frame;
            EditorUtility.SetDirty(graphic);
            return true;
        }
    }
}
