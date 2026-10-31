using System.Collections.Generic;
using System.IO;
using CarGame.Items;
using CarGame.Player;
using UnityEditor;
using UnityEngine;

namespace CarGame.EditorTools
{
    /// <summary>
    /// Настройка терминала спавна из редактора. Раскладка объекта в сцене:
    /// Terminal (пустой корень с компонентом SpawnTerminal) → Model (внешний вид)
    /// и SpawnPanel (панель с коллайдером) → SpawnPoint (точка спавна).
    ///
    /// Префабов мобов в проекте нет — мобы лежат только объектами сцены, поэтому
    /// отдельным пунктом меню они превращаются в префабы.
    /// </summary>
    public static class TerminalSetupMenu
    {
        private const string TerminalName = "Terminal";
        private const string ModelName = "Model";
        private const string PanelName = "SpawnPanel";
        private const string PointName = "SpawnPoint";
        private const string ScreenPointName = "ScreenPoint";
        private const string ActiveTriggerName = "ActiveTrigger";
        private const string MobsFolder = "Assets/Prefabs/Mobs";
        private const string DefaultMaterialPath = "Assets/Materials/Metall.mat";

        private static readonly string[] MobSourceNames =
        {
            "Dron",
            "DinamiteBot",
            "SwordBot"
        };

        private static readonly string[] MobDisplayNames =
        {
            "Drone",
            "Dynamite Bot",
            "Swordsman"
        };

        /// <summary>
        /// Главный пункт: находит объект Terminal (или создаёт его), дозакрепляет
        /// Model и SpawnPoint и заполняет список мобов. Идемпотентно — можно
        /// жать повторно, чтобы починить потерянные ссылки.
        /// </summary>
        [MenuItem("Tools/CarGame/Terminal/Setup Terminal")]
        public static void SetupTerminal()
        {
            SpawnTerminal terminal = FindOrCreateTerminal();
            if (terminal == null) return;

            EnsureHierarchy(terminal);

            SpawnTerminal.MobEntry[] entries = LoadMobEntriesFromDisk();
            if (entries.Length == 0)
            {
                // Префабов ещё нет — делаем их сразу, чтобы терминал после одного
                // клика был готов, а не пустой.
                CreateMobPrefabs();
                entries = terminal.Mobs;
            }

            terminal.SetMobs(entries);
            EditorUtility.SetDirty(terminal);

            CreateEditableHud();

            Selection.activeGameObject = terminal.gameObject;
            Debug.Log($"[Terminal] Done. Mobs in list: {(entries != null ? entries.Length : 0)}.");
        }

        /// <summary>
        /// Creates a real scene object with TerminalUI on it. Without it the panel
        /// was built at runtime on a throwaway GameObject, so none of its settings
        /// could be edited: colors, text and cell size were hardcoded. Now the
        /// component lives in the scene and is wired to the terminal, and the
        /// Inspector edits it directly.
        /// </summary>
        [MenuItem("Tools/CarGame/Terminal/Create Editable HUD")]
        public static void CreateEditableHud()
        {
            SpawnTerminal terminal = FindOrCreateTerminal();
            if (terminal == null)
            {
                Debug.LogWarning("[Terminal] No terminal found, HUD not created.");
                return;
            }

            CarGame.UI.TerminalUI existing = Object.FindFirstObjectByType<CarGame.UI.TerminalUI>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.SetDirty(existing);
                Debug.Log("[Terminal] Editable HUD already exists: " + existing.name);
                return;
            }

            GameObject go = new GameObject("TerminalHUD");
            go.transform.SetParent(terminal.transform.parent, false);
            CarGame.UI.TerminalUI hud = go.AddComponent<CarGame.UI.TerminalUI>();
            hud.Attach(terminal);

            Undo.RegisterCreatedObjectUndo(go, "Create Terminal HUD");
            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;
            Debug.Log("[Terminal] Editable HUD created and wired to the terminal.");
        }

        [MenuItem("Tools/CarGame/Terminal/Create Mob Prefabs")]
        public static void CreateMobPrefabs()
        {
            List<SpawnTerminal.MobEntry> entries = new List<SpawnTerminal.MobEntry>();
            int created = 0;

            for (int i = 0; i < MobSourceNames.Length; i++)
            {
                GameObject source = FindSceneObject(MobSourceNames[i]);
                if (source == null)
                {
                    Debug.LogWarning($"[Terminal] No scene object «{MobSourceNames[i]}» found — skipping.");
                    continue;
                }

                string path = PrefabPath(MobSourceNames[i]);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
                if (prefab == null)
                {
                    Debug.LogError($"[Terminal] Failed to save prefab «{path}».");
                    continue;
                }

                created++;
                entries.Add(new SpawnTerminal.MobEntry
                {
                    displayName = MobDisplayNames[i],
                    prefab = prefab
                });
            }

            if (created == 0)
            {
                Debug.LogWarning("[Terminal] Mob prefabs not created: no source objects found in the scene.");
                return;
            }

            SpawnTerminal terminal = FindTerminalComponent();
            if (terminal != null)
            {
                terminal.SetMobs(entries.ToArray());
                EditorUtility.SetDirty(terminal);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Terminal] Mob prefabs created: {created}.");
        }

        /// <summary>Дозакрепляет Model и SpawnPoint, создавая недостающие объекты.</summary>
        [MenuItem("Tools/CarGame/Terminal/Repair Model and SpawnPoint")]
        public static void RepairHierarchy()
        {
            SpawnTerminal terminal = FindTerminalComponent();
            if (terminal == null)
            {
                Debug.LogWarning("[Terminal] No Terminal object with SpawnTerminal found. Run «Setup Terminal» first.");
                return;
            }

            EnsureHierarchy(terminal);
            Debug.Log("[Terminal] Model and SpawnPoint are in place.");
        }

        private static void EnsureHierarchy(SpawnTerminal terminal)
        {
            GameObject root = terminal.gameObject;

            GameObject model = FindChild(root, ModelName);
            if (model == null)
            {
                model = new GameObject(ModelName);
                model.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(model, "Создать Model");
                MakeBodyCube(model);
            }

            GameObject panel = FindChild(root, PanelName);
            if (panel == null)
            {
                panel = new GameObject(PanelName);
                panel.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(panel, "Создать SpawnPanel");
                MakeBodyCube(panel);
            }

            if (panel.GetComponent<Collider>() == null)
            {
                BoxCollider box = panel.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one;
            }

            GameObject point = FindChild(panel, PointName);
            if (point == null)
            {
                point = new GameObject(PointName);
                point.transform.SetParent(panel.transform, false);
                point.transform.localPosition = new Vector3(0f, 0f, 3f);
                Undo.RegisterCreatedObjectUndo(point, "Создать SpawnPoint");
            }

            terminal.SetModel(model);
            terminal.SetSpawnPoint(point.transform);

            GameObject screenPoint = FindChild(root, ScreenPointName);
            if (screenPoint == null)
            {
                screenPoint = new GameObject(ScreenPointName);
                screenPoint.transform.SetParent(root.transform, false);
                screenPoint.transform.localPosition = new Vector3(0f, 1.5f, -2.5f);
                Undo.RegisterCreatedObjectUndo(screenPoint, "Создать ScreenPoint");
            }

            terminal.SetScreenPoint(screenPoint.transform);
            terminal.SetActiveTrigger(EnsureActiveTrigger(root, screenPoint.transform));

            EditorUtility.SetDirty(terminal);
        }

        /// <summary>
        /// Триггер активации: отдельный объём перед терминалом, в который надо
        /// зайти, чтобы появилась панель. Ставится перед точкой экрана и
        /// достаточно высоким, чтобы в него попал корпус игрока целиком.
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

            // Перед точкой экрана: игрок заходит в триггер и оказывается лицом к панели.
            Vector3 target = screenPoint != null
                ? screenPoint.position
                : go.transform.position + Vector3.forward * 2f;

            Vector3 local = root.transform.InverseTransformPoint(target);
            Vector3 offset = local - new Vector3(0f, 1f, 0f);
            go.transform.localPosition = new Vector3(offset.x, offset.y, offset.z + 2f);

            TerminalActivationTrigger trigger = go.GetComponent<TerminalActivationTrigger>();
            if (trigger == null) trigger = Undo.AddComponent<TerminalActivationTrigger>(go);

            SpawnTerminal terminal = root.GetComponent<SpawnTerminal>();
            trigger.SetTerminal(terminal);

            return trigger;
        }

        private static void MakeBodyCube(GameObject target)
        {
            MeshFilter filter = target.AddComponent<MeshFilter>();
            Mesh mesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx");
            if (mesh != null) filter.sharedMesh = mesh;

            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        }

        private static SpawnTerminal FindOrCreateTerminal()
        {
            SpawnTerminal terminal = FindTerminalComponent();
            if (terminal != null) return terminal;

            GameObject existing = FindSceneObject(TerminalName);
            if (existing != null)
            {
                terminal = Undo.AddComponent<SpawnTerminal>(existing);
                Debug.Log($"[Terminal] Added SpawnTerminal to «{TerminalName}».");
                return terminal;
            }

            Transform anchor = FindSpawnAnchor(out Vector3 position);
            if (anchor == null) return null;

            GameObject go = new GameObject(TerminalName);
            Undo.RegisterCreatedObjectUndo(go, "Создать терминал");
            go.transform.SetPositionAndRotation(SnapToGround(position), Quaternion.Euler(0f, anchor.eulerAngles.y, 0f));

            return Undo.AddComponent<SpawnTerminal>(go);
        }

        private static SpawnTerminal FindTerminalComponent()
        {
            return Object.FindFirstObjectByType<SpawnTerminal>();
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

        private static Transform FindSpawnAnchor(out Vector3 terminalPosition)
        {
            terminalPosition = Vector3.zero;

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
                Debug.LogError("[Terminal] No PlayerController in the scene — cannot pick a spot for the terminal.");
                return null;
            }

            // Сбоку от старта игрока: там не мешает ни стартовый мусор, ни полоса
            // дороги, а терминал сразу видно при выходе из машины.
            terminalPosition = player.position + player.rotation * new Vector3(7f, 0f, -5f);
            return player;
        }

        private static SpawnTerminal.MobEntry[] LoadMobEntriesFromDisk()
        {
            List<SpawnTerminal.MobEntry> entries = new List<SpawnTerminal.MobEntry>();

            for (int i = 0; i < MobSourceNames.Length; i++)
            {
                string path = PrefabPath(MobSourceNames[i]);
                if (!File.Exists(path)) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                entries.Add(new SpawnTerminal.MobEntry
                {
                    displayName = MobDisplayNames[i],
                    prefab = prefab
                });
            }

            return entries.ToArray();
        }

        private static string PrefabPath(string name)
        {
            return $"{MobsFolder}/{name}.prefab";
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
