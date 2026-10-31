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
                StartCoroutine(DeleteAfterDelay(toDelete, Mathf.Max(0f, deleteDelay)));
                return;
            }

            PlayWhiff();
        }

        private IEnumerator DeleteAfterDelay(GameObject target, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
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
