using System.Collections.Generic;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Combat
{
    /// <summary>
    /// Эффект взрыва. Ставится как готовый объект со своими триггерами.
    ///
    /// Эпицентр наносит урон один раз, пока зона досягаемости не перекрыла его размер.
    /// Зона расширяется и бьёт всё время, пока расширяется. После достижения
    /// максимума оба триггера плавно становятся прозрачными, затем объект удаляется.
    ///
    /// Зоны растут масштабом своего GameObject, поэтому коллайдер и модель
    /// увеличиваются вместе. Радиус коллайдера в скрипте не меняется.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExplosionEffect : MonoBehaviour
    {
        private enum Phase
        {
            Expanding,
            Fading,
            Lingering,
            Done
        }

        [Header("Триггеры")]
        [Tooltip("Триггер эпицентра. Бьёт один раз, пока зона досягаемости не перекрыла его размер")]
        [SerializeField] private SphereCollider epicenterTrigger;
        [Tooltip("Триггер зоны досягаемости. Скрипт расширяет его GameObject от размера эпицентра до максимума")]
        [SerializeField] private SphereCollider reachTrigger;

        [Header("Размеры зон")]
        [Tooltip("Радиус эпицентра, м. Он же стартовый размер зоны досягаемости")]
        [SerializeField] private float epicenterRadius = 1.5f;
        [Tooltip("Максимальный радиус зоны досягаемости, м")]
        [SerializeField] private float reachMaxRadius = 6f;
        [Tooltip("С какой скоростью расширяется зона досягаемости, м/с")]
        [SerializeField] private float expandSpeed = 9f;

        [Header("Урон")]
        [Tooltip("Урон в эпицентре")]
        [SerializeField] private float maxDamage = 100f;
        [Tooltip("Урон на границе максимальной зоны")]
        [SerializeField] private float minDamage = 10f;
        [Tooltip("Как часто зона досягаемости наносит урон, с")]
        [SerializeField] private float damageInterval = 0.25f;
        [Tooltip("Не наносить урон игроку, пока он в машине")]
        [SerializeField] private bool protectPlayerInCar = true;
        [Tooltip("Слои, которые получают урон и отбрасываются")]
        [SerializeField] private LayerMask affectedMask = ~0;

        [Header("Отбрасывание")]
        [Tooltip("Сила импульса в эпицентре")]
        [SerializeField] private float knockbackForce = 18f;
        [Tooltip("Подъёмная часть импульса. 0 — только горизонтально, 1 — сильно вверх")]
        [SerializeField] private float knockbackLift = 0.7f;
        [Tooltip("Предельная скорость после отбрасывания, м/с")]
        [SerializeField] private float maxKnockbackSpeed = 26f;

        [Header("Исчезновение")]
        [Tooltip("Сколько триггеры становятся прозрачными после расширения, с")]
        [SerializeField] private float fadeDuration = 0.6f;
        [Tooltip("Сколько ждать после полной прозрачности перед удалением, с")]
        [SerializeField] private float lingerDuration = 0.25f;
        [Tooltip("Рендереры, которые гаснут. Пусто — берутся автоматически из триггеров")]
        [SerializeField] private Renderer[] fadeRenderers = new Renderer[0];

        [Header("Звук")]
        [Tooltip("Звуковой эффект взрыва. Пусто — используется процедурный звук")]
        [SerializeField] private AudioClip explosionSound;
        [Tooltip("Играть звук взрыва при старте")]
        [SerializeField] private bool playSound = true;
        [SerializeField] private float soundVolume = 1f;

        private AudioSource soundSource;
        private Transform ignored;

        private Vector3 epicenterBaseScale;
        private Vector3 reachBaseScale;
        private float epicenterBaseRadius;
        private float reachBaseRadius;

        private readonly HashSet<Health> epicenterHits = new HashSet<Health>();
        private readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

        private readonly List<Material> fadeClones = new List<Material>();

        private Phase phase = Phase.Expanding;
        private float reachRadius;
        private float damageTimer;
        private float phaseTimer;
        private bool built;

        public bool IsFinished => phase == Phase.Done;

        /// <summary>
        /// Исключает объект из урона и отбрасывания. Вызывается сразу после Instantiate,
        /// чтобы взорвавшийся робот не получил урон от собственного взрыва.
        /// </summary>
        public void Ignore(GameObject source)
        {
            if (source != null) ignored = source.transform;
        }

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            if (built) return;
            built = true;

            reachRadius = Mathf.Max(0.05f, epicenterRadius);
            damageTimer = 0f;

            CaptureBase();

            ApplyRadius(epicenterRadius);
            ApplyRadius(reachRadius);

            PrepareFade();

            soundSource = gameObject.AddComponent<AudioSource>();
            soundSource.playOnAwake = false;
            soundSource.spatialBlend = 1f;
            soundSource.rolloffMode = AudioRolloffMode.Linear;
            soundSource.maxDistance = 60f;
            soundSource.minDistance = 6f;
            soundSource.volume = soundVolume;

            PlaySound();
        }

        private void CaptureBase()
        {
            epicenterBaseScale = epicenterTrigger != null ? epicenterTrigger.transform.localScale : Vector3.one;
            reachBaseScale = reachTrigger != null ? reachTrigger.transform.localScale : Vector3.one;

            epicenterBaseRadius = WorldRadius(epicenterTrigger);
            reachBaseRadius = WorldRadius(reachTrigger);
        }

        private static float WorldRadius(SphereCollider trigger)
        {
            if (trigger == null) return 0.5f;

            Vector3 scale = trigger.transform.lossyScale;
            float max = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            return Mathf.Max(0.001f, trigger.radius * max);
        }

        /// <summary>
        /// Растягивает зону масштабом её GameObject. Коллайдер и всё, что внутри
        /// этого объекта, включая модель, увеличиваются вместе.
        /// </summary>
        private void ApplyRadius(float radius)
        {
            ScaleTo(epicenterTrigger, epicenterBaseScale, epicenterBaseRadius, epicenterRadius);
            ScaleTo(reachTrigger, reachBaseScale, reachBaseRadius, radius);
        }

        private static void ScaleTo(SphereCollider trigger, Vector3 baseScale, float baseRadius, float radius)
        {
            if (trigger == null) return;

            trigger.transform.localScale = baseScale * (radius / Mathf.Max(0.001f, baseRadius));
        }

        private void Update()
        {
            if (phase == Phase.Done) return;

            switch (phase)
            {
                case Phase.Expanding: TickExpand(); break;
                case Phase.Fading: TickFade(); break;
                case Phase.Lingering: TickLinger(); break;
            }
        }

        private void TickExpand()
        {
            bool expanded = false;

            if (reachRadius < reachMaxRadius)
            {
                reachRadius = Mathf.Min(reachMaxRadius, reachRadius + expandSpeed * Time.deltaTime);
                ApplyRadius(reachRadius);

                if (reachRadius >= reachMaxRadius) expanded = true;
            }
            else
            {
                expanded = true;
            }

            if (reachRadius <= epicenterRadius) DamageEpicenter();

            DamageReach();

            if (expanded) BeginFade();
        }

        private void DamageEpicenter()
        {
            float radius = WorldRadius(epicenterTrigger);
            Collider[] hits = Physics.OverlapSphere(transform.position, radius, affectedMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;
                if (IsSelf(hit.transform)) continue;

                Health health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;
                if (!epicenterHits.Add(health)) continue;
                if (protectPlayerInCar && IsPlayerInCar(health)) continue;

                Vector3 point = hit.ClosestPoint(transform.position);
                health.TakeDamage(new DamageInfo(maxDamage, null, point, Direction(point)));
            }

            ApplyKnockbackOnce(radius);
        }

        private void DamageReach()
        {
            damageTimer -= Time.deltaTime;
            if (damageTimer > 0f) return;

            damageTimer = Mathf.Max(0.02f, damageInterval);

            float radius = WorldRadius(reachTrigger);
            Collider[] hits = Physics.OverlapSphere(transform.position, radius, affectedMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;
                if (IsSelf(hit.transform)) continue;

                Health health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;
                if (protectPlayerInCar && IsPlayerInCar(health)) continue;

                Vector3 point = hit.ClosestPoint(transform.position);
                float distance = Vector3.Distance(point, transform.position);
                float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, radius));

                float amount = Mathf.Lerp(minDamage, maxDamage, falloff);
                health.TakeDamage(new DamageInfo(amount, null, point, Direction(point)));
            }
        }

        private void ApplyKnockbackOnce(float radius)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, radius, affectedMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;

                Rigidbody body = hit.attachedRigidbody;
                if (body == null || body.isKinematic) continue;
                if (IsSelf(body.transform)) continue;
                if (!pushed.Add(body)) continue;

                Vector3 direction = body.worldCenterOfMass - transform.position;
                direction.y += knockbackLift;

                if (direction.sqrMagnitude < 0.0001f) continue;

                body.AddForce(direction.normalized * knockbackForce, ForceMode.Impulse);

                Vector3 velocity = body.linearVelocity;
                if (velocity.sqrMagnitude > maxKnockbackSpeed * maxKnockbackSpeed)
                    body.linearVelocity = velocity.normalized * maxKnockbackSpeed;
            }
        }

        private bool IsSelf(Transform candidate)
        {
            if (candidate == null) return false;
            if (candidate == transform || candidate.IsChildOf(transform)) return true;
            if (ignored == null) return false;
            return candidate == ignored || candidate.IsChildOf(ignored);
        }

        private Vector3 Direction(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            return direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;
        }

        private void BeginFade()
        {
            phase = Phase.Fading;
            phaseTimer = Mathf.Max(0f, fadeDuration);
        }

        private void TickFade()
        {
            phaseTimer -= Time.deltaTime;

            float alpha = fadeDuration <= 0.001f ? 0f : Mathf.Clamp01(phaseTimer / fadeDuration);
            ApplyFade(alpha);

            if (phaseTimer > 0f) return;

            ApplyFade(0f);

            if (epicenterTrigger != null) epicenterTrigger.enabled = false;
            if (reachTrigger != null) reachTrigger.enabled = false;

            phase = Phase.Lingering;
            phaseTimer = Mathf.Max(0f, lingerDuration);
        }

        private void TickLinger()
        {
            phaseTimer -= Time.deltaTime;
            if (phaseTimer > 0f) return;

            phase = Phase.Done;

            foreach (Material clone in fadeClones)
            {
                if (clone != null) Destroy(clone);
            }

            Destroy(gameObject);
        }

        private void PrepareFade()
        {
            Renderer[] targets = fadeRenderers != null && fadeRenderers.Length > 0
                ? fadeRenderers
                : CollectRenderers();

            for (int i = 0; i < targets.Length; i++)
            {
                Renderer target = targets[i];
                if (target == null) continue;

                Material[] materials = target.sharedMaterials;
                Material[] clones = new Material[materials.Length];

                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == null)
                    {
                        clones[m] = null;
                        continue;
                    }

                    Material clone = new Material(materials[m]);
                    clone.hideFlags = HideFlags.HideAndDontSave;

                    MakeTransparent(clone);
                    clones[m] = clone;
                    fadeClones.Add(clone);
                }

                target.sharedMaterials = clones;
            }
        }

        private Renderer[] CollectRenderers()
        {
            List<Renderer> found = new List<Renderer>();

            if (epicenterTrigger != null) found.AddRange(epicenterTrigger.GetComponentsInChildren<Renderer>(true));
            if (reachTrigger != null) found.AddRange(reachTrigger.GetComponentsInChildren<Renderer>(true));

            return found.ToArray();
        }

        private void ApplyFade(float alpha)
        {
            for (int i = 0; i < fadeClones.Count; i++)
            {
                Material clone = fadeClones[i];
                if (clone == null) continue;

                if (!clone.HasProperty("_BaseColor") && !clone.HasProperty("_Color")) continue;

                string property = clone.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Color color = clone.GetColor(property);
                color.a = Mathf.Clamp01(alpha);

                clone.SetColor("_BaseColor", color);
                clone.SetColor("_Color", color);
            }
        }

        private static void MakeTransparent(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
        }

        private static bool IsPlayerInCar(Health health)
        {
            if (!health.IsPlayer) return false;

            PlayerController controller = health.GetComponentInParent<PlayerController>();
            return controller != null && controller.IsInCar;
        }

        private void PlaySound()
        {
            if (!playSound || soundSource == null) return;

            AudioClip clip = explosionSound != null ? explosionSound : Explosion.GetClip();
            if (clip == null) return;

            soundSource.clip = clip;
            soundSource.Play();
        }
    }
}
