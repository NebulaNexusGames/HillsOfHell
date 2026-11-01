using System;
using CarGame.Items;
using CarGame.Player;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Настраивает из редактора предметы-расходники: энергетик и пиво.
    /// Каждому нужны две ипостаси — префаб для мира (лежит, падает,
    /// поднимается через PickupItem) и вариант руки (то, что игрок видит
    /// в кулаке и что применяется как IHandUsable).
    ///
    /// Модели тут — заглушки-кубы: анимации и настоящие модели появятся
    /// позже, их можно просто заменить, не трогая компоненты расхода.
    /// Всё идемпотентно: повторный запуск чинит ссылки, а не плодит копии.
    /// </summary>
    public static class ConsumablesSetupMenu
    {
        private const string ItemsFolder = "Assets/Prefabs/Items";
        private const string DefaultMaterialPath = "Assets/Materials/Metall.mat";

        private sealed class Setup
        {
            public ItemDef Def;
            public string PrefabPath;
            public string HandVariantName;
            public Type Component;
        }

        [MenuItem("Tools/CarGame/Consumables/Setup Consumables")]
        public static void SetupConsumables()
        {
            Setup[] setups =
            {
                new Setup
                {
                    Def = ItemDefs.EnergyDrink,
                    PrefabPath = ItemsFolder + "/EnergyDrink.prefab",
                    HandVariantName = "Hand_EnergyDrink",
                    Component = typeof(EnergyDrinkItem)
                },
                new Setup
                {
                    Def = ItemDefs.Beer,
                    PrefabPath = ItemsFolder + "/Beer.prefab",
                    HandVariantName = "Hand_Beer",
                    Component = typeof(BeerItem)
                }
            };

            for (int i = 0; i < setups.Length; i++) SetupOne(setups[i]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Consumables] Энергетик и пиво настроены: префабы, каталог и варианты руки.");
        }

        private static void SetupOne(Setup setup)
        {
            if (setup.Def == null)
            {
                Debug.LogWarning("[Consumables] ItemDef не найден, пропускаю.");
                return;
            }

            GameObject prefab = EnsurePrefab(setup);
            EnsureCatalogEntry(setup.Def.Id, prefab);
            EnsureHandVariant(setup);
        }

        private static GameObject EnsurePrefab(Setup setup)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(setup.PrefabPath);
            if (existing != null) return existing;

            GameObject temp = new GameObject(setup.Def.DisplayName);

            MeshFilter filter = temp.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = temp.AddComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (material != null) renderer.sharedMaterial = material;

            // Компактная банка/бутылка: в мире предмет должен быть заметен,
            // но не крупнее кулака.
            temp.transform.localScale = new Vector3(0.14f, 0.3f, 0.14f);

            BoxCollider box = temp.AddComponent<BoxCollider>();
            box.size = Vector3.one;

            Rigidbody body = temp.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            PickupItem pickup = temp.AddComponent<PickupItem>();
            pickup.SetItemId(setup.Def.Id);
            pickup.SetStackAmount(1);

            EnsureFolder(ItemsFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, setup.PrefabPath);
            UnityEngine.Object.DestroyImmediate(temp);

            Debug.Log($"[Consumables] Префаб создан: {setup.PrefabPath}");
            return prefab;
        }

        private static void EnsureCatalogEntry(string itemId, GameObject prefab)
        {
            if (prefab == null) return;

            ItemWorldCatalog catalog = UnityEngine.Object.FindFirstObjectByType<ItemWorldCatalog>();
            if (catalog == null)
            {
                Debug.LogWarning("[Consumables] В сцене нет ItemWorldCatalog — префаб не добавлен в каталог.");
                return;
            }

            SerializedObject so = new SerializedObject(catalog);
            SerializedProperty entries = so.FindProperty("entries");
            if (entries == null) return;

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (id == null || id.stringValue != itemId) continue;

                entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(catalog);
                return;
            }

            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = itemId;
            added.FindPropertyRelative("prefab").objectReferenceValue = prefab;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            Debug.Log($"[Consumables] '{itemId}' добавлен в ItemWorldCatalog.");
        }

        private static void EnsureHandVariant(Setup setup)
        {
            PlayerHand hand = UnityEngine.Object.FindFirstObjectByType<PlayerHand>();
            if (hand == null)
            {
                Debug.LogWarning("[Consumables] В сцене нет PlayerHand — вариант руки не создан.");
                return;
            }

            SerializedObject so = new SerializedObject(hand);
            SerializedProperty variants = so.FindProperty("handVariants");
            if (variants == null) return;

            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty id = variants.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (id == null || id.stringValue != setup.Def.Id) continue;

                GameObject bound = variants.GetArrayElementAtIndex(i).FindPropertyRelative("variant").objectReferenceValue as GameObject;
                if (bound != null) EnsureComponent(bound, setup.Component);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(hand);
                return;
            }

            GameObject variant = new GameObject(setup.HandVariantName);
            variant.transform.SetParent(hand.transform, false);
            Undo.RegisterCreatedObjectUndo(variant, "Создать вариант руки " + setup.HandVariantName);

            GameObject model = new GameObject("Model");
            model.transform.SetParent(variant.transform, false);
            MakeCube(model);

            EnsureComponent(variant, setup.Component);

            variants.InsertArrayElementAtIndex(variants.arraySize);
            SerializedProperty added = variants.GetArrayElementAtIndex(variants.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = setup.Def.Id;
            added.FindPropertyRelative("variant").objectReferenceValue = variant;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(hand);
            Debug.Log($"[Consumables] Вариант руки для '{setup.Def.Id}' создан.");
        }

        private static void EnsureComponent(GameObject target, Type type)
        {
            if (target == null || type == null) return;
            if (target.GetComponent(type) != null) return;
            Undo.AddComponent(target, type);
        }

        private static void MakeCube(GameObject target)
        {
            MeshFilter filter = target.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (material != null) renderer.sharedMaterial = material;

            target.transform.localScale = new Vector3(0.12f, 0.28f, 0.12f);
        }

        /// <summary>Создаёт папку ассета, если её ещё нет.</summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            int slash = folder.LastIndexOf('/');
            string parent = slash > 0 ? folder.Substring(0, slash) : "Assets";
            string leaf = folder.Substring(parent.Length + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}