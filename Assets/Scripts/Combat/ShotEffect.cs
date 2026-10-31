using System.Collections;
using UnityEngine;

namespace CarGame.Combat
{
    /// <summary>
    /// Эффект выстрела. Один и тот же эффект ставится в двух местах: в точку
    /// попадания и у ствола. Разница только в направлении — в точке попадания
    /// эффект смотрит туда, откуда прилетел выстрел, у ствола по направлению
    /// выстрела.
    ///
    /// В точку выстрела встаёт точка Placement: эффект как бы примагничивается к
    /// ней, поэтому части эффекта не важно, вложены они в корень или в якорь.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShotEffect : MonoBehaviour
    {
        private const float FallbackLifeTime = 0.2f;

        [Tooltip("Точка, которой эффект примагничивается к месту выстрела: она оказывается ровно в точке попадания, её локальное смещение сохраняется")]
        [SerializeField] private Transform placement;
        [Tooltip("Точка «перед» эффекта. Задаёт, в какую сторону эффект повёрнут: её вперёд смотрит туда, откуда пришёл выстрел. Крутится по всем осям свободно")]
        [SerializeField] private Transform front;
        [Tooltip("Имя пустого объекта внутри эффекта, если Placement не задан вручную")]
        [SerializeField] private string placementName = "EffectAnchor";
        [Tooltip("Сколько живёт эффект попадания, с")]
        [SerializeField] private float lifeTime = 0.2f;
        [Tooltip("Сколько живёт эффект у ствола, с. Если 0 — столько же, сколько эффект попадания")]
        [SerializeField] private float muzzleLifeTime = 0.08f;
        [Tooltip("Запускать таймер жизни сразу, если эффект стоит в сцене, а не создан выстрелом")]
        [SerializeField] private bool playOnStart;

        private Coroutine lifetimeRoutine;

        private static readonly string[] KnownPlacementNames = { "EffectAnchor", "Placement" };
        private static readonly string[] KnownFrontNames = { "Front" };

        private void Awake()
        {
            if (placement == null) placement = FindPlacement();
            if (front == null) front = FindFront();
        }

        private void Start()
        {
            if (playOnStart) BeginLifetime(false);
        }

        /// <summary>
        /// Ставит эффект в точку выстрела: корень префаба оказывается ровно в point
        /// и смотрит по forward, всё остальное содержимое префаба едет вместе с ним.
        /// atMuzzle выбирает короткий срок жизни для эффекта у ствола.
        /// </summary>
        public static ShotEffect Spawn(GameObject prefab, Vector3 point, Vector3 forward, bool atMuzzle = false)
        {
            if (prefab == null) return null;

            Quaternion rotation = LookAlong(forward);
            GameObject instance = Instantiate(prefab, point, rotation);

            ShotEffect effect = instance.GetComponent<ShotEffect>();
            if (effect == null)
            {
                Debug.LogWarning($"[ShotEffect] На префабе '{prefab.name}' нет компонента ShotEffect, он удалится по общему таймеру", prefab);

                Destroy(instance, FallbackLifeTime);
                return null;
            }

            Place(instance.transform, effect.placement, effect.front, point, rotation);

            effect.BeginLifetime(atMuzzle);
            return effect;
        }

        /// <summary>
        /// Ставит эффект в точку выстрела двумя точками: Placement «примагничивается»
        /// к месту попадания, а Front задаёт поворот — её вперёд смотрит туда, откуда
        /// пришёл выстрел, и она свободно крутится по всем осям. Корню выставляется
        /// такая поза, чтобы обе точки легли точно куда надо, поэтому части эффекта
        /// не остаются там, где был префаб.
        /// </summary>
        private static void Place(Transform root, Transform placement, Transform front, Vector3 point, Quaternion rotation)
        {
            Transform facing = front != null ? front : placement;
            if (facing == null) facing = root;

            Quaternion rootRotation = rotation * Quaternion.Inverse(facing.localRotation);
            if (placement == null) placement = root;

            root.SetPositionAndRotation(point - rootRotation * placement.localPosition, rootRotation);
        }

        private Transform FindFront()
        {
            for (int i = 0; i < KnownFrontNames.Length; i++)
            {
                Transform found = transform.Find(KnownFrontNames[i]);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Перезапускает отсчёт жизни. Вызывается сразу после установки эффекта,
        /// поэтому таймер всегда начинается заново и не наследуется от Awake.
        /// </summary>
        public void BeginLifetime(bool atMuzzle)
        {
            if (lifetimeRoutine != null) StopCoroutine(lifetimeRoutine);

            float duration = lifeTime;
            if (atMuzzle && muzzleLifeTime > 0f) duration = muzzleLifeTime;

            lifetimeRoutine = StartCoroutine(Lifetime(Mathf.Max(0.01f, duration)));
        }

        private IEnumerator Lifetime(float duration)
        {
            yield return new WaitForSeconds(duration);
            Destroy(gameObject);
        }

        private Transform FindPlacement()
        {
            if (!string.IsNullOrEmpty(placementName))
            {
                Transform named = transform.Find(placementName);
                if (named != null) return named;
            }

            for (int i = 0; i < KnownPlacementNames.Length; i++)
            {
                Transform known = transform.Find(KnownPlacementNames[i]);
                if (known != null) return known;
            }

            return transform;
        }

        private static Quaternion LookAlong(Vector3 forward)
        {
            if (forward.sqrMagnitude < 0.0001f) return Quaternion.identity;

            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forward.normalized, up)) > 0.999f) up = Vector3.forward;

            return Quaternion.LookRotation(forward.normalized, up);
        }
    }
}
