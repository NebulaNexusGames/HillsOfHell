using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Компонент летящего куба, выброшенного игроком (LMB) из руки или
    /// инвентаря. При столкновении с любым объектом, у которого коллайдер не
    /// включён как триггер, куб исчезает, а спавнящийся моб появляется в точке
    /// удара.
    ///
    /// Поворот моба берётся СТРОГО из самого префаба моба (его настройка в
    /// инспекторе), а не из направления полёта куба: куда игрок кинул куб —
    /// не должно влиять на то, как повёрнут робот. Моб всегда появляется так,
    /// как его задал дизайнер префаба.
    ///
    /// Сам куб при этом уничтожается: он «сгорает» при спавне моба. Вариант
    /// руки игрока при этом не трогается — уничтожается только физический
    /// экземпляр, который был выброшен.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnCubeProjectile : MonoBehaviour
    {
        [SerializeField] private GameObject mobPrefab;
        private bool spawned;
        private Rigidbody body;

        public GameObject MobPrefab => mobPrefab;

        public void SetMob(GameObject prefab)
        {
            mobPrefab = prefab;
        }

        public void SetMobPrefab(GameObject prefab)
        {
            mobPrefab = prefab;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (spawned) return;
            if (mobPrefab == null) return;
            Collider other = collision.collider;
            if (other == null) return;
            if (other.isTrigger) return;

            spawned = true;

            Vector3 pos = transform.position;

            // Поворот — СТРОГО из префаба моба, каким он был задан изначально.
            // Направление полёта куба на ориентацию робота влиять не должно.
            GameObject mob = Instantiate(mobPrefab, pos, mobPrefab.transform.rotation);
            if (mob != null) mob.name = mobPrefab.name;
            Destroy(gameObject);
        }
    }
}
