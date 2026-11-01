using CarGame.Items;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Подключает два фонарика к каталогу предметов: обычный и светотряс.
    /// Подбираемые копии уже лежат в Assets/Prefabs/Items (Flashlight.prefab с
    /// ID 20 и Shakelight.prefab с ID 19), а варианты руки собраны в сцене —
    /// инструмент их не создаёт и не трогает. Он только прописывает префабы в
    /// ItemWorldCatalog, чтобы фонарики можно было выбрасывать и подбирать.
    ///
    /// Идемпотентно: повторный запуск чинит ссылку, а не плодит записи.
    /// </summary>
    public static class FlashlightsSetupMenu
    {
        private const string ItemsFolder = "Assets/Prefabs/Items";

        [MenuItem("Tools/CarGame/Items/Setup Flashlights")]
        public static void SetupFlashlights()
        {
            EnsureCatalogEntry(ItemDefs.Flashlight, ItemsFolder + "/Flashlight.prefab");
            EnsureCatalogEntry(ItemDefs.ShakeLight, ItemsFolder + "/Shakelight.prefab");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Flashlights] Фонарик и светотряс подключены к каталогу предметов.");
        }

        private static void EnsureCatalogEntry(ItemDef def, string prefabPath)
        {
            if (def == null) return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Flashlights] Нет префаба '{prefabPath}' — '{def.Id}' не добавлен в каталог.");
                return;
            }

            ItemWorldCatalog catalog = Object.FindFirstObjectByType<ItemWorldCatalog>();
            if (catalog == null)
            {
                Debug.LogWarning("[Flashlights] В сцене нет ItemWorldCatalog — префаб не добавлен в каталог.");
                return;
            }

            SerializedObject so = new SerializedObject(catalog);
            SerializedProperty entries = so.FindProperty("entries");
            if (entries == null) return;

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty id = entries.GetArrayElementAtIndex(i).FindPropertyRelative("itemId");
                if (id == null || id.stringValue != def.Id) continue;

                entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(catalog);
                return;
            }

            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = def.Id;
            added.FindPropertyRelative("prefab").objectReferenceValue = prefab;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            Debug.Log($"[Flashlights] '{def.Id}' добавлен в ItemWorldCatalog.");
        }
    }
}
