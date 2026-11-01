using CarGame.Combat;
using CarGame.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.Player
{
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public class PlayerRespawn : MonoBehaviour
    {
        [Header("Параметры")]
        [Tooltip("Максимальное здоровье игрока")]
        [SerializeField] private float maxHealth = 100f;
        [Tooltip("Сколько секунд ждать перед возрождением, с")]
        [SerializeField] private float respawnDelay = 1.5f;
        [Tooltip("Чинить ли машину, если игрок погиб в ней")]
        [SerializeField] private bool repairCarOnRespawn = true;

        [Header("Сцена смерти")]
        [Tooltip("Если задано — при гибели игрока грузится эта сцена вместо возрождения. Пусто — обычное возрождение")]
        [SerializeField] private string deathSceneName = "DeathScreen";

        [Header("Точка возрождения")]
        [Tooltip("Куда возвращать игрока. Если пусто — позиция при старте сцены")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Поднимать ли игрока на землю при возрождении")]
        [SerializeField] private bool dropToGround = true;
        [Tooltip("С какой высоты искать землю, м")]
        [SerializeField] private float groundRayDistance = 60f;

        private Health health;
        private PlayerController player;
        private CarController carAtDeath;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private float respawnAt = -1f;
        private float loadSceneAt = -1f;

        public Health Health => health;
        public bool WaitingForRespawn => respawnAt > 0f;
        private bool HasDeathScene => !string.IsNullOrWhiteSpace(deathSceneName);

        private void Awake()
        {
            health = GetComponent<Health>();
            if (health == null) health = gameObject.AddComponent<Health>();
            health.Configure(maxHealth, Health.HpOwner.Player);

            player = GetComponent<PlayerController>();
            if (player == null) player = GetComponentInParent<PlayerController>();

            if (spawnPoint == null)
            {
                startPosition = transform.position;
                startRotation = transform.rotation;
            }

            if (health != null) health.Died += OnDied;
        }

        private void Update()
        {
            if (loadSceneAt >= 0f && Time.time >= loadSceneAt)
            {
                loadSceneAt = -1f;
                SceneManager.LoadScene(deathSceneName);
                return;
            }

            if (respawnAt < 0f || Time.time < respawnAt) return;

            respawnAt = -1f;
            Respawn();
        }

        private void OnDied(Health dead)
        {
            carAtDeath = player != null && player.CurrentVehicle != null ? player.CurrentVehicle : null;
            if (carAtDeath != null) carAtDeath.ForceExit();

            if (player != null) player.SetControlEnabled(false);

            if (HasDeathScene)
                loadSceneAt = Time.time + Mathf.Max(0f, respawnDelay);
            else
                respawnAt = Time.time + Mathf.Max(0f, respawnDelay);
        }

        public void Respawn()
        {
            Vector3 position = spawnPoint != null ? spawnPoint.position : startPosition;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : startRotation;
            position = ResolveGround(position);

            CharacterController body = player != null ? player.Body : GetComponent<CharacterController>();
            if (body != null) body.enabled = false;

            transform.SetPositionAndRotation(position, rotation);

            if (body != null) body.enabled = true;
            if (player != null) player.SetControlEnabled(true);
            if (health != null) health.ResetToFull();

            if (repairCarOnRespawn && carAtDeath != null && carAtDeath.Health != null)
                carAtDeath.Health.ResetToFull();

            carAtDeath = null;
        }

        private Vector3 ResolveGround(Vector3 position)
        {
            if (!dropToGround) return position;

            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, groundRayDistance, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            return position;
        }
    }
}
