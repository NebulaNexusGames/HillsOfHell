using System.Collections.Generic;
using CarGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.Game
{
    /// <summary>
    /// Глобальный сброс состояния игры к «заводским» значениям из файла сцены.
    ///
    /// Смерть в игре фатальна и обнуляет прогресс забега, поэтому после любой
    /// смены сцены (главное меню -> игра, экран смерти -> игра) всё, что было
    /// изменено во время предыдущего забега, обязано вернуться к состоянию,
    /// которое автор проекта сохранил в Unity.
    ///
    /// Как это работает:
    /// 1. При первой загрузке игровой сцены Unity восстанавливает объекты ровно
    ///    так, как они сохранены в .unity-файле. В этот момент снимается полный
    ///    снимок сцены: позиция, rotation, scale, активность каждого GameObject
    ///    и сериализуемые поля всех MonoBehaviour — то есть именно те значения,
    ///    что видны в инспекторе.
    /// 2. Снимок хранится в нестатическом поле хост-объекта с DontDestroyOnLoad.
    ///    Статика живёт между загрузками сцен, но НЕ между запусками приложения,
    ///    а DontDestroyOnLoad-хост переживает рестарты сцен — эталон всегда
    ///    берётся из файла проекта, а не из PlayerPrefs или кэша.
    /// 3. При каждой повторной загрузке той же сцены (смерть, выход в меню)
    ///    Apply() возвращает все объекты к эталону: сбитые позиции, состояние
    ///    включено/выключено и настройки компонентов (в т.ч. размеры окна и
    ///    музыкальная тема терминала) сбрасываются.
    ///
    /// Уничтоженные за забег объекты (DelHammer, взрывы) полностью восстановить
    /// из снимка нельзя — для них используется копия живого близнеца того же
    /// пути, если такой ещё существует в сцене.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneResetter : MonoBehaviour
    {
        private const string HostName = "[SceneResetter]";

        [Header("Версия скрипта")]
        [SerializeField] private string scriptVersion = "1.0.0";

        [Tooltip("Сцены, состояние которых фиксируется и сбрасывается после перезапуска. Пусто = любые загружаемые сцены.")]
        [SerializeField] private List<string> managedScenes = new List<string> { "SampleScene" };

        private static SceneResetter instance;

        private class ObjSnap
        {
            public TransformPath path;
            public bool activeSelf;
            public Vector3 localPos, localEuler, localScale;
            public ComponentSnap[] components;
        }

        private class ComponentSnap
        {
            public int typeIndex;              // index in typeList
            public string typeName;            // fallback: full assembly name
            public string json;                // JsonUtility-serialized fields
        }

        /// <summary>Путь объекта в сцене: корневое имя + цепочка имён детей.</summary>
        private struct TransformPath
        {
            public string root;
            public string[] children;

            public override string ToString()
            {
                if (children == null || children.Length == 0) return root;
                return root + "/" + string.Join("/", children);
            }
        }

        private sealed class SceneSnap
        {
            public string sceneName;
            public ObjSnap[] objects;
        }

        private readonly List<SceneSnap> snapshots = new List<SceneSnap>();
        private readonly Dictionary<string, System.Type> capturedTypes = new Dictionary<string, System.Type>();
        private readonly List<System.Type> typeList = new List<System.Type>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Единственный хост-объект живёт всю игру: пересоздавать его нельзя,
            // иначе потеряем эталонные снимки между загрузками сцен.
            if (instance != null) return;
            GameObject go = new GameObject(HostName);
            DontDestroyOnLoad(go);
            go.AddComponent<SceneResetter>();
        }

        private void OnEnable()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this) instance = null;
        }

        private bool IsManaged(string sceneName)
        {
            if (managedScenes == null || managedScenes.Count == 0) return true;
            for (int i = 0; i < managedScenes.Count; i++)
                if (!string.IsNullOrEmpty(managedScenes[i]) && managedScenes[i] == sceneName)
                    return true;
            return false;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsManaged(scene.name)) return;

            SceneSnap existing = FindSnap(scene.name);
            if (existing == null)
            {
                // Первая загрузка сцены за этот запуск приложения: объекты ещё
                // не тронуты кодом (sceneLoaded приходит до Start'ов) — снимаем
                // эталон ровно из состояния файла.
                Capture(scene);
                // Эталон настроек панели терминала — со сценного объекта,
                // до того как Show() начнёт пересоздавать панель кодом.
                TerminalUI.CaptureFromSceneObject();
            }
            else
            {
                // Повторная загрузка (рестарт после смерти / из главного меню):
                // возвращаем всё к эталону из файла проекта.
                Apply(existing);
                TerminalUI.CaptureFromSceneObject();
            }
        }

        private SceneSnap FindSnap(string sceneName)
        {
            for (int i = 0; i < snapshots.Count; i++)
                if (snapshots[i].sceneName == sceneName) return snapshots[i];
            return null;
        }

        // ---------- Фиксация ----------

        private void Capture(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            var list = new List<ObjSnap>(roots.Length);
            foreach (GameObject root in roots)
                if (root != gameObject) Collect(root.transform, root.name, null, list);

            SceneSnap snap = new SceneSnap { sceneName = scene.name, objects = list.ToArray() };
            snapshots.Add(snap);
            Debug.Log($"[SceneResetter] Зафиксировано состояние сцены '{scene.name}': объектов {list.Count}.");
        }

        private void Collect(Transform t, string rootName, string[] parentChain, List<ObjSnap> into)
        {
            ObjSnap s = new ObjSnap
            {
                path = new TransformPath { root = rootName, children = parentChain },
                activeSelf = t.gameObject.activeSelf,
                localPos = t.localPosition,
                localEuler = t.localEulerAngles,
                localScale = t.localScale,
            };

            MonoBehaviour[] behaviours = t.GetComponents<MonoBehaviour>();
            var comps = new List<ComponentSnap>(behaviours.Length);
            foreach (MonoBehaviour mb in behaviours)
            {
                if (mb == null) continue;                 // destroyed component
                if (mb is SceneResetter) continue;        // сам хост не трогаем
                System.Type type = mb.GetType();
                string key = type.FullName;
                if (!capturedTypes.TryGetValue(key, out int idx))
                {
                    idx = typeList.Count;
                    capturedTypes[key] = idx;
                    typeList.Add(type);
                }
                comps.Add(new ComponentSnap
                {
                    typeIndex = idx,
                    typeName = key,
                    json = SafeToJson(mb),
                });
            }
            s.components = comps.ToArray();
            into.Add(s);

            for (int i = 0; i < t.childCount; i++)
            {
                string[] chain;
                if (parentChain == null)
                {
                    chain = new string[] { t.GetChild(i).name };
                }
                else
                {
                    chain = new string[parentChain.Length + 1];
                    System.Array.Copy(parentChain, chain, parentChain.Length);
                    chain[chain.Length - 1] = t.GetChild(i).name;
                }
                Collect(t.GetChild(i), rootName, chain, into);
            }
        }

        private static string SafeToJson(MonoBehaviour mb)
        {
            try { return JsonUtility.ToJson(mb); }
            catch (System.Exception) { return null; } // неподдерживаемые типы просто пропускаются
        }

        // ---------- Восстановление ----------

        private void Apply(SceneSnap snap)
        {
            Scene scene = SceneManager.GetSceneByName(snap.sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return;

            int restored = 0, missing = 0;
            foreach (ObjSnap s in snap.objects)
            {
                Transform t = ResolvePath(scene, s.path);
                if (t == null)
                {
                    // Объекта больше нет: он был уничтожен за забег (DelHammer,
                    // взрывы, разобранные постройки). Восстанавливаем копию из
                    // живого близнеца, если такой ещё есть в сцене.
                    GameObject clone = TryRecreate(scene, s);
                    if (clone == null) { missing++; continue; }
                    t = clone.transform;
                }

                t.localPosition = s.localPos;
                t.localEulerAngles = s.localEuler;
                t.localScale = s.localScale;
                t.gameObject.SetActive(s.activeSelf);

                MonoBehaviour[] behaviours = t.GetComponents<MonoBehaviour>();
                foreach (ComponentSnap cs in s.components)
                {
                    if (cs.json == null) continue;
                    System.Type type = cs.typeIndex >= 0 && cs.typeIndex < typeList.Count
                        ? typeList[cs.typeIndex]
                        : System.Type.GetType(cs.typeName);
                    if (type == null) continue;
                    MonoBehaviour target = FindByType(behaviours, type);
                    if (target == null) continue; // компонент удалили за забег — не навязываем
                    try { JsonUtility.FromJsonOverwrite(cs.json, target); restored++; }
                    catch (System.Exception) { }
                }
            }

            Debug.Log($"[SceneResetter] Сцена '{snap.sceneName}' сброшена к состоянию проекта: полей восстановлено {restored}, объектов утрачено {missing}.");
        }

        private static MonoBehaviour FindByType(MonoBehaviour[] all, System.Type type)
        {
            foreach (MonoBehaviour mb in all)
                if (mb != null && mb.GetType() == type) return mb;
            return null;
        }

        private static Transform ResolvePath(Scene scene, TransformPath path)
        {
            GameObject found = null;
            foreach (GameObject r in scene.GetRootGameObjects())
                if (r.name == path.root) { found = r; break; }
            if (found == null) return null;
            Transform t = found.transform;
            if (path.children != null)
                foreach (string child in path.children)
                {
                    if (t == null) return null;
                    t = t.Find(child);
                }
            return t;
        }

        /// <summary>
        /// Пытается воссоздать уничтоженный объект копией живого близнеца
        /// (того же корневого имени и пути). Если близнецов нет — объект
        /// остаётся удалённым до следующего полного запуска игры: Unity не
        /// даёт реконструировать GameObject из JSON без ссылки на префаб.
        /// </summary>
        private static GameObject TryRecreate(Scene scene, ObjSnap s)
        {
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.name != s.path.root) continue;
                Transform twin = MatchTwin(r.transform, s.path);
                if (twin != null && twin.GetComponent<SceneResetter>() == null)
                {
                    GameObject copy = Instantiate(twin.gameObject);
                    copy.name = twin.name;
                    copy.transform.SetParent(twin.parent, false);
                    return copy;
                }
            }
            return null;
        }

        private static Transform MatchTwin(Transform root, TransformPath path)
        {
            if (path.children == null || path.children.Length == 0) return root;
            Transform t = root;
            foreach (string child in path.children)
            {
                if (t == null) return null;
                t = t.Find(child);
            }
            return t;
        }
    }
}
