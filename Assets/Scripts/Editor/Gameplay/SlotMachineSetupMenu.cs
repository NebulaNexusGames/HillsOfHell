using CarGame.Items;
using CarGame.Player;
using CarGame.UI;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Настройка автомата из редактора. Раскладка объекта в сцене:
    /// SlotMachineTerminal (пустой корень с компонентом) → Model (вид),
    /// DepositSlot (деталь приёма ставки), Lever (рычаг запуска), ScreenPoint
    /// (точка панели), ActiveTrigger (объём появления панели), SpawnPoint
    /// (точка выигрыша — прямой ребёнок корня, как SpawnPanel у терминала).
    ///
    /// Корень автомата в сцене уже стоит с масштабом 0.403, и у него восемь
    /// случайных кубов-«мусора» в детях — их трогать нельзя. Поэтому всё
    /// строится в локали корня: размеры делятся на масштаб, и локальные кубы
    /// получаются в размере настоящей детали автомата. Меню идемпотентно:
    /// повторный запуск чинит потерянные ссылки, а не плодит копии.
    /// </summary>
    public static class SlotMachineSetupMenu
    {
        private const string SlotName = "SlotMachineTerminal";
        private const string ModelName = "Model";
        private const string ScreenPointName = "ScreenPoint";
        private const string ActiveTriggerName = "ActiveTrigger";
        private const string DepositSlotName = "DepositSlot";
        private const string LeverName = "Lever";
        private const string SpawnPointName = "SpawnPoint";

        private const string DefaultMaterialPath = "Assets/Materials/Metall.mat";
        private const string GoldMaterialPath = "Assets/Materials/Gold.mat";
        private const string ExplosionPrefabPath = "Assets/Prefabs/Effects/Explosion.prefab";

        /// <summary>Главный пункт: создаёт автомат, приём ставки и рычаг.</summary>
        [MenuItem("Tools/CarGame/Slot/Setup Slot Machine")]
        public static void SetupSlotMachine()
        {
            SlotMachineTerminal slot = FindOrCreateSlot();
            if (slot == null) return;

            EnsureHierarchy(slot);
            CreateEditableSlotMachineHud(slot);
            

            Selection.activeGameObject = slot.gameObject;
            Debug.Log("[Slot] Slot machine ready: deposit, lever, HUD wired.");
        }

        /// <summary>Чинит иерархию автомата, если запустить Setup ещё раз.</summary>
        [MenuItem("Tools/CarGame/Slot/Repair Slot Machine")]
        public static void RepairSlotMachine()
        {
            SlotMachineTerminal slot = FindSlotComponent();
            if (slot == null)
            {
                Debug.LogWarning("[Slot] No SlotMachineTerminal found. Run «Setup Slot Machine» first.");
                return;
            }

            EnsureHierarchy(slot);
            Debug.Log("[Slot] Hierarchy is in place.");
        }

        private static void EnsureHierarchy(SlotMachineTerminal slot)
        {
            GameObject root = slot.gameObject;
            float scale = Mathf.Max(0.01f, Mathf.Abs(root.transform.lossyScale.x));

            GameObject model = FindChild(root, ModelName);
            if (model == null)
            {
                model = new GameObject(ModelName);
                model.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(model, "Создать Model автомата");
                MakeBodyCube(model);
            }

            // Корпус автомата: примерно 0.9 x 1.5 x 0.7 м в мире.
            model.transform.localPosition = new Vector3(0f, 1.86f, 0f);
            model.transform.localScale = new Vector3(0.9f / scale, 1.5f / scale, 0.7f / scale);
            slot.SetModel(model);

            // Точка выдачи ищется у корня автомата, как SpawnPoint у SpawnPanel в
            // терминале магазина: меню не должно хватать чужой SpawnPoint в детях
            // модели. Позицию существующей точки не трогаем — её расставили в сцене,
            // предмет вылетает из неё дугой вперёд-вверх (SpawnEntry в терминале).
            // Новая точка создаётся у корня в классическом месте дропа.
            GameObject spawnPoint = FindChild(root, SpawnPointName);
            if (spawnPoint == null)
            {
                spawnPoint = new GameObject(SpawnPointName);
                spawnPoint.transform.SetParent(root.transform, false);
                spawnPoint.transform.localPosition = new Vector3(0f, 1.2f / scale, 1.2f / scale);
                Undo.RegisterCreatedObjectUndo(spawnPoint, "Создать SpawnPoint");
            }

            slot.SetSpawnPoint(spawnPoint.transform);

            GameObject screenPoint = FindChild(root, ScreenPointName);
            if (screenPoint == null)
            {
                screenPoint = new GameObject(ScreenPointName);
                screenPoint.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(screenPoint, "Создать ScreenPoint");
            }

            screenPoint.transform.localPosition = new Vector3(0f, 2.6f / scale, 1.6f / scale);
            slot.SetScreenPoint(screenPoint.transform);

            slot.SetActiveTrigger(EnsureActiveTrigger(root, scale));
            slot.SetDepositSlot(EnsureDepositSlot(root, slot, scale));
            slot.SetLeverSlot(EnsureLever(root, slot, scale));

            slot.SetExplosionPrefab(LoadExplosionPrefab());

            EditorUtility.SetDirty(slot);
        }

        /// <summary>
        /// Деталь приёма ставки: золотой поддон перед автоматом с
        /// SlotMachineDeposit и объёмом-триггером. Прицел на него + любой
        /// предмет в руке кладёт единицу, сюда же физически вкатывается
        /// брошенный предмет — целиком.
        /// </summary>
        private static GameObject EnsureDepositSlot(GameObject root, SlotMachineTerminal slot, float scale)
        {
            GameObject go = FindChild(root, DepositSlotName);
            if (go == null)
            {
                go = new GameObject(DepositSlotName);
                go.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Создать DepositSlot");
                MakeBodyCube(go);
                EditorUtility.SetDirty(go);
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = EnsureGoldMaterial();

            go.transform.localPosition = new Vector3(0f, 0.5f / scale, 2.2f / scale);
            go.transform.localScale = new Vector3(0.7f / scale, 0.12f / scale, 0.6f / scale);

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);

            // Триггер заметно больше тонкой «полочки»: цель E должна ловиться
            // прицелом, а не требовать попадания в миллиметр. Мировой размер
            // указываем явно — коллайдер пересчитывается под масштаб корня.
            box.isTrigger = true;
            ResizeTrigger(box, go, new Vector3(0.9f, 0.55f, 0.9f));
            box.center = new Vector3(0f, -0.1f, -0.3f);

            SlotMachineDeposit deposit = go.GetComponent<SlotMachineDeposit>();
            if (deposit == null) deposit = Undo.AddComponent<SlotMachineDeposit>(go);

            slot.SetDepositSlot(go);
            return go;
        }

        /// <summary>
        /// Наводит коллайдер-триггер на нужный мировой размер: считается в
        /// локальных единицах по lossyScale, так что тонкая деталь не съедает
        /// зону прицела. Нужно для надёжного E-прицела на рычаг и полку.
        /// </summary>
        private static void ResizeTrigger(BoxCollider box, GameObject go, Vector3 worldSize)
        {
            Vector3 lossy = go.transform.lossyScale;
            box.size = new Vector3(
                worldSize.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
                worldSize.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
                worldSize.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
        }

        /// <summary>
        /// Рычаг: золотая рукоять справа на корпусе с SlotMachineLever и
        /// коллайдером-триггером. Прицел на него + E запускает вращение.
        /// </summary>
        private static GameObject EnsureLever(GameObject root, SlotMachineTerminal slot, float scale)
        {
            GameObject go = FindChild(root, LeverName);
            if (go == null)
            {
                go = new GameObject(LeverName);
                go.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Создать Lever");
                MakeBodyCube(go);
                EditorUtility.SetDirty(go);
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = EnsureGoldMaterial();

            go.transform.localPosition = new Vector3(1.0f / scale, 1.2f / scale, 0.1f / scale);
            go.transform.localScale = new Vector3(0.12f / scale, 0.6f / scale, 0.12f / scale);

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);

            // Триггер, а не твёрдое тело: прицельный рейкаст попадает в триггер
            // (как у детали депозита), а игрока рычаг не расталкивает. Зона
            // прицела вокруг тонкой рукояти широкая — иначе мелкую цель E не
            // поймать центральным лучом.
            box.isTrigger = true;
            ResizeTrigger(box, go, new Vector3(0.6f, 0.9f, 0.6f));
            box.center = Vector3.zero;

            SlotMachineLever lever = go.GetComponent<SlotMachineLever>();
            if (lever == null) lever = Undo.AddComponent<SlotMachineLever>(go);

            slot.SetLeverSlot(go);
            return go;
        }

        /// <summary>
        /// Триггер активации панели автомата: невысокий объём по уровню груди
        /// у самого автомата. Накрывает обе рабочие позиции — приём ставки
        /// (игрок стоит перед поддоном) и рычаг (игрок стоит справа) — но не
        /// вылезает вперёд большим кубом и не накрывает поддон/рычаг как цели:
        /// пятно прицела на поддон и рычаг всегда свободно, панель только
        /// показывается, когда игрок уже у дела.
        /// </summary>
        private static TerminalActivationTrigger EnsureActiveTrigger(GameObject root, float scale)
        {
            GameObject go = FindChild(root, ActiveTriggerName);
            if (go == null)
            {
                go = new GameObject(ActiveTriggerName);
                go.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Создать ActiveTrigger");
            }

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);

            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = new Vector3(2.1f / scale, 1.1f / scale, 2.0f / scale);
            go.transform.localPosition = new Vector3(0.4f / scale, 1.3f / scale, 1.5f / scale);

            TerminalActivationTrigger trigger = go.GetComponent<TerminalActivationTrigger>();
            if (trigger == null) trigger = Undo.AddComponent<TerminalActivationTrigger>(go);

            if (root != null)
            {
                SlotMachineTerminal slot = root.GetComponent<SlotMachineTerminal>();
                if (slot != null) trigger.SetSlotMachine(slot);
            }

            return trigger;
        }

        /// <summary>Редактируемый HUD в сцене, как терминальный и магазинный.</summary>
        private static void CreateEditableSlotMachineHud(SlotMachineTerminal slot)
        {
            if (slot == null) return;

            SlotMachineUI existing = Object.FindFirstObjectByType<SlotMachineUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.SetDirty(existing);
                Debug.Log("[Slot] Editable HUD already exists: " + existing.name);
                return;
            }

            GameObject go = new GameObject("SlotHUD");
            go.transform.SetParent(slot.transform.parent, false);
            SlotMachineUI hud = go.AddComponent<SlotMachineUI>();
            hud.Attach(slot);

            Undo.RegisterCreatedObjectUndo(go, "Create Slot HUD");
            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;
            Debug.Log("[Slot] Editable HUD created and wired to the slot machine.");
        }

        private static GameObject LoadExplosionPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[Slot] No " + ExplosionPrefabPath + " — аннигиляция пойдёт процедурным Explosion.Detonate.");
            }
            return prefab;
        }

        private static Material EnsureGoldMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(GoldMaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material material = new Material(shader);
            material.color = new Color(0.98f, 0.83f, 0.32f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.9f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.6f);

            EnsureFolder(PathForFolder(GoldMaterialPath));
            AssetDatabase.CreateAsset(material, GoldMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void MakeBodyCube(GameObject target)
        {
            MeshFilter filter = target.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        }

        private static SlotMachineTerminal FindOrCreateSlot()
        {
            SlotMachineTerminal slot = FindSlotComponent();
            if (slot != null) return slot;

            GameObject existing = FindSceneObject(SlotName);
            if (existing != null)
            {
                slot = Undo.AddComponent<SlotMachineTerminal>(existing);
                Debug.Log($"[Slot] Added SlotMachineTerminal to «{SlotName}».");
                return slot;
            }

            Transform anchor = FindSpawnAnchor(out Vector3 position);
            if (anchor == null) return null;

            GameObject go = new GameObject(SlotName);
            Undo.RegisterCreatedObjectUndo(go, "Создать автомат");
            go.transform.SetPositionAndRotation(SnapToGround(position), Quaternion.Euler(0f, anchor.eulerAngles.y, 0f));

            return Undo.AddComponent<SlotMachineTerminal>(go);
        }

        private static SlotMachineTerminal FindSlotComponent()
        {
            return Object.FindFirstObjectByType<SlotMachineTerminal>();
        }

        private static GameObject FindChild(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            return child != null ? child.gameObject : null;
        }

        private static Vector3 SnapToGround(Vector3 position)
        {
            if (!Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 60f)) return position;
            return hit.point;
        }

        private static Transform FindSpawnAnchor(out Vector3 slotPosition)
        {
            slotPosition = Vector3.zero;

            // Если в сцене уже есть магазин — автомат ставим рядом с ним.
            ShopTerminal shop = Object.FindFirstObjectByType<ShopTerminal>();
            if (shop != null)
            {
                slotPosition = shop.transform.position + shop.transform.rotation * new Vector3(-5f, 0f, 3f);
                return shop.transform;
            }

            SpawnTerminal terminal = Object.FindFirstObjectByType<SpawnTerminal>();
            if (terminal != null)
            {
                slotPosition = terminal.transform.position + terminal.transform.rotation * new Vector3(0f, 0f, 8f);
                return terminal.transform;
            }

            Transform player = null;
            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                PlayerController found = roots[i].GetComponentInChildren<PlayerController>(true);
                if (found == null) continue;

                player = found.transform;
                break;
            }

            if (player == null)
            {
                Debug.LogError("[Slot] No PlayerController, Terminal or Shop in the scene — cannot pick a spot for the slot machine.");
                return null;
            }

            slotPosition = player.position + player.rotation * new Vector3(10f, 0f, 4f);
            return player;
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

        /// <summary>Только имя папки из полного пути ассета («Assets/Materials/Gold.mat» → «Assets/Materials»).</summary>
        private static string PathForFolder(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : "Assets";
        }

        /// <summary>Создаёт папку ассета, если её ещё нет.</summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = PathForFolder(folder);
            string leaf = folder.Substring(parent.Length + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}