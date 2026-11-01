using UnityEngine;

namespace CarGame.Items
{
    [DisallowMultipleComponent]
    public class SpawnEggItem : MonoBehaviour
    {
        [SerializeField] private GameObject mobPrefab;

        public GameObject MobPrefab => mobPrefab;

        public void SetMob(GameObject prefab)
        {
            mobPrefab = prefab;
        }

        public void SetMob(GameObject prefab, string id)
        {
            mobPrefab = prefab;
        }
    }
}
