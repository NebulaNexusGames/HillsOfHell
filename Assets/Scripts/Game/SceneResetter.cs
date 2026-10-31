using System.Collections.Generic;
using CarGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.Game
{
    /// <summary>
    /// Глобальный сброс состояния игры к «первозданному играбельному состоянию» —
    /// тому, в котором сцена оказывается при ПЕРВОМ ЗАПУСКЕ ИГРЫ.
    ///
    /// Смерть в игре фатальна и обнуляет прогресс забега, поэтому после любой
    /// смены сцены (главное меню -> игра, экран смерти -> игра) всё, что было
    /// изменено во время предыдущего забега, обязано вернуться к состоянию
    /// первого запуска.
    ///
    /// Ключевое отличие от простого отката к файлу сцены: снимок снимается НЕ в
    /// момент sceneLoaded (когда объекты ещё «сырые», как в редакторе), а в
    /// конце первого кадра — когда уже отработали все Awake()/Start() игровых
    /// скриптов. То есть эталон включает всю стартовую логику: заспавненные
    /// кодом предметы, построенные интерфейсы, корректные размеры окон,
    /// выбранные темы терминалов и т. п. Именно эту картину игрок видит при
    /// свежем запуске — к ней и возвращается сцена после рестарта.
    ///
    /// Как это работает:
    /// 1. Первая загрузка игровой сцены за запуск приложения: ждём конец кадра
    ///    (все Start'ы выполнены) и снимаем полный снимок: позиция, rotation,
    ///    scale, активность каждого GameObject и сериализуемые поля всех
    ///    MonoBehaviour — включая объекты, созданные кодом на старте.
    /// 2. Снимок хранится в нестатическом поле хост-объекта с DontDestroyOnLoad.
    ///    Хост переживает рестарты сцен, поэтому эталон всегда = состояние
    ///    первого запуска этого запуска приложения, а не PlayerPrefs или кэш.
    /// 3. При каждой повторной загрузке той же сцены (смерть, выход в меню)
    ///    Apply() возвращает все объекты к эталону первого запуска.
    ///
    /// Уничтоженные за забег объекты восстановить из снимка напрямую нельзя —
    /// для них используется копия живого близнеца того же пути, если такой ещё
    /// существует в сцене.
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneResetter : MonoBehaviour
    {
        private const string HostName = "[SceneResetter]";

        [Header("Версия скрипта")]
        [SerializeField] private string scriptVersion = "1.1.0";

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

        // Корневые объекты, существовавшие в момент фиксации эталона. Всё, что
        // появилось в сцене ПОЗЖЕ (сгенерированные за забег постройки, эффекты,
        // выданные из терминала предметы-пустышки), при рестарте уничтожается —
        // иначе в «первозданную» сцену первого запуска просачивается мусор
        // предыдущего забега.
        private readonly HashSet<GameObject> snapshotRoots = new HashSet<GameObject>();

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
                // Первая загрузка сцены за этот запуск приложения. Снимать
                // эталон прямо сейчас нельзя: sceneLoaded приходит ДО Awake/Start,
                // и мы зафиксировали бы «сырое» состояние из редактора (без
                // заспавненных кодом предметов, без настроенных панелей).
                // Ждём конца первого кадра — тогда сцена выглядит ровно так,
                // как её видит игрок при свежем запуске.
                StartCoroutine(CaptureAtEndOfFirstFrame(scene));
            }
            else
            {
                // Повторная загрузка (рестарт после смерти / из главного меню):
                // Unity уже отстроила сцену из файла; дожидаемся старта всех
                // скриптов и возвращаем всё к эталону первого запуска.
                StartCoroutine(ApplyOnNextFrame(existing));
            }
        }

        private System.Collections.IEnumerator CaptureAtEndOfFirstFrame(Scene scene)
        {
            yield return new WaitForEndOfFrame();
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            if (FindSnap(scene.name) != null) yield break; // уже снят

            Capture(scene);
            // Эталон настроек панели терминала — со сценного объекта после
            // полной инициализации.
            TerminalUI.CaptureFromSceneObject();
        }

        private System.Collections.IEnumerator ApplyOnNextFrame(SceneSnap snap)
        {
            // Один кадр даём всем Awake()/Start() отработать на freshly loaded
            // сцене, затем сравниваем с эталоном первого запуска и чиним всё,
            // что стартовая логика восстановила иначе (или не успела).
            yield return null;

            Scene scene = SceneManager.GetSceneByName(snap.sceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            Apply(snap);
            TerminalUI.CaptureFromSceneObject();
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
            snapshotRoots.Clear();
            foreach (GameObject root in roots)
            {
                if (root == gameObject) continue;
                snapshotRoots.Add(root);
                Collect(root.transform, root.name, null, list);
            }

            SceneSnap snap = new SceneSnap { sceneName = scene.name, objects = list.ToArray() };
            snapshots.Add(snap);
            Debug.Log($"[SceneResetter] Зафиксировано первозданное состояние сцены '{scene.name}' (конец первого кадра): объектов {list.Count}.");
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
                if (!capturedTypes.TryGetValue(key, out System.Type knownType))
                {
                    knownType = type;
                    capturedTypes[key] = knownType;
                }
                int idx = typeList.IndexOf(knownType);
                if (idx < 0)
                {
                    idx = typeList.Count;
                    typeList.Add(knownType);
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

            // 1. Удаляем всё, чего не было в эталоне первого запуска: объекты,
            //    заспавненные/построенные уже ПОСЛЕ фиксации снимка (постройки
            //    за забег, эффекты, выданные из терминала пустышки-предметы).
            DestroyForeignObjects(scene);

            int restored = 0, missing = 0;
            foreach (ObjSnap s in snap.objects)
            {
                Transform t = ResolvePath(scene, s.path);
                if (t == null)
                {
                    // Объекта больше нет: он был уничтожен за забег. Восстанавливаем
                    // копию из живого близнеца, если такой ещё есть в сцене.
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

            Debug.Log($"[SceneResetter] Сцена '{snap.sceneName}' сброшена к первозданному состоянию первого запуска: полей восстановлено {restored}, объектов утрачено {missing}.");
        }

        /// <summary>
        /// Уничтожает корневые объекты загружаемой сцены, которых не было в
        /// эталоне (появились уже после фиксации снимка первого запуска).
        /// DontDestroyOnLoad-объекты (хосты, менеджеры) не трогаем — они не
        /// относятся к этой сцене.
        /// </summary>
        private void DestroyForeignObjects(Scene scene)
        {
            if (snapshotRoots.Count == 0) return; // эталон по какой-то причине пуст — не рискуем
            GameObject[] roots = scene.GetRootGameObjects();
            int destroyed = 0;
            foreach (GameObject root in roots)
            {
                if (root == gameObject) continue;
                if (snapshotRoots.Contains(root)) continue;
                DontDestroyOnLoad(root); // выводим из сцены, чтобы Destroy не задел чужое
                Destroy(root);
                destroyed++;
            }
            if (destroyed > 0)
                Debug.Log($"[SceneResetter] Удалено объектов, появившихся после эталона: {destroyed}.");
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
