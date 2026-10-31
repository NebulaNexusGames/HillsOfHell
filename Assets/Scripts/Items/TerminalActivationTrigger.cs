using CarGame.Player;
using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Триггер активации терминала. Пока игрок внутри этого объёма — панель
    /// терминала показывается, вышел — гаснет. Никаких нажатий: зашёл в триггер,
    /// панель на экране.
    ///
    /// Положение игрока сверяется с границами самого коллайдера, а не через
    /// OnTriggerEnter. CharacterController в паре с триггером события шлёт
    /// не всегда (например, если слои терминала и игрока выключены в матрице
    /// столкновений), и тогда панель просто не появлялась бы. Объём при этом
    /// ровно тот же, что и у обычного триггера — этот самый BoxCollider.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class TerminalActivationTrigger : MonoBehaviour
    {
        [Tooltip("Терминал, который этот триггер активирует")]
        [SerializeField] private SpawnTerminal terminal;

        [Tooltip("Магазин, который этот триггер активирует, если триггер стоит на магазине")]
        [SerializeField] private ShopTerminal shop;

        [Tooltip("Автомат, который этот триггер активирует, если триггер стоит на автомате")]
        [SerializeField] private SlotMachineTerminal slotMachine;

        [Tooltip("Обменник (переработчик), который этот триггер активирует, если триггер стоит на нём")]
        [SerializeField] private ExchangerTerminal exchanger;

        [Tooltip("Насколько расширить объём на столько метров. Немного — чтобы зайти было легче")]
        [SerializeField] private float skinWidth = 0.2f;

        [Tooltip("Запасной радиус, если объём почему-то не сработал. 0 — выключить, тогда панель только по триггеру")]
        [SerializeField] private float fallbackRadius;

        private BoxCollider volume;
        private Transform cachedPlayer;
        private Camera cachedCamera;

        /// <summary>Терминал, который активирует этот триггер.</summary>
        public SpawnTerminal Terminal => terminal;

        /// <summary>Магазин, который активирует этот триггер.</summary>
        public ShopTerminal Shop => shop;

        /// <summary>Автомат, который активирует этот триггер.</summary>
        public SlotMachineTerminal SlotMachine => slotMachine;

        /// <summary>Обменник, который активирует этот триггер.</summary>
        public ExchangerTerminal Exchanger => exchanger;

        /// <summary>Игрок сейчас внутри триггера.</summary>
        public bool IsPlayerInside { get; private set; }

        private void Awake()
        {
            volume = GetComponent<BoxCollider>();

            // Объём должен быть именно триггером: иначе терминал начнёт
            // расталкивать игрока.
            if (volume != null && !volume.isTrigger) volume.isTrigger = true;
        }

        /// <summary>Привязывает триггер к терминалу. Вызывается при настройке сцены.</summary>
        public void SetTerminal(SpawnTerminal value)
        {
            terminal = value;
        }

        /// <summary>Привязывает триггер к магазину. Вызывается при настройке сцены.</summary>
        public void SetShopTerminal(ShopTerminal value)
        {
            shop = value;
        }

        /// <summary>Привязывает триггер к автомату. Вызывается при настройке сцены.</summary>
        public void SetSlotMachine(SlotMachineTerminal value)
        {
            slotMachine = value;
        }

        /// <summary>Привязывает триггер к обменнику. Вызывается при настройке сцены.</summary>
        public void SetExchanger(ExchangerTerminal value)
        {
            exchanger = value;
        }

        private void Update()
        {
            bool inside = Evaluate();
            IsPlayerInside = inside;

            if (terminal != null) terminal.SetPlayerInside(inside);
            else if (shop != null) shop.SetPlayerInside(inside);
            else if (slotMachine != null) slotMachine.SetPlayerInside(inside);
            else if (exchanger != null) exchanger.SetPlayerInside(inside);
        }

private bool Evaluate()
        {
            if (volume == null) return false;

            Transform playerTransform = ResolvePlayerTransform();
            if (playerTransform == null) return false;

            Bounds box = volume.bounds;
            box.Expand(skinWidth * 2f);

            // Рост игрока больше объёма триггера — норма, поэтому сравниваем
            // пересечение габаритов, а не точку входа. Ноги в триггере — уже
            // «зашёл».
            Collider body = playerTransform.GetComponent<Collider>();
            Bounds playerBounds = body != null
                ? body.bounds
                : new Bounds(playerTransform.position + Vector3.up, Vector3.one);

            if (box.Intersects(playerBounds)) return true;

            // Страховка: если объём всё-таки не ловит (например, терминал
            // оказался под лестницей и игрок стоит в паре сантиметров), панель
            // не должна пропадать насовсем.
            return fallbackRadius > 0f &&
                   Vector3.Distance(playerTransform.position, box.center) <= fallbackRadius;
        }

        /// <summary>
        /// Где находится игрок. Сначала PlayerController, но он не обязан
        /// существовать и быть включённым — тогда берём камеру, она в тех же
        /// координатах. Терминалу не нужно ни «включение», ни живой контроллер:
        /// зашёл в объём — панель есть.
        /// </summary>
        private Transform ResolvePlayerTransform()
        {
            if (cachedPlayer != null) return cachedPlayer;

            PlayerController controller = FindFirstObjectByType<PlayerController>();
            if (controller != null) return cachedPlayer = controller.transform;

            if (cachedCamera == null) cachedCamera = Camera.main;
            return cachedCamera != null ? cachedCamera.transform : null;
        }


#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.25f);
            box.isTrigger = true;
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);

            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size + Vector3.one * (skinWidth * 2f));
        }
#endif
    }
}
