using System;
using CarGame.Items;
using CarGame.Player;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Настраивает из редактора предметы тюнинга машины: три щита, три турели и
    /// таран. Как и расходники, каждый предмет существует в двух видах —
    /// префаб для мира (поднимается через PickupItem) и вариант руки (то, что
    /// игрок видит в кулаке). У щитов и турелей в руке висит
    /// <see cref="TuningItem"/> (замедление по весу); таран его не получает,
    /// а установка происходит у места на машине.
    ///
    /// Модели пока заглушки-кубы — их можно заменить, не трогая компоненты.
    /// Всё идемпотентно: повторный запуск чинит ссылки, а не плодит копии.
    /// </summary>
    public static class TuningItemsSetupMenu
    {
        private const string ItemsFolder = "Assets/Prefabs/Items";
        private const string DefaultMaterialPath = "Assets/Materials/Metall.mat";

        private sealed class Setup
        {
            public ItemDef Def;
            public string PrefabPath;
            public string HandVariantName;
        }

        [MenuItem("Tools/CarGame/Tuning/Setup Tuning Items")]
        public static void SetupTuningItems()
        {
            Setup[] setups =
            {
                new Setup { Def = ItemDefs.ShieldScrap, PrefabPath = ItemsFolder + "/ScrapShield.prefab", HandVariantName = "Hand_ScrapShield" },
                new Setup { Def = ItemDefs.ShieldIron, PrefabPath = ItemsFolder + "/IronShield.prefab", HandVariantName = "Hand_IronShield" },
                new Setup { Def = ItemDefs.ShieldPlatinum, PrefabPath = ItemsFolder + "/PlatinumShield.prefab", HandVariantName = "Hand_PlatinumShield" },
                new Setup { Def = ItemDefs.TurretScrap, PrefabPath = ItemsFolder + "/Turret1Item.prefab", HandVariantName = "Hand_TurretScrap" },
                new Setup { Def = ItemDefs.TurretIron, PrefabPath = ItemsFolder + "/Turret2Item.prefab", HandVariantName = "Hand_TurretIron" },
                new Setup { Def = ItemDefs.TurretPlatinum, PrefabPath = ItemsFolder + "/Turret3Item.prefab", HandVariantName = "Hand_TurretPlatinum" },
                new Setup { Def = ItemDefs.RamScrap, PrefabPath = ItemsFolder + "/ScrapMetallRam.prefab", HandVariantName = "Hand_RamScrap" },
                new Setup { Def = ItemDefs.RamIron, PrefabPath = ItemsFolder + "/MetallRam.prefab", HandVariantName = "Hand_RamIron" },
                new Setup { Def = ItemDefs.RamPlatinum, PrefabPath = ItemsFolder + "/PlatinumRam.prefab", HandVariantName = "Hand_RamPlatinum" },
            };

            for (int i = 0; i < setups.Length; i++) SetupOne(setups[i]);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Tuning] Предметы тюнинга настроены: префабы, каталог и варианты руки.");
        }

        private static void SetupOne(Setup setup)
        {
            if (setup.Def == null)
            {
                Debug.LogWarning("[Tuning] ItemDef не найден, пропускаю.");
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
            temp.transform.localScale = new Vector3(0.6f, 0.35f, 0.25f);

            MakeBox(temp, Vector3.one);

            Rigidbody body = temp.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.5f, setup.Def.Weight);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            PickupItem pickup = temp.AddComponent<PickupItem>();
            pickup.SetItemId(setup.Def.Id);
            pickup.SetStackAmount(1);

            EnsureFolder(ItemsFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, setup.PrefabPath);
            UnityEngine.Object.DestroyImmediate(temp);

            Debug.Log($"[Tuning] Префаб создан: {setup.PrefabPath}");
            return prefab;
        }

        private static void EnsureCatalogEntry(string itemId, GameObject prefab)
        {
            if (prefab == null) return;

            ItemWorldCatalog catalog = UnityEngine.Object.FindFirstObjectByType<ItemWorldCatalog>();
            if (catalog == null)
            {
                Debug.LogWarning("[Tuning] В сцене нет ItemWorldCatalog — префаб не добавлен в каталог.");
                return;
            }

            SerializedObject so = new SerializedObject(catalog);
            SerializedProperty entries = so.FindProperty("entries");
            if (entries == null) return;

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (id == null || ItemDefs.Canonical(id.stringValue) != ItemDefs.Canonical(itemId)) continue;

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
            Debug.Log($"[Tuning] '{itemId}' добавлен в ItemWorldCatalog.");
        }

        private static void EnsureHandVariant(Setup setup)
        {
            PlayerHand hand = UnityEngine.Object.FindFirstObjectByType<PlayerHand>();
            if (hand == null)
            {
                Debug.LogWarning("[Tuning] В сцене нет PlayerHand — вариант руки не создан.");
                return;
            }

            SerializedObject so = new SerializedObject(hand);
            SerializedProperty variants = so.FindProperty("handVariants");
            if (variants == null) return;

            TuningKind kind = setup.Def.Tuning ?? TuningKind.Shield;

            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty id = variants.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (id == null || ItemDefs.Canonical(id.stringValue) != setup.Def.Id) continue;

                GameObject bound = variants.GetArrayElementAtIndex(i).FindPropertyRelative("variant").objectReferenceValue as GameObject;
                if (bound != null)
                {
                    TuningItem tuning = bound.GetComponent<TuningItem>();
                    if (kind == TuningKind.Ram && tuning != null) Undo.DestroyObjectImmediate(tuning);
                    else if (kind != TuningKind.Ram && tuning == null) Undo.AddComponent<TuningItem>(bound);
                    EnsureBuildItem(bound, setup.Def);
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(hand);
                return;
            }

            GameObject variant = new GameObject(setup.HandVariantName);
            variant.transform.SetParent(hand.transform, false);
            Undo.RegisterCreatedObjectUndo(variant, "Создать вариант руки " + setup.HandVariantName);

            GameObject model = new GameObject("Model");
            model.transform.SetParent(variant.transform, false);
            MakeVisual(model, new Vector3(0.5f, 0.3f, 0.2f));

            if (kind != TuningKind.Ram) Undo.AddComponent<TuningItem>(variant);
            EnsureBuildItem(variant, setup.Def);

            variants.InsertArrayElementAtIndex(variants.arraySize);
            SerializedProperty added = variants.GetArrayElementAtIndex(variants.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = setup.Def.Id;
            added.FindPropertyRelative("variant").objectReferenceValue = variant;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(hand);
            Debug.Log($"[Tuning] Вариант руки для '{setup.Def.Id}' создан.");
        }

        /// <summary>
        /// Вешает на вариант руки строй-компонент: без него предмет в руке
        /// только замедляет и по ЛКМ ничего не делает. Таран ставится лишь на
        /// готовый чертёж, поэтому у него выключена свободная постановка и не
        /// нужен префаб призрака.
        /// </summary>
        private static void EnsureBuildItem(GameObject variant, ItemDef def)
        {
            BuildItem build = variant.GetComponent<BuildItem>();
            if (build == null) build = Undo.AddComponent<BuildItem>(variant);

            TuningKind kind = def.Tuning ?? TuningKind.Shield;

            SerializedObject so = new SerializedObject(build);

            SerializedProperty kindProp = so.FindProperty("kind");
            if (kindProp != null) kindProp.enumValueIndex = (int)kind;

            SerializedProperty anyProp = so.FindProperty("buildAnyKind");
            if (anyProp != null) anyProp.boolValue = false;

            // Таран работает как турель: свободная постановка
            // выключена, и тогда префаб призрака ему не нужен.
            SerializedProperty freeProp = so.FindProperty("allowFreePlacement");
            if (freeProp != null) freeProp.boolValue = kind != TuningKind.Ram;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(build);
        }

        private static void MakeVisual(GameObject target, Vector3 scale)
        {
            MeshFilter filter = target.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (material != null) renderer.sharedMaterial = material;

            target.transform.localScale = scale;
        }

        private static void MakeBox(GameObject target, Vector3 scale)
        {
            MakeVisual(target, scale);

            BoxCollider box = target.AddComponent<BoxCollider>();
            box.size = Vector3.one;
        }

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
