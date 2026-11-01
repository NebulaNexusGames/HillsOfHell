using CarGame.Items;
using CarGame.Player;
using CarGame.UI;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Ставит в сцену терминал-обменник (переработчик). Корень с
    /// ExchangerTerminal, дочерняя модель-куб, точка выдачи SpawnPoint, откуда
    /// вылетают золотые болты, ScreenPoint под панель и ActiveTrigger, который
    /// показывает панель, когда игрок заходит в объём. Сдача идёт по E, а
    /// подтверждение/отмену буфера делает панель (ExchangerTerminalUI) по ЛКМ.
    /// </summary>
    public static class ExchangerSetupMenu
    {
        private const string RootName = "ExchangerTerminal";
        private const string ModelName = "Model";
        private const string SpawnPointName = "SpawnPoint";
        private const string ScreenPointName = "ScreenPoint";
        private const string ActiveTriggerName = "ActiveTrigger";
        private const string DefaultMaterialPath = "Assets/Materials/Metall.mat";

        
        [MenuItem("Tools/CarGame/Exchanger/Setup Exchanger")]
        public static void SetupExchanger()
        {
            ExchangerTerminal terminal = EnsureTerminal();

            GameObject root = terminal.gameObject;
            GameObject model = FindChild(root, ModelName);
            if (model == null)
            {
                model = new GameObject(ModelName);
                model.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(model, "Создать модель обменника");
                MakeBodyCube(model);
            }

            model.transform.localPosition = new Vector3(0f, 1f, 0f);
            model.transform.localScale = new Vector3(1.2f, 2f, 1.2f);
            terminal.SetModel(model);

            GameObject spawnPoint = FindChild(root, SpawnPointName);
            if (spawnPoint == null)
            {
                spawnPoint = new GameObject(SpawnPointName);
                spawnPoint.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(spawnPoint, "Создать точку выдачи обменника");
            }

            spawnPoint.transform.localPosition = new Vector3(0f, 1.4f, 0.8f);
            terminal.SetSpawnPoint(spawnPoint.transform);

            GameObject screenPoint = FindChild(root, ScreenPointName);
            if (screenPoint == null)
            {
                screenPoint = new GameObject(ScreenPointName);
                screenPoint.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(screenPoint, "Создать ScreenPoint обменника");
            }

            screenPoint.transform.localPosition = new Vector3(0f, 1.9f, 1.8f);
            terminal.SetScreenPoint(screenPoint.transform);

            terminal.SetActiveTrigger(EnsureActiveTrigger(root, screenPoint.transform));

            CreateEditableHud(terminal);

            EditorUtility.SetDirty(terminal);
            Selection.activeGameObject = root;
            Debug.Log("[Exchanger] Терминал-обменник создан: буфер, панель, точка выдачи.");
        }

        /// <summary>
        /// Trigger активации панели: отдельный объём перед терминалом, как у
        /// магазина и автомата.
        /// </summary>
        private static TerminalActivationTrigger EnsureActiveTrigger(GameObject root, Transform screenPoint)
        {
            GameObject go = FindChild(root, ActiveTriggerName);
            if (go == null)
            {
                go = new GameObject(ActiveTriggerName);
                go.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Создать ActiveTrigger обменника");
            }

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);

            box.isTrigger = true;
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(3f, 2f, 3f);

            Vector3 target = screenPoint != null ? screenPoint.position : root.transform.position;
            Vector3 local = root.transform.InverseTransformPoint(target);
            Vector3 offset = local - new Vector3(0f, 1f, 0f);
            go.transform.localPosition = new Vector3(offset.x, offset.y, offset.z + 2f);

            TerminalActivationTrigger trigger = go.GetComponent<TerminalActivationTrigger>();
            if (trigger == null) trigger = Undo.AddComponent<TerminalActivationTrigger>(go);

            trigger.SetExchanger(root.GetComponent<ExchangerTerminal>());
            return trigger;
        }

        /// <summary>Редактируемая панель в сцене, как у магазина.</summary>
        private static void CreateEditableHud(ExchangerTerminal terminal)
        {
            if (terminal == null) return;

            ExchangerTerminalUI existing = Object.FindFirstObjectByType<ExchangerTerminalUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.SetDirty(existing);
                Debug.Log("[Exchanger] Редактируемая панель уже есть: " + existing.name);
                return;
            }

            GameObject go = new GameObject("ExchangerHUD");
            go.transform.SetParent(terminal.transform.parent, false);
            ExchangerTerminalUI hud = go.AddComponent<ExchangerTerminalUI>();
            hud.Attach(terminal);

            Undo.RegisterCreatedObjectUndo(go, "Create Exchanger HUD");
            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;
            Debug.Log("[Exchanger] Панель создана и привязана к терминалу.");
        }

        private static ExchangerTerminal EnsureTerminal()
        {
            ExchangerTerminal existing = Object.FindFirstObjectByType<ExchangerTerminal>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            GameObject existingObject = FindSceneObject(RootName);
            if (existingObject != null)
            {
                return Undo.AddComponent<ExchangerTerminal>(existingObject);
            }

            Vector3 position = PickPosition(out float yaw);
            GameObject go = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(go, "Создать обменник");
            go.transform.SetPositionAndRotation(SnapToGround(position), Quaternion.Euler(0f, yaw, 0f));
            return Undo.AddComponent<ExchangerTerminal>(go);
        }

        /// <summary>Ставит обменник рядом с терминалом спавна, а без него — рядом с игроком.</summary>
        private static Vector3 PickPosition(out float yaw)
        {
            yaw = 0f;

            SpawnTerminal terminal = Object.FindFirstObjectByType<SpawnTerminal>();
            if (terminal != null)
            {
                yaw = terminal.transform.eulerAngles.y;
                return terminal.transform.position + terminal.transform.rotation * new Vector3(-5f, 0f, 3f);
            }

            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                yaw = player.transform.eulerAngles.y;
                return player.transform.position + player.transform.rotation * new Vector3(8f, 0f, 4f);
            }

            Debug.LogWarning("[Exchanger] В сцене нет ни терминала, ни игрока — обменник поставлен в начало координат.");
            return Vector3.zero;
        }

        private static Vector3 SnapToGround(Vector3 position)
        {
            if (!Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 60f)) return position;
            return hit.point;
        }

        private static void MakeBodyCube(GameObject target)
        {
            MeshFilter filter = target.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (material != null) renderer.sharedMaterial = material;
        }

        private static GameObject FindChild(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            return child != null ? child.gameObject : null;
        }

        private static GameObject FindSceneObject(string name)
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name != name) continue;
                return transforms[i].gameObject;
            }

            return null;
        }
    }
}