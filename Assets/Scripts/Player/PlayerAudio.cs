using CarGame.Audio;
using CarGame.Combat;
using UnityEngine;

namespace CarGame.Player
{
    /// <summary>
    /// Звуки игрока: прыжок, получение урона и шаги, привязанные к поверхности под ногами.
    /// Источник 2D — игрок должен слышать только себя, так же как остальные его звуки.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAudio : MonoBehaviour
    {
        /// <summary>
        /// Набор шагов для одной поверхности. Поверхность определяется по FootstepSurface
        /// на полу, затем по PhysicMaterial, затем по имени объекта. Регистр не важен.
        /// </summary>
        [System.Serializable]
        public class SurfaceSteps
        {
            [Tooltip("Имя поверхности, например Concrete, Metal, Grass, Wood")]
            public string surface = "Concrete";

            [Tooltip("Шаги для этой поверхности. Пусто — используются общие шаги")]
            public AudioClip[] clips = new AudioClip[0];

            [Range(0f, 1f)]
            [Tooltip("Громкость шагов по этой поверхности")]
            public float volume = 1f;

            [Range(0f, 1f)]
            [Tooltip("Крошевость шага. 0 — гладкий бетон/асфальт, 1 — гравий/земля. Действует, только если клипов нет")]
            public float grit = 0.35f;

            [Range(40f, 600f)]
            [Tooltip("Тон тела шага, Гц. Низкий — глухой удар по бетону, высокий — звонкий металл. Действует, только если клипов нет")]
            public float tone = 118f;
        }

        [Header("Источник")]
        [SerializeField] private AudioSource source;
        [Range(0f, 1f)]
        [Tooltip("Общая громкость звуков игрока")]
        [SerializeField] private float volume = 1f;

        [Header("Прыжок и урон")]
        [Tooltip("Звук прыжка. Пусто — генерируется процедурно")]
        [SerializeField] private AudioClip jumpClip;
        [Tooltip("Звук получения урона. Пусто — генерируется процедурно")]
        [SerializeField] private AudioClip damageClip;

        [Header("Шаги")]
        [Tooltip("Вкл — шаги различаются по поверхности под ногами (Surface Steps). " +
                "Выкл — всегда играется дефолтный звук ходьбы независимо от поверхности")]
        [SerializeField] private bool dynamicStepSound = false;
        [Tooltip("Шаги по умолчанию, если для поверхности не задано своих")]
        [SerializeField] private AudioClip[] defaultStepClips = new AudioClip[0];
        [SerializeField] private SurfaceSteps[] surfaces = new SurfaceSteps[0];
        [Tooltip("Длина шага, м. На каждые столько пройденных метров звучит шаг")]
        [SerializeField] private float stepDistance = 1.9f;
        [Tooltip("С какой горизонтальной скорости шаги уже звучат, м/с")]
        [SerializeField] private float stepMinSpeed = 0.6f;
        [Range(0f, 1f)]
        [SerializeField] private float stepVolume = 0.7f;
        [Header("Шаги без клипов")]
        [Range(0f, 1f)]
        [Tooltip("Крошевость шага для поверхностей без настроенного entry")]
        [SerializeField] private float defaultGrit = 0.35f;
        [Range(40f, 600f)]
        [Tooltip("Тон тела шага для поверхностей без настроенного entry, Гц")]
        [SerializeField] private float defaultTone = 118f;
        [Tooltip("На сколько ниже ног искать пол, м")]
        [SerializeField] private float groundProbe = 0.4f;
        [Tooltip("Слои, по которым определяется поверхность")]
        [SerializeField] private LayerMask groundMask = ~0;

        private class GeneratedStep
        {
            public float grit;
            public float tone;
            public AudioClip clip;
        }

        private PlayerController controller;
        private CharacterController body;
        private Health health;
        private Transform cachedTransform;
        private Vector3 lastStepPosition;
        private AudioClip generatedJump;
        private AudioClip generatedDamage;
        private AudioClip generatedDefaultStep;

        private readonly System.Collections.Generic.List<GeneratedStep> generatedSteps = new System.Collections.Generic.List<GeneratedStep>();

        private void Awake()
        {
            cachedTransform = transform;

            controller = GetComponent<PlayerController>();
            body = GetComponent<CharacterController>();
            health = GetComponent<Health>();

            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;

            lastStepPosition = cachedTransform.position;

            if (controller != null) controller.Jumped += OnJumped;
            if (health != null) health.Damaged += OnDamaged;
        }

        private void OnDestroy()
        {
            if (controller != null) controller.Jumped -= OnJumped;
            if (health != null) health.Damaged -= OnDamaged;
        }

        /// <summary>
        /// Источник обязан быть живым и не заглушённым: после рестартов сцены
        /// AudioSource мог остаться выключенным или «выключенным» по объёму —
        /// шаги при этом молчали, хотя код их проигрывал.
        /// </summary>
        private void EnsureSource()
        {
            if (source == null || source.Equals(null))
                source = GetComponent<AudioSource>();
            if (source == null)
                source = gameObject.AddComponent<AudioSource>();

            if (!source.playOnAwake) source.playOnAwake = false;
            source.spatialBlend = 0f;

            if (!source.enabled) source.enabled = true;
            if (source.mute) source.mute = false;
            if (source.volume <= 0.001f) source.volume = 1f;
        }

        private void Update()
        {
            if (controller != null && controller.IsInCar) return;

            // Источник мог быть пересобран/перехвачен после рестарта забега —
            // иначе PlayOneShot молчит и шаги «не работают».
            EnsureSource();

            Vector3 position = cachedTransform.position;
            float speed = HorizontalSpeed();

            if (speed < stepMinSpeed || !TouchesGround())
            {
                // Стоя на месте шаги не повторяем, но счётчик сбрасываем, чтобы после
                // остановки первый шаг не проиграл сразу нескольких метров подряд.
                if (speed < stepMinSpeed) lastStepPosition = position;
                return;
            }

            float travelled = Vector3.Distance(position, lastStepPosition);
            if (travelled < stepDistance) return;

            lastStepPosition = position;
            PlayStep();
        }

        private float HorizontalSpeed()
        {
            if (controller == null) return 0f;
            return Mathf.Abs(controller.Speed);
        }

        private bool TouchesGround()
        {
            // CharacterController.isGrounded — самый надёжный признак: он
            // учитывает SkinWidth и шаг по склонам. Raycast ниже ног часто
            // промахивался (ноги внутри капсулы/сквозь пол), и шаги молчали
            // «во всех режимах» — именно этот баг и чиним.
            if (body != null && !body.Equals(null) && body.enabled && body.isGrounded)
                return true;

            Vector3 origin = cachedTransform.position + Vector3.up * 0.15f;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbe, groundMask, QueryTriggerInteraction.Ignore))
                return false;

            return hit.collider != null && hit.collider.GetComponentInParent<PlayerController>() == null;
        }

        private void PlayStep()
        {
            // Динамичные шаги по поверхностям — только если DynamicStepSound включён.
            // Выключен — всегда слышен дефолтный звук ходьбы независимо от поверхности.
            if (dynamicStepSound)
            {
                string surface = ResolveSurfaceName();
                AudioClip dynamic = PickStepClip(surface);
                if (dynamic != null)
                {
                    source.PlayOneShot(dynamic, volume * stepVolume * ResolveSurfaceVolume(surface));
                    return;
                }
            }

            AudioClip clip = FirstUsableDefaultClip();
            if (clip == null)
            {
                // В сцене в defaultStepClips мог лежать пустой слот
                // ({fileID: 0}) — раньше из-за этого шаги молчали вообще.
                // Генерируем процедурный шаг один раз и играем его.
                if (generatedDefaultStep == null)
                    generatedDefaultStep = ProceduralAudio.Footstep(defaultGrit, defaultTone);
                clip = generatedDefaultStep;
            }

            if (clip == null) return;

            source.PlayOneShot(clip, volume * stepVolume);
        }

        /// <summary>
        /// Первый реально загруженный клип из дефолтных шагов. Пустые слоты и
        /// битые ссылки (сброшенные ассеты) пропускаются.
        /// </summary>
        private AudioClip FirstUsableDefaultClip()
        {
            if (defaultStepClips == null || defaultStepClips.Length == 0) return null;

            int start = Random.Range(0, defaultStepClips.Length);
            for (int i = 0; i < defaultStepClips.Length; i++)
            {
                AudioClip clip = defaultStepClips[(start + i) % defaultStepClips.Length];
                if (clip != null && !clip.Equals(null)) return clip;
            }

            return null;
        }

        /// <summary>
        /// Клипы поверхности, иначе общие клипы, иначе процедурный шаг с характером
        /// этой поверхности. Так звук различается по полу даже без единого ассета.
        /// </summary>
        private AudioClip PickStepClip(string surface)
        {
            AudioClip[] clips = null;

            if (!string.IsNullOrEmpty(surface))
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    SurfaceSteps entry = surfaces[i];
                    if (entry == null) continue;
                    if (!Matches(entry.surface, surface)) continue;

                    if (entry.clips != null && entry.clips.Length > 0) clips = entry.clips;
                    break;
                }
            }

            if (clips == null || clips.Length == 0) clips = defaultStepClips;

            if (clips != null && clips.Length > 0)
            {
                AudioClip clip = clips[Random.Range(0, clips.Length)];
                if (clip != null) return clip;
            }

            return GetGeneratedStep(ResolveSurfaceGrit(surface), ResolveSurfaceTone(surface));
        }

        /// <summary>
        /// Процедурный клип зависит от пары grit/tone, поэтому кешируем по ним:
        /// иначе на каждом шаге создавался бы новый AudioClip.
        /// </summary>
        private AudioClip GetGeneratedStep(float grit, float tone)
        {
            for (int i = 0; i < generatedSteps.Count; i++)
            {
                GeneratedStep entry = generatedSteps[i];
                if (Mathf.Approximately(entry.grit, grit) && Mathf.Approximately(entry.tone, tone))
                    return entry.clip;
            }

            AudioClip clip = ProceduralAudio.Footstep(grit, tone);
            generatedSteps.Add(new GeneratedStep { grit = grit, tone = tone, clip = clip });
            return clip;
        }

        /// <summary>
        /// Поверхность под ногами: явный компонент важнее материала и имени объекта,
        /// потому что на полу обычно лежит несколько разных объектов.
        /// </summary>
        private string ResolveSurfaceName()
        {
            Vector3 origin = cachedTransform.position + Vector3.up * 0.15f;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbe, groundMask, QueryTriggerInteraction.Ignore))
                return string.Empty;

            Collider collider = hit.collider;
            if (collider == null) return string.Empty;

            FootstepSurface marked = collider.GetComponentInParent<FootstepSurface>();
            if (marked != null && !string.IsNullOrEmpty(marked.Surface)) return marked.Surface;

            if (collider.sharedMaterial != null && !string.IsNullOrEmpty(collider.sharedMaterial.name))
                return collider.sharedMaterial.name;

            return collider.gameObject != null ? collider.gameObject.name : string.Empty;
        }

        private float ResolveSurfaceVolume(string surface)
        {
            if (string.IsNullOrEmpty(surface)) return 1f;

            for (int i = 0; i < surfaces.Length; i++)
            {
                SurfaceSteps entry = surfaces[i];
                if (entry == null) continue;
                if (Matches(entry.surface, surface)) return entry.volume;
            }

            return 1f;
        }

        private float ResolveSurfaceGrit(string surface)
        {
            float grit = defaultGrit;

            if (!string.IsNullOrEmpty(surface))
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    SurfaceSteps entry = surfaces[i];
                    if (entry == null) continue;
                    if (!Matches(entry.surface, surface)) continue;

                    grit = entry.grit;
                    break;
                }
            }

            return Mathf.Clamp01(grit);
        }

        private float ResolveSurfaceTone(string surface)
        {
            float tone = defaultTone;

            if (!string.IsNullOrEmpty(surface))
            {
                for (int i = 0; i < surfaces.Length; i++)
                {
                    SurfaceSteps entry = surfaces[i];
                    if (entry == null) continue;
                    if (!Matches(entry.surface, surface)) continue;

                    tone = entry.tone;
                    break;
                }
            }

            return Mathf.Clamp(tone, 40f, 600f);
        }

        private static bool Matches(string configured, string actual)
        {
            if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(actual)) return false;

            return string.Equals(configured.Trim(), actual.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        private void OnJumped()
        {
            if (source == null) return;

            AudioClip clip = jumpClip;
            if (clip == null)
            {
                if (generatedJump == null) generatedJump = ProceduralAudio.Jump();
                clip = generatedJump;
            }

            source.PlayOneShot(clip, volume);
        }

        private void OnDamaged(Health damaged, DamageInfo info)
        {
            if (source == null) return;

            AudioClip clip = damageClip;
            if (clip == null)
            {
                if (generatedDamage == null) generatedDamage = ProceduralAudio.Hurt();
                clip = generatedDamage;
            }

            float scale = damaged != null && damaged.Max > 0.01f
                ? Mathf.Clamp(info.amount / damaged.Max, 0.3f, 1.5f)
                : 1f;

            source.PlayOneShot(clip, volume * scale);
        }
    }
}