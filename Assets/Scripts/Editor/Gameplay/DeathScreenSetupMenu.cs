using CarGame.Combat;
using CarGame.Player;
using CarGame.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Собирает в текущей сцене экран смерти: затемнение и одну мигающую
    /// подсказку «Press R to Restart». Всей логикой (мигание и нажатие R)
    /// заведует единственный компонент <see cref="DeathScreenUI"/>.
    ///
    /// Сцена смерти — отдельная: создайте пустую сцену, назовите её (по
    /// умолчанию DeathScreen), запустите пункт и сохраните. Игру в неё
    /// отправляет DeathSceneLoader в игровой сцене — его добавляет второй
    /// пункт этого меню.
    /// </summary>
    public static class DeathScreenSetupMenu
    {
        [MenuItem("Tools/CarGame/Menus/Create Death Screen")]
        public static void CreateDeathScreen()
        {
            GameObject root = Build();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            Debug.Log("[Menus] Экран смерти создан. Сохраните сцену (Ctrl+S).");
        }

        /// <summary>
        /// Собирает экран смерти в текущей сцене и возвращает корневой объект.
        /// Вынесено отдельно, чтобы сцену смерти можно было создать целиком
        /// через SceneFlowSetupMenu.
        /// </summary>
        public static GameObject Build()
        {
            MenuUiBuilder.EnsureEventSystem();
            MenuUiBuilder.EnsureCamera();
            MenuUiBuilder.EnsureDirectionalLight();

            Canvas canvas = MenuUiBuilder.GetOrCreateCanvas("DeathCanvas", 300);
            DeathScreenUI screen = MenuUiBuilder.GetOrAddComponent<DeathScreenUI>(canvas.gameObject);
            Undo.RecordObject(screen, "Обновить экран смерти");

            MenuUiBuilder.ClearChildren(canvas.transform);

            MenuUiBuilder.CreateFullscreen(canvas.transform, "Backdrop", MenuUiBuilder.BackdropColor, true);

            // Одна мигающая подсказка: и текст, и клавиша перезапуска живут в
            // самом DeathScreenUI, кнопки больше не нужны.
            TextMeshProUGUI hint = MenuUiBuilder.CreateLabel(canvas.transform, "RestartText", "Press R to Restart",
                Vector2.zero, new Vector2(1400f, 180f), 72f, Color.white, TextAlignmentOptions.Center);

            SerializedObject so = new SerializedObject(screen);
            MenuUiBuilder.SetString(so, "gameSceneName", "SampleScene");
            MenuUiBuilder.SetReference(so, "restartText", hint);
            so.ApplyModifiedProperties();

            return canvas.gameObject;
        }

        /// <summary>
        /// Вешает на игрока DeathSceneLoader: он ловит гибель и грузит сцену
        /// смерти. Запускать при открытой ИГРОВОЙ сцене, выделив объект игрока
        /// (или его родителя с Health).
        /// </summary>
        [MenuItem("Tools/CarGame/Menus/Add Death Scene Loader")]
        public static void AddDeathSceneLoader()
        {
            GameObject target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("[Menus] Выделите объект игрока, чтобы добавить DeathSceneLoader.");
                return;
            }

            if (target.GetComponentInParent<Health>() == null)
            {
                Debug.LogWarning("[Menus] У выделенного объекта и его родителей нет Health — это не игрок?");
                return;
            }

            if (target.GetComponentInParent<DeathSceneLoader>() != null)
            {
                Debug.Log("[Menus] DeathSceneLoader уже есть.");
                Selection.activeGameObject = target.GetComponentInParent<DeathSceneLoader>().gameObject;
                return;
            }

            DeathSceneLoader loader = Undo.AddComponent<DeathSceneLoader>(target);
            SerializedObject so = new SerializedObject(loader);
            MenuUiBuilder.SetString(so, "deathSceneName", "DeathScreen");
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(target.scene);
            Selection.activeGameObject = target;

            Debug.Log("[Menus] DeathSceneLoader добавлен. Сцена смерти по умолчанию — DeathScreen.");
        }
    }
}
