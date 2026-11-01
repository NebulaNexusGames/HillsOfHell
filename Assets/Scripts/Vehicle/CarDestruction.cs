using CarGame.Combat;
using UnityEngine;

namespace CarGame.Vehicle
{
    /// <summary>
    /// Уничтожение машины. Когда её здоровье падает до нуля, водителя моментально
    /// выбрасывает, на месте корпуса играется взрыв, выпадает лут, а сама машина
    /// исчезает. Всё опционально: без префаба взрыва и без лута машина просто
    /// скрывается — но исчезает всегда, чтобы уничтоженная машина не продолжала
    /// ездить и стрелять.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CarController))]
    [RequireComponent(typeof(Health))]
    public class CarDestruction : MonoBehaviour
    {
        [Header("Взрыв")]
        [Tooltip("Префаб взрыва (Assets/Prefabs/Effects/Explosion.prefab). Пусто — без взрыва")]
        [SerializeField] private GameObject explosionPrefab;

        [Header("Водитель")]
        [Tooltip("Выбрасывать водителя из машины при уничтожении")]
        [SerializeField] private bool ejectDriver = true;

        [Header("Лут")]
        [Tooltip("Что выпадает из уничтоженной машины")]
        [SerializeField] private LootEntry[] loot = new LootEntry[0];
        [Tooltip("Разброс лута вокруг машины, м")]
        [SerializeField] private float scatterRadius = 0.7f;

        [Header("Что делать с корпусом")]
        [Tooltip("Скрыть корпус вместо удаления. Скрытая машина остаётся в сцене и не рвёт внешние ссылки")]
        [SerializeField] private bool hideInsteadOfDestroy = true;
        [Tooltip("Задержка перед удалением, с. Только если корпус удаляется, а не скрывается")]
        [SerializeField] private float destroyDelay = 0.05f;

        private CarController car;
        private Health health;
        private bool destroyed;

        private void Awake()
        {
            car = GetComponent<CarController>();
            health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            if (health != null) health.Died += OnDied;
        }

        private void OnDisable()
        {
            if (health != null) health.Died -= OnDied;
        }

        private void OnDied(Health dead)
        {
            if (destroyed) return;
            destroyed = true;

            Vector3 center = transform.position;

            if (ejectDriver && car != null) car.EjectNow();

            SpawnExplosion(center);
            DropLoot(center);

            if (hideInsteadOfDestroy) gameObject.SetActive(false);
            else Destroy(gameObject, Mathf.Max(0f, destroyDelay));
        }

        private void SpawnExplosion(Vector3 position)
        {
            if (explosionPrefab == null) return;

            GameObject source = Instantiate(explosionPrefab, position, Quaternion.identity);

            ExplosionEffect effect = source != null ? source.GetComponent<ExplosionEffect>() : null;
            if (effect != null) effect.Ignore(gameObject);
        }

        private void DropLoot(Vector3 center)
        {
            if (loot == null || loot.Length == 0) return;

            for (int i = 0; i < loot.Length; i++)
            {
                LootEntry entry = loot[i];
                if (entry == null || entry.prefab == null) continue;
                if (Random.value > entry.chance) continue;

                int min = Mathf.Min(entry.countMin, entry.countMax);
                int max = Mathf.Max(entry.countMin, entry.countMax);
                int amount = Random.Range(min, max + 1);

                for (int n = 0; n < amount; n++) SpawnLootPiece(entry.prefab, center);
            }
        }

        private void SpawnLootPiece(GameObject prefab, Vector3 center)
        {
            Vector2 offset = Random.insideUnitCircle * scatterRadius;
            GameObject piece = Instantiate(prefab, center + new Vector3(offset.x, 0.35f, offset.y), Random.rotation);
            if (piece == null) return;

            Rigidbody pieceBody = piece.GetComponentInChildren<Rigidbody>();
            if (pieceBody == null) return;

            pieceBody.AddForce(new Vector3(offset.x, 1.4f, offset.y) * 1.6f, ForceMode.Impulse);
            pieceBody.AddTorque(Random.insideUnitSphere * 4f, ForceMode.Impulse);
        }

        [System.Serializable]
        public class LootEntry
        {
            [Tooltip("Префаб выпадающего предмета")]
            public GameObject prefab;
            [Tooltip("Шанс выпадения, 0..1. 1 — всегда")]
            [Range(0f, 1f)] public float chance = 1f;
            [Tooltip("Сколько выпадает, минимум")]
            public int countMin = 1;
            [Tooltip("Сколько выпадает, максимум")]
            public int countMax = 1;
        }
    }
}
