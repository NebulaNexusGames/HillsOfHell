using CarGame.Items;
using CarGame.Player;
using CarGame.UI;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Настройка магазина из редактора. Раскладка объекта в сцене:
    /// ShopTerminal (пустой корень с компонентом ShopTerminal) → Model (внешний
    /// вид), ShopPanel (панель с коллайдером) → SpawnPoint (точка появления
    /// купленного), ScreenPoint (точка экрана панели), ActiveTrigger (объём, при
    /// входе в который панель появляется) и DepositSlot (деталь депозита Golden
    /// Bolt с BoltDepositor).
    ///
    /// Золотой болт — настоящий ассет: материал Gold.mat и префаб GoldenBolt,
    /// который добавляется в каталог альт-версий и в варианты руки. Иначе болт
    /// не с чего было бы показать в руке и у Депозита.
    ///
    /// Всё идемпотентно: повторный запуск чинит потерянные ссылки, а не плодит
    /// копии.
    /// </summary>
    public static class ShopSetupMenu
    {
        private const string ShopName = "ShopTerminal";
        private const string ModelName = "Model";
        private const string PanelName = "ShopPanel";
        private const string PointName = "SpawnPoint";
        private const string ScreenPointName = "ScreenPoint";
        private const string ActiveTriggerName = "ActiveTrigger";
        private const string DepositSlotName = "DepositSlot";

        private const string DefaultMaterialPath = "Assets/Materials/Metall.mat";
        private const string GoldMaterialPath = "Assets/Materials/Gold.mat";
        private const string BoltPrefabPath = "Assets/Prefabs/Items/GoldenBolt.prefab";
        private const string HandVariantName = "Hand_GoldenBolt";

        /// <summary>Главный пункт: создаёт магазин, деталь депозита и ассеты болта.</summary>
        [MenuItem("Tools/CarGame/Shop/Setup Shop")]
        public static void SetupShop()
        {
            ShopTerminal shop = FindOrCreateShop();
            if (shop == null) return;

            EnsureHierarchy(shop);

            EnsureGoldMaterial();
            EnsureBoltPrefab();
            EnsureCatalogEntry();
            EnsureHandVariant();

            CreateEditableShopHud(shop);

            Selection.activeGameObject = shop.gameObject;
            Debug.Log("[Shop] Shop terminal ready: deposit, bolts, HUD wired.");
        }

        /// <summary>Чинит иерархию магазина, если запустить Setup ещё раз.</summary>
        [MenuItem("Tools/CarGame/Shop/Repair Shop")]
        public static void RepairShop()
        {
            ShopTerminal shop = FindShopComponent();
            if (shop == null)
            {
                Debug.LogWarning("[Shop] No ShopTerminal found. Run «Setup Shop» first.");
                return;
            }

            EnsureHierarchy(shop);
            Debug.Log("[Shop] Hierarchy is in place.");
        }

        private static void EnsureHierarchy(ShopTerminal shop)
        {
            GameObject root = shop.gameObject;

            GameObject model = FindChild(root, ModelName);
            if (model == null)
            {
                model = new GameObject(ModelName);
                model.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(model, "Создать Model магазина");
                MakeBodyCube(model);
            }

            model.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            model.transform.localScale = new Vector3(0.9f, 1.9f, 0.9f);

            GameObject panel = FindChild(root, PanelName);
            if (panel == null)
            {
                panel = new GameObject(PanelName);
                panel.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(panel, "Создать ShopPanel");
                MakeBodyCube(panel);
            }

            // Панель — прилавок перед магазином и он должен быть твёрдым, чтобы
            // через него нельзя было пройти.
            panel.transform.localPosition = new Vector3(0f, 1.1f, 1.1f);
            panel.transform.localScale = new Vector3(1.6f, 1.1f, 0.4f);

            if (panel.GetComponent<Collider>() == null)
            {
                BoxCollider box = panel.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.size = Vector3.one;
            }

            GameObject point = FindChild(panel, PointName);
            if (point == null)
            {
                point = new GameObject(PointName);
                point.transform.SetParent(panel.transform, false);
                Undo.RegisterCreatedObjectUndo(point, "Создать SpawnPoint");
            }

            point.transform.localPosition = new Vector3(0f, 1.5f, 0.1f);

            shop.SetSpawnPoint(point.transform);

            GameObject screenPoint = FindChild(root, ScreenPointName);
            if (screenPoint == null)
            {
                screenPoint = new GameObject(ScreenPointName);
                screenPoint.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(screenPoint, "Создать ScreenPoint");
            }

            screenPoint.transform.localPosition = new Vector3(0f, 1.9f, 1.8f);

            shop.SetScreenPoint(screenPoint.transform);
            shop.SetActiveTrigger(EnsureActiveTrigger(root, screenPoint.transform));

            shop.SetModel(model);
            shop.SetDepositSlot(EnsureDepositSlot(root, shop));

            EditorUtility.SetDirty(shop);
        }

        /// <summary>
        /// Деталь депозита: золотой ящичек перед прилавком с BoltDepositor и
        /// объёмом-триггером. Прицел на него + ЛКМ кладёт болт, сюда же
        /// физически закатывается брошенный болт.
        /// </summary>
        private static GameObject EnsureDepositSlot(GameObject root, ShopTerminal shop)
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

            go.transform.localPosition = new Vector3(0f, 0.75f, 2.2f);
            go.transform.localScale = new Vector3(0.5f, 0.4f, 0.6f);

            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);

            box.isTrigger = true;
            box.size = Vector3.one;
            box.center = new Vector3(0f, -0.1f, -0.3f);

            BoltDepositor depositor = go.GetComponent<BoltDepositor>();
            if (depositor == null) depositor = Undo.AddComponent<BoltDepositor>(go);

            shop.SetDepositSlot(go);
            return go;
        }

        /// <summary>
        /// Триггер активации панели магазина: отдельный объём перед прилавком,
        /// ровно как у терминала спавна.
        /// </summary>
        private static TerminalActivationTrigger EnsureActiveTrigger(GameObject root, Transform screenPoint)
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
            box.center = new Vector3(0f, 1f, 0f);
            box.size = new Vector3(3f, 2f, 3f);

            Vector3 target = screenPoint != null
                ? screenPoint.position
                : go.transform.position + Vector3.forward * 2f;

            Vector3 local = root.transform.InverseTransformPoint(target);
            Vector3 offset = local - new Vector3(0f, 1f, 0f);
            go.transform.localPosition = new Vector3(offset.x, offset.y, offset.z + 2f);

            TerminalActivationTrigger trigger = go.GetComponent<TerminalActivationTrigger>();
            if (trigger == null) trigger = Undo.AddComponent<TerminalActivationTrigger>(go);

            if (root != null)
            {
                ShopTerminal shop = root.GetComponent<ShopTerminal>();
                trigger.SetShopTerminal(shop);
            }

            return trigger;
        }

        /// <summary>Редактируемый HUD в сцене, как терминальный.</summary>
        private static void CreateEditableShopHud(ShopTerminal shop)
        {
            if (shop == null) return;

            ShopTerminalUI existing = Object.FindFirstObjectByType<ShopTerminalUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.SetDirty(existing);
                Debug.Log("[Shop] Editable HUD already exists: " + existing.name);
                return;
            }

            GameObject go = new GameObject("ShopHUD");
            go.transform.SetParent(shop.transform.parent, false);
            ShopTerminalUI hud = go.AddComponent<ShopTerminalUI>();
            hud.Attach(shop);

            Undo.RegisterCreatedObjectUndo(go, "Create Shop HUD");
            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;
            Debug.Log("[Shop] Editable HUD created and wired to the shop.");
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

        private static void EnsureBoltPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath);
            if (existing != null) return;

            GameObject temp = new GameObject("GoldenBolt");

            MeshFilter filter = temp.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cylinder.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = temp.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureGoldMaterial();

            // Длинный золотой прут: диаметр ~10 см, длина ~60 см — не потеряется,
            // когда лежит рядом с магазином или в руке.
            temp.transform.localScale = new Vector3(0.05f, 0.3f, 0.05f);

            CapsuleCollider capsule = temp.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = 0.05f;
            capsule.height = 0.6f;

            Rigidbody body = temp.AddComponent<Rigidbody>();
            body.mass = 0.35f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            PickupItem pickup = temp.AddComponent<PickupItem>();
            pickup.SetItemId(ItemDefs.GoldenBoltId);
            pickup.SetStackAmount(1);

            EnsureFolder("Assets/Prefabs/Items");
            PrefabUtility.SaveAsPrefabAsset(temp, BoltPrefabPath);
            Object.DestroyImmediate(temp);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Shop] GoldenBolt prefab created: " + BoltPrefabPath);
        }

        /// <summary>Добавляет болт в каталог альт-версий, чтобы его можно было спавнить.</summary>
        private static void EnsureCatalogEntry()
        {
            ItemWorldCatalog catalog = Object.FindFirstObjectByType<ItemWorldCatalog>();
            if (catalog == null)
            {
                Debug.LogWarning("[Shop] No ItemWorldCatalog in the scene — bolt prefab not wired.");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath);
            if (prefab == null) return;

            SerializedObject so = new SerializedObject(catalog);
            SerializedProperty entries = so.FindProperty("entries");
            if (entries == null) return;

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty itemId = entries.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (itemId == null || itemId.stringValue != ItemDefs.GoldenBoltId) continue;

                entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(catalog);
                return;
            }

            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = ItemDefs.GoldenBoltId;
            added.FindPropertyRelative("prefab").objectReferenceValue = prefab;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            Debug.Log("[Shop] GoldenBolt added to ItemWorldCatalog.");
        }

        /// <summary>Добавляет болту вариант руки, чтобы его можно было взять в руку.</summary>
        private static void EnsureHandVariant()
        {
            PlayerHand hand = Object.FindFirstObjectByType<PlayerHand>();
            if (hand == null)
            {
                Debug.LogWarning("[Shop] No PlayerHand in the scene — bolt hand variant not wired.");
                return;
            }

            SerializedObject so = new SerializedObject(hand);
            SerializedProperty variants = so.FindProperty("handVariants");
            if (variants == null) return;

            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty itemId = variants.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (itemId == null || itemId.stringValue != ItemDefs.GoldenBoltId) continue;

                EditorUtility.SetDirty(hand);
                return;
            }

            GameObject variant = new GameObject(HandVariantName);
            variant.transform.SetParent(hand.transform, false);

            variants.InsertArrayElementAtIndex(variants.arraySize);
            SerializedProperty added = variants.GetArrayElementAtIndex(variants.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = ItemDefs.GoldenBoltId;
            added.FindPropertyRelative("variant").objectReferenceValue = variant;

            so.ApplyModifiedProperties();
            Undo.RegisterCreatedObjectUndo(variant, "Create GoldenBolt hand variant");
            EditorUtility.SetDirty(hand);
            Debug.Log("[Shop] GoldenBolt hand variant created.");
        }

        private static void MakeBodyCube(GameObject target)
        {
            MeshFilter filter = target.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        }

        private static ShopTerminal FindOrCreateShop()
        {
            ShopTerminal shop = FindShopComponent();
            if (shop != null) return shop;

            GameObject existing = FindSceneObject(ShopName);
            if (existing != null)
            {
                shop = Undo.AddComponent<ShopTerminal>(existing);
                Debug.Log($"[Shop] Added ShopTerminal to «{ShopName}».");
                return shop;
            }

            Transform anchor = FindSpawnAnchor(out Vector3 position);
            if (anchor == null) return null;

            GameObject go = new GameObject(ShopName);
            Undo.RegisterCreatedObjectUndo(go, "Создать магазин");
            go.transform.SetPositionAndRotation(SnapToGround(position), Quaternion.Euler(0f, anchor.eulerAngles.y, 0f));

            return Undo.AddComponent<ShopTerminal>(go);
        }

        private static ShopTerminal FindShopComponent()
        {
            return Object.FindFirstObjectByType<ShopTerminal>();
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

        private static Transform FindSpawnAnchor(out Vector3 shopPosition)
        {
            shopPosition = Vector3.zero;

            // Если в сцене уже есть терминал спавна — магазин ставим рядом с ним.
            SpawnTerminal terminal = Object.FindFirstObjectByType<SpawnTerminal>();
            if (terminal != null)
            {
                shopPosition = terminal.transform.position + terminal.transform.rotation * new Vector3(5f, 0f, 3f);
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
                Debug.LogError("[Shop] No PlayerController and no Terminal in the scene — cannot pick a spot for the shop.");
                return null;
            }

            shopPosition = player.position + player.rotation * new Vector3(10f, 0f, 4f);
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