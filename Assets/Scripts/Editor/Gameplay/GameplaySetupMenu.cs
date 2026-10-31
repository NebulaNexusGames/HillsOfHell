using CarGame.Combat;
using CarGame.Items;
using CarGame.Player;
using CarGame.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CarGame.EditorTools
{
    public static class GameplaySetupMenu
    {
        private const string MaterialPath = "Assets/Materials/Gameplay_Default.mat";
        private const string WrenchPrefabPath = "Assets/Prefabs/Wrench.prefab";
        private const string RobotPrefabPath = "Assets/Prefabs/RobotSwordsman.prefab";

        [MenuItem("Инструменты/CarGame/Настроить сцену (ключ, робот, HUD)")]
        public static void SetupScene()
        {
            Undo.SetCurrentGroupName("CarGame Setup");
            int group = Undo.GetCurrentGroup();

            GameObject wrench = CreateWrench();
            GameObject robot = CreateRobot();
            GameObject hud = EnsureHud();

            Undo.CollapseUndoOperations(group);

            Debug.Log($"[Setup] Готово. Ключ: {wrench.name}, робот: {robot.name}, HUD: {hud.name}", null);
        }

        [MenuItem("Инструменты/CarGame/Создать префабы (ключ, робот)")]
        public static void CreatePrefabs()
        {
            GameObject wrench = BuildWrench();
            GameObject robot = BuildRobot();

            GameObject wrenchPrefab = PrefabUtility.SaveAsPrefabAsset(wrench, WrenchPrefabPath);
            GameObject robotPrefab = PrefabUtility.SaveAsPrefabAsset(robot, RobotPrefabPath);

            Object.DestroyImmediate(wrench);
            Object.DestroyImmediate(robot);
            AssetDatabase.Refresh();

            Debug.Log($"[Setup] Префабы созданы: {wrenchPrefab.name}, {robotPrefab.name}", null);
        }

        [MenuItem("Инструменты/CarGame/Создать ключ рядом с игроком")]
        public static void CreateWrenchNearPlayer()
        {
            Vector3 position = FindSpawnPosition() + new Vector3(-1.2f, 0.1f, 0.8f);
            CreateWrench(position);
        }

        [MenuItem("Инструменты/CarGame/Создать стволы и патроны рядом с игроком")]
        public static void CreateFirearmsNearPlayer()
        {
            Vector3 origin = FindSpawnPosition();

            GameObject pistol = CreateInventoryPickup("Pistol", ItemDefs.PistolId, new Vector3(0.75f, 0.08f, 1.1f), new Vector3(0.07f, 0.16f, 0.05f), new Color(0.24f, 0.25f, 0.28f));
            GameObject shotgun = CreateInventoryPickup("Shotgun", ItemDefs.ShotgunId, new Vector3(1.15f, 0.08f, 0.75f), new Vector3(0.07f, 0.16f, 0.62f), new Color(0.34f, 0.26f, 0.18f));
            GameObject pistolAmmo = CreateInventoryPickup("PistolAmmo", ItemDefs.PistolAmmoId, new Vector3(0.45f, 0.06f, 1.65f), new Vector3(0.16f, 0.08f, 0.11f), new Color(0.78f, 0.66f, 0.28f), 24);
            GameObject shotgunAmmo = CreateInventoryPickup("ShotgunAmmo", ItemDefs.ShotgunAmmoId, new Vector3(0.95f, 0.06f, 1.75f), new Vector3(0.14f, 0.08f, 0.16f), new Color(0.76f, 0.24f, 0.2f), 8);

            Debug.Log($"[Setup] Создано: {pistol.name}, {shotgun.name}, {pistolAmmo.name} x24, {shotgunAmmo.name} x8", null);
        }

        /// <summary>
        /// Предмет, который при подборе сразу уходит в инвентарь, а не в руку.
        /// Поэтому вариант руки ему не нужен: стволы и патроны цепляются цифрами,
        /// а не показываются в руке.
        /// </summary>
        private static GameObject CreateInventoryPickup(string name, string itemId, Vector3 offset, Vector3 scale, Color color, int stackAmount = 1)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = FindSpawnPosition() + offset;
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = SetupTintedMaterial(name, color);

            PickupItem pickup = go.AddComponent<PickupItem>();
            pickup.SetItemId(itemId);
            pickup.SetStackAmount(stackAmount);

            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        /// <summary>
        /// Цветной материал как ассет: иначе new Material потерялся бы при
        /// перезагрузке сцены и предмет остался бы серым.
        /// </summary>
        private static Material SetupTintedMaterial(string name, Color color)
        {
            string path = $"Assets/Materials/Gameplay_{name}.mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Material baseMaterial = SetupMaterial();
            if (baseMaterial == null) return null;

            Material created = new Material(baseMaterial) { name = "Gameplay_" + name, color = color };
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static GameObject CreateWrench()
        {
            return CreateWrench(FindSpawnPosition() + new Vector3(-1.2f, 0.1f, 0.8f));
        }

        private static GameObject CreateWrench(Vector3 position)
        {
            GameObject wrench = BuildWrench();
            wrench.transform.position = position;
            Undo.RegisterCreatedObjectUndo(wrench, "Create Wrench");
            Selection.activeGameObject = wrench;
            return wrench;
        }

        private static GameObject CreateRobot()
        {
            GameObject robot = BuildRobot();
            robot.transform.position = FindSpawnPosition() + new Vector3(4f, 0.1f, 6f);
            Undo.RegisterCreatedObjectUndo(robot, "Create Robot");
            Selection.activeGameObject = robot;
            return robot;
        }

        private static GameObject BuildWrench()
        {
            GameObject wrench = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wrench.name = "Wrench";
            wrench.transform.localScale = new Vector3(0.08f, 0.08f, 0.55f);
            wrench.transform.position = new Vector3(0f, 0.05f, 0f);

            Renderer renderer = wrench.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = SetupMaterial();

            PickupItem pickup = wrench.AddComponent<PickupItem>();
            pickup.SetItemId("wrench");
            wrench.AddComponent<WrenchItem>();
            return wrench;
        }

        private static GameObject BuildRobot()
        {
            GameObject robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            robot.name = "RobotSwordsman";
            robot.transform.position = new Vector3(0f, 0.9f, 0f);

            Renderer renderer = robot.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = SetupMaterial();

            robot.AddComponent<Rigidbody>();
            robot.AddComponent<RobotSwordsman>();
            return robot;
        }

        private static GameObject EnsureHud()
        {
            DamageHud existing = Object.FindFirstObjectByType<DamageHud>();
            if (existing != null)
            {
                Debug.Log("[Setup] DamageHud уже есть в сцене", existing);
                return existing.gameObject;
            }

            GameObject hud = new GameObject("DamageHud", typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(hud, "Create HUD");
            hud.AddComponent<DamageHud>();
            Selection.activeGameObject = hud;
            return hud;
        }

        private static Material SetupMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            Material created = new Material(shader) { name = "Gameplay_Default" };
            AssetDatabase.CreateAsset(created, MaterialPath);
            return created;
        }

        private static Vector3 FindSpawnPosition()
        {
            PlayerController[] players = Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            return players.Length > 0 ? players[0].transform.position : new Vector3(0f, 0f, 0f);
        }
    }
}
