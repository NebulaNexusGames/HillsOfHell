using CarGame.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarGame.Player
{
    /// <summary>
    /// Грузит сцену смерти, когда игрок погибает. Живёт в игровой сцене рядом
    /// с игроком и подписывается на его здоровье.
    ///
    /// Автовозрождение (PlayerRespawn) на время отключается: иначе оно
    /// «оживило» бы игрока раньше, чем загрузится экран смерти. Задержка даёт
    /// секунду-другую на то, чтобы смерть успела отыграть, и только потом
    /// открывается сцена смерти.
    ///
    /// Добавляется инструментом Tools/CarGame/Menus/Add Death Scene Loader.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeathSceneLoader : MonoBehaviour
    {
        [Tooltip("Сцена смерти, которая грузится при гибели игрока")]
        [SerializeField] private string deathSceneName = "DeathScreen";
        [Tooltip("Пауза перед загрузкой сцены смерти, с")]
        [SerializeField] private float delay = 2f;
        [Tooltip("Отключать автовозрождение, пока грузится сцена смерти")]
        [SerializeField] private bool disableAutoRespawn = true;

        private Health health;
        private float loadAt = -1f;

        private void Awake()
        {
            health = GetComponentInParent<Health>();
            if (health != null && !health.IsPlayer) health = null;
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
            if (string.IsNullOrEmpty(deathSceneName))
            {
                Debug.LogWarning("[DeathSceneLoader] Не задано имя сцены смерти.", this);
                return;
            }

            if (disableAutoRespawn)
            {
                PlayerRespawn respawn = GetComponentInParent<PlayerRespawn>();
                if (respawn != null) respawn.enabled = false;
            }

            loadAt = Time.time + Mathf.Max(0f, delay);
        }

        private void Update()
        {
            if (loadAt < 0f || Time.time < loadAt) return;
            loadAt = -1f;
            SceneManager.LoadScene(deathSceneName);
        }
    }
}
