using CarGame.Effects;
using CarGame.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Инструмент всего пост-процессинга сцены: создаёт в текущей сцене один
    /// объект <see cref="PostFXManager"/>, который держит все экранные эффекты
    /// (виньетка урона/лечения, пульс низкого здоровья, произвольные вспышки)
    /// в одном месте. Повторный запуск просто выделяет уже созданный менеджер.
    ///
    /// Заодно убирает старые одиночные <see cref="DamageVignette"/> — их работу
    /// теперь выполняет менеджер, а две виньетки одновременно закрыли бы экран.
    /// </summary>
    public static class PostFXSetupMenu
    {
        [MenuItem("Tools/CarGame/Post Processing/Create Post FX Manager")]
        public static void CreatePostFXManager()
        {
            PostFXManager existing = Object.FindFirstObjectByType<PostFXManager>();
            if (existing != null)
            {
                EnsureDayNightCycle(existing.gameObject);
                Select(existing.gameObject);
                Debug.Log("[PostFX] Менеджер пост-процессинга уже есть: " + existing.name + ". Цикл дня и ночи проверен.");
                return;
            }

            GameObject root = new GameObject("PostFXManager");
            Undo.RegisterCreatedObjectUndo(root, "Создать PostFXManager");
            PostFXManager manager = Undo.AddComponent<PostFXManager>(root);
            EnsureDayNightCycle(root);

            MigrateOldVignettes();
            DamageVignetteSetupEditor.Setup();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Select(root);

            Debug.Log("[PostFX] Создан единый менеджер пост-процессинга PostFXManager. Рендереры URP настроены на виньетку добраться честной постобработкой.");
        }

        [MenuItem("Tools/CarGame/Lighting/Create Day Night Cycle")]
        public static void CreateDayNightCycle()
        {
            DayNightCycle existing = Object.FindFirstObjectByType<DayNightCycle>();
            if (existing != null)
            {
                Select(existing.gameObject);
                Debug.Log("[Lighting] DayNightCycle уже есть в сцене.");
                return;
            }

            GameObject root = new GameObject("DayNightCycle");
            Undo.RegisterCreatedObjectUndo(root, "Создать DayNightCycle");
            Undo.AddComponent<DayNightCycle>(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Select(root);
            Debug.Log("[Lighting] Создан DayNightCycle. Меняет солнце, небо, туман и дальность прорисовки.");
        }

        /// <summary>
        /// Добавляет на объект цикл дня и ночи, если его ещё нет. Смену солнца,
        /// неба, тумана и дальности прорисовки ведёт именно он.
        /// </summary>
        private static void EnsureDayNightCycle(GameObject target)
        {
            if (target == null) return;
            if (target.GetComponent<DayNightCycle>() != null) return;

            Undo.AddComponent<DayNightCycle>(target);
            EditorSceneManager.MarkSceneDirty(target.scene);
        }

        /// <summary>
        /// Старые DamageVignette на игроке теперь лишние: у них был свой оверлей и
        /// свой канвас, а с менеджером виньетка рисовалась бы дважды. Удаляем только
        /// компоненты, объекты игрока не трогаем.
        /// </summary>
        private static void MigrateOldVignettes()
        {
            DamageVignette[] old = Object.FindObjectsByType<DamageVignette>(FindObjectsSortMode.None);
            if (old == null || old.Length == 0) return;

            for (int i = 0; i < old.Length; i++)
            {
                if (old[i] == null) continue;
                Undo.DestroyObjectImmediate(old[i]);
            }

            Debug.Log("[PostFX] Удалено старых компонентов DamageVignette: " + old.Length);
        }

        private static void Select(GameObject target)
        {
            if (target == null) return;
            Selection.activeGameObject = target;
            SceneView.FrameLastActiveSceneView();
        }
    }
}