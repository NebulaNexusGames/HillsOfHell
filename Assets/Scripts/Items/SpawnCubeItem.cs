using UnityEngine;

namespace CarGame.Items
{
    /// <summary>
    /// Компонент куба, который превращается в моба при ударе о любой
    /// сплошной коллайдер (см. SpawnCubeProjectile). Содержит ссылку на моба,
    /// которого нужно спавнить: задаётся в префабе куба (Assets/Prefabs/Items)
    /// или через <see cref="SetMob"/> при спавне из терминала.
    ///
    /// Это чистый маркер: физику, Rigidbody и коллайдеры не заботит —
    /// они есть у префаба куба самостоятельно.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnCubeItem : MonoBehaviour
    {
        [SerializeField] private GameObject mobPrefab;

        public GameObject MobPrefab => mobPrefab;

        public void SetMob(GameObject prefab)
        {
            mobPrefab = prefab;
        }

        public void SetMobPrefab(GameObject prefab)
        {
            mobPrefab = prefab;
        }
    }
}
