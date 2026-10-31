using CarGame.Audio;
using CarGame.Player;
using System.Collections;
using UnityEngine;

namespace CarGame.Items
{
    [DisallowMultipleComponent]
    public class DelHammerItem : MonoBehaviour, IHandUsable
    {
        [Header("Удар")]
        [SerializeField] private float hitRange = 3.5f;
        [SerializeField] private float hitStartOffset = 0.4f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private float swingCooldown = 0.6f;
        [SerializeField] private float deleteDelay = 0.25f;

        [Header("Анимация")]
        [SerializeField] private Animator animator;
        [SerializeField] private string hitTrigger = "Hit";
        [SerializeField] private string idleParameter = "Idle";

        [Header("Удаление по анимации")]
        [Tooltip("Доля клика удара (0..1), на которой засчитывается попадание. " +
                 "Таймер удаления запускается именно в момент замаха, а объект " +
                 "исчезает через deleteDelay после этого момента — так удаление " +
                 "не выглядит ни раньше, ни позже удара молотка")]
        [SerializeField, Range(0f, 1f)] private float hitMomentNormalizedTime = 0.55f;
        [Tooltip("Сколько секунд ждать исчезновения объекта после момента удара в анимации, с")]
        [SerializeField] private float deleteDelayAfterHit = 0.12f;
        [Tooltip("Длительность клика «Hit» в секундах. Нужна, чтобы вычислить " +
                 "момент удара внутри анимации. Берётся из варианта руки " +
                 "автоматически, это значение — запасное")]
        [SerializeField] private float swingClipLength = 1.1667f;

        [Header("Звук")]
        [SerializeField] private AudioClip hitClip;
        [SerializeField] private AudioClip whiffClip;
        [SerializeField] private float volume = 0.9f;
        [SerializeField] private float pitchJitter = 0.1f;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[32];

        private AudioSource audioSource;
        private Animator activeAnimator;
        private PlayerHand currentHand;
        private AudioClip generatedHitClip;
        private AudioClip generatedWhiffClip;
        private float nextSwingTime;
        private bool warnedAboutAnimator;
        private Coroutine pendingDelete;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        public void OnUsePressed(PlayerHand hand)
        {
            currentHand = hand;
            if (!CanUse(hand) || Time.time < nextSwingTime) return;

            ResolveAnimator(hand);
            Swing(hand);
            nextSwingTime = Time.time + swingCooldown;
        }

        public void OnUseHeld(PlayerHand hand)
        {
            currentHand = hand;
        }

        public void OnUseReleased(PlayerHand hand)
        {
            currentHand = hand;
        }

        public void OnUseCanceled()
        {
        }

        public void OnEquipped(PlayerHand hand)
        {
            ResolveAnimator(hand);
            if (activeAnimator != null && !string.IsNullOrEmpty(idleParameter))
                activeAnimator.SetBool(idleParameter, true);
        }

        private bool CanUse(PlayerHand hand)
        {
            if (hand == null) return false;
            if (!hand.IsHolding) return false;
            if (hand.HeldItem == null && hand.HeldFromInventory == null) return false;
            return true;
        }

        private void Swing(PlayerHand hand)
        {
            ResolveAnimator(hand);
            PlaySwingAnimation();

            if (!TryGetAimRay(hand, out Ray ray))
            {
                PlayWhiff();
                return;
            }

            Vector3 origin = ray.GetPoint(hitStartOffset);
            int count = Physics.RaycastNonAlloc(origin, ray.direction, hitBuffer, hitRange, hitMask, QueryTriggerInteraction.Ignore);
            if (count <= 0)
            {
                PlayWhiff();
                return;
            }

            SortByDistance(count);

            for (int i = 0; i < count; i++)
            {
                Collider target = hitBuffer[i].collider;
                if (target == null) continue;
                if (target.GetComponentInParent<PlayerController>() != null) continue;

                GameObject toDelete = target.attachedRigidbody != null ? target.attachedRigidbody.gameObject : target.transform.root.gameObject;
                if (toDelete == null) continue;
                if (toDelete.GetComponentInParent<PlayerController>() != null) continue;

                PlayHit();
                ScheduleDelete(toDelete);
                return;
            }

            PlayWhiff();
        }

        /// <summary>
        /// Ставит удаление в очередь: объект исчезнет не сразу, а после того
        /// как анимация замахa дойдёт до момента удара (hitMoment) плюс
        /// deleteDelayAfterHit. Пока молот только летит вниз, цель стоит на
        /// месте — визуально удар и исчезновение совпадают.
        /// </summary>
        private void ScheduleDelete(GameObject target)
        {
            float hitMoment = GetSwingHitMoment();
            float delay = Mathf.Max(0f, deleteDelay) + hitMoment + Mathf.Max(0f, deleteDelayAfterHit);

            if (pendingDelete != null) StopCoroutine(pendingDelete);
            pendingDelete = StartCoroutine(DeleteAfterDelay(target, delay));
        }

        /// <summary>
        /// Момент удара внутри клика «Hit» в секундах: длительность клика
        /// берётся из состояния Hit активного аниматора, доля — из
        /// <see cref="hitMomentNormalizedTime"/>. Если аниматора нет или клик
        /// не найден — используется запасная длина swingClipLength.
        /// </summary>
        private float GetSwingHitMoment()
        {
            float clipLength = ResolveHitClipLength();
            return clipLength * Mathf.Clamp01(hitMomentNormalizedTime);
        }

        private float ResolveHitClipLength()
        {
            Animator anim = activeAnimator;
            if (anim != null && anim.runtimeAnimatorController != null && !string.IsNullOrEmpty(hitTrigger))
            {
                AnimationClip hitClip = FindClipForState(anim.runtimeAnimatorController, hitTrigger);
                if (hitClip != null && hitClip.length > 0.01f) return hitClip.length;
            }

            return Mathf.Max(0.01f, swingClipLength);
        }

        private static AnimationClip FindClipForState(RuntimeAnimatorController controller, string stateName)
        {
            // В контроллере DelHammer состояние называется так же, как триггер
            // («Hit»), поэтому ищем состояние по имени напрямую среди клипов.
            AnimationClip[] clips = controller.animationClips;
            if (clips == null || clips.Length == 0) return null;

            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip != null && string.Equals(clip.name, stateName, System.StringComparison.OrdinalIgnoreCase))
                    return clip;
            }

            return null;
        }

        private IEnumerator DeleteAfterDelay(GameObject target, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            pendingDelete = null;
            if (target != null)
            {
                if (target.GetComponentInParent<PlayerController>() != null) yield break;
                Destroy(target);
            }
        }

        private void SortByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit current = hitBuffer[i];
                int j = i - 1;
                while (j >= 0 && hitBuffer[j].distance > current.distance)
                {
                    hitBuffer[j + 1] = hitBuffer[j];
                    j--;
                }

                hitBuffer[j + 1] = current;
            }
        }

        private bool TryGetAimRay(PlayerHand hand, out Ray ray)
        {
            if (hand != null && hand.TryGetAimRay(out ray)) return true;

            Camera camera = Camera.main;
            if (camera == null)
            {
                ray = default;
                return false;
            }

            ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        private void ResolveAnimator(PlayerHand hand)
        {
            if (activeAnimator != null) return;

            if (hand != null && hand.ActiveHandVariant != null)
            {
                activeAnimator = hand.ActiveHandVariant.GetComponentInChildren<Animator>();
                if (activeAnimator != null) return;
            }

            if (animator != null)
            {
                activeAnimator = animator;
                return;
            }

            if (warnedAboutAnimator) return;
            warnedAboutAnimator = true;
            Debug.LogWarning($"[DelHammer] У молотка '{name}' не найден аниматор.", this);
        }

        private void PlaySwingAnimation()
        {
            if (activeAnimator == null) return;
            if (!string.IsNullOrEmpty(hitTrigger)) activeAnimator.SetTrigger(hitTrigger);
            if (!string.IsNullOrEmpty(idleParameter)) activeAnimator.SetBool(idleParameter, false);
        }

        private void PlayHit()
        {
            AudioClip clip = hitClip != null ? hitClip : GetGeneratedHitClip();
            PlaySound(clip, 1f, 1f);
        }

        private void PlayWhiff()
        {
            AudioClip clip = whiffClip != null ? whiffClip : GetGeneratedWhiffClip();
            PlaySound(clip, 0.6f, 1f + Random.Range(-0.1f, 0.1f));
        }

        private void PlaySound(AudioClip clip, float gain, float pitchScale)
        {
            if (clip == null) return;
            AudioSource source = ResolveAudioSource();
            if (source == null) return;
            source.pitch = pitchScale * (1f + Random.Range(-pitchJitter, pitchJitter));
            source.PlayOneShot(clip, volume * gain);
        }

        private AudioSource ResolveAudioSource()
        {
            if (currentHand != null && currentHand.SharedAudioSource != null) return currentHand.SharedAudioSource;
            if (audioSource != null && gameObject.activeInHierarchy) return audioSource;
            return audioSource;
        }

        private AudioClip GetGeneratedHitClip()
        {
            if (generatedHitClip == null) generatedHitClip = ProceduralAudio.Impact(true);
            return generatedHitClip;
        }

        private AudioClip GetGeneratedWhiffClip()
        {
            if (generatedWhiffClip == null) generatedWhiffClip = ProceduralAudio.Whiff();
            return generatedWhiffClip;
        }
    }
}
