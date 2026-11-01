using CarGame.Audio;
using UnityEngine;

namespace CarGame.Vehicle
{
    [RequireComponent(typeof(CarController))]
    [DisallowMultipleComponent]
    public class CarAudio : MonoBehaviour
    {
        [Header("РљР»РёРїС‹")]
        [Tooltip("Р—РІСѓРє Р·Р°Р¶РёРіР°РЅРёСЏ РїСЂРё РїРѕСЃР°РґРєРµ. Р•СЃР»Рё РїСѓСЃС‚Рѕ вЂ” РіРµРЅРµСЂРёСЂСѓРµС‚СЃСЏ")]
        [SerializeField] private AudioClip ignitionClip;
        [Tooltip("Р—РІСѓРє РґРІРёРіР°С‚РµР»СЏ (Р·Р°С†РёРєР»РµРЅРЅС‹Р№). Р•СЃР»Рё РїСѓСЃС‚Рѕ вЂ” РіРµРЅРµСЂРёСЂСѓРµС‚СЃСЏ")]
        [SerializeField] private AudioClip engineClip;
        [Tooltip("Р—РІСѓРє РµР·РґС‹ (Р·Р°С†РёРєР»РµРЅРЅС‹Р№). Р•СЃР»Рё РїСѓСЃС‚Рѕ вЂ” РіРµРЅРµСЂРёСЂСѓРµС‚СЃСЏ")]
        [SerializeField] private AudioClip driveClip;

        [Header("Р—РІСѓРє РїСЂРё РіР»РѕС…РЅСѓС‰РµРј РјРѕС‚РѕСЂРµ")]
        [Tooltip("РљР»РёРї, РїСЂРѕРёРіСЂС‹РІР°РµРјС‹Р№ РІ РјРѕРјРµРЅС‚, РєРѕРіРґР° С‚РѕРїР»РёРІРѕ Р·Р°РєРѕРЅС‡РёР»РѕСЃСЊ. Р•СЃР»Рё РїСѓСЃС‚Рѕ вЂ” РіРµРЅРµСЂРёСЂСѓРµС‚СЃСЏ")]
        [SerializeField] private AudioClip engineStallClip;
        [Tooltip("Р“СЂРѕРјРєРѕСЃС‚СЊ Р·РІСѓРєР° РіР»РѕС…РЅСѓС‰РµРіРѕ РјРѕС‚РѕСЂР°")]
        [SerializeField] private float stallVolume = 0.9f;

        [Header("Р“СЂРѕРјРєРѕСЃС‚СЊ")]
        [Tooltip("Р“СЂРѕРјРєРѕСЃС‚СЊ РґРІРёРіР°С‚РµР»СЏ РЅР° С…РѕР»РѕСЃС‚С‹С… Рё РЅР° РіР°Р·Сѓ")]
        [SerializeField] private float engineVolume = 0.4f;
        [Tooltip("Р“СЂРѕРјРєРѕСЃС‚СЊ Р·РІСѓРєР° РµР·РґС‹ РЅР° РјР°РєСЃРёРјР°Р»СЊРЅРѕР№ СЃРєРѕСЂРѕСЃС‚Рё")]
        [SerializeField] private float driveVolume = 0.45f;
        [Tooltip("РќР°СЃРєРѕР»СЊРєРѕ Р·РІСѓРє РїСЂРёРІСЏР·Р°РЅ Рє РїРѕР·РёС†РёРё РІ РїСЂРѕСЃС‚СЂР°РЅСЃС‚РІРµ. 0 вЂ” РІРµР·РґРµ, 1 вЂ” С‚РѕС‡РЅРѕ РІ С‚РѕС‡РєРµ")]
        [SerializeField] private float spatialBlend = 0.7f;
        [Tooltip("РњР°РєСЃРёРјР°Р»СЊРЅР°СЏ СЃР»С‹С€РёРјРѕСЃС‚СЊ РґРІРёРіР°С‚РµР»СЏ, Рј")]
        [SerializeField] private float maxDistance = 60f;

        [Header("Тон двигателя")]
        [Tooltip("Р’С‹СЃРѕС‚Р° Р·РІСѓРєР° РґРІРёРіР°С‚РµР»СЏ РЅР° С…РѕР»РѕСЃС‚С‹С…")]
        [SerializeField] private float idlePitch = 0.75f;
        [Tooltip("Р’С‹СЃРѕС‚Р° Р·РІСѓРєР° РґРІРёРіР°С‚РµР»СЏ РЅР° РјР°РєСЃРёРјР°Р»СЊРЅС‹С… РѕР±РѕСЂРѕС‚Р°С…")]
        [SerializeField] private float maxPitch = 2.1f;
        [Tooltip("РЎРєРѕСЂРѕСЃС‚СЊ, РїСЂРё РєРѕС‚РѕСЂРѕР№ РґРІРёРіР°С‚РµР»СЊ СЃС‡РёС‚Р°РµС‚СЃСЏ РЅР° РјР°РєСЃРёРјР°Р»СЊРЅС‹С… РѕР±РѕСЂРѕС‚Р°С…, Рј/СЃ")]
        [SerializeField] private float pitchSpeedReference = 24f;
        [Tooltip("РЎ РєР°РєРѕР№ СЃРєРѕСЂРѕСЃС‚Рё РІРєР»СЋС‡Р°РµС‚СЃСЏ Р·РІСѓРє РµР·РґС‹, Рј/СЃ")]
        [SerializeField] private float driveStartSpeed = 1.2f;
        [Tooltip("РЎРєРѕСЂРѕСЃС‚СЊ, РїСЂРё РєРѕС‚РѕСЂРѕР№ Р·РІСѓРє РµР·РґС‹ РІС‹С…РѕРґРёС‚ РЅР° РїРѕР»РЅСѓСЋ РіСЂРѕРјРєРѕСЃС‚СЊ, Рј/СЃ")]
        [SerializeField] private float driveFullSpeed = 22f;

        private CarController carController;
        private AudioSource ignitionSource;
        private AudioSource engineSource;
        private AudioSource driveSource;
        private bool wasOccupied;
        private bool running;
        private AudioClip generatedIgnition;
        private AudioClip generatedEngine;
        private AudioClip generatedDrive;
        private AudioClip generatedStall;
        private bool stallPlayed;

        private void Awake()
        {
            carController = GetComponent<CarController>();
            ignitionSource = CreateSource("Ignition", false);
            engineSource = CreateSource("Engine", true);
            driveSource = CreateSource("Drive", true);
        }

        private void OnEnable()
        {
            wasOccupied = false;
            running = false;
            stallPlayed = false;
        }

        private void Update()
        {
            if (carController == null) return;

            bool occupied = carController.IsOccupied;
            bool hasFuel = carController.HasFuel;

            if (occupied && hasFuel && !running)
            {
                stallPlayed = false;
                StartEngine();
            }
            else if ((!occupied || !hasFuel) && running)
            {
                if (!hasFuel && occupied) PlayStall();
                StopEngine();
            }

            wasOccupied = occupied;
            if (!running) return;

            UpdateEngineTone();
            UpdateDrive();
        }

        public void StartEngine()
        {
            ignitionSource.PlayOneShot(GetIgnitionClip(), 0.9f);
            running = true;

            engineSource.clip = GetEngineClip();
            engineSource.loop = true;
            engineSource.pitch = idlePitch;
            engineSource.volume = engineVolume;
            if (!engineSource.isPlaying) engineSource.Play();
        }

        public void StopEngine()
        {
            running = false;
            engineSource.Stop();
            driveSource.Stop();
        }

        private void PlayStall()
        {
            if (stallPlayed) return;
            stallPlayed = true;
            AudioClip clip = engineStallClip != null ? engineStallClip : (generatedStall ?? (generatedStall = GenerateStall()));
            if (clip != null) ignitionSource.PlayOneShot(clip, stallVolume);
        }

        private AudioClip GenerateStall()
        {
            try
            {
                return ProceduralAudio.Ignition();
            }
            catch
            {
                return null;
            }
        }

        private void UpdateEngineTone()
        {
            float speed = Mathf.Abs(carController.SpeedMs);
            float load = Mathf.Clamp01(speed / Mathf.Max(1f, pitchSpeedReference));
            float target = Mathf.Lerp(idlePitch, maxPitch, load);
            engineSource.pitch = Mathf.MoveTowards(engineSource.pitch, target, 6f * Time.deltaTime);
            engineSource.volume = engineVolume * Mathf.Lerp(0.75f, 1f, load);
        }

        private void UpdateDrive()
        {
            float speed = Mathf.Abs(carController.SpeedMs);
            if (speed < driveStartSpeed)
            {
                if (driveSource.isPlaying) driveSource.Stop();
                return;
            }

            if (!driveSource.isPlaying)
            {
                driveSource.clip = GetDriveClip();
                driveSource.loop = true;
                driveSource.Play();
            }

            float load = Mathf.InverseLerp(driveStartSpeed, Mathf.Max(driveStartSpeed + 0.01f, driveFullSpeed), speed);
            driveSource.volume = driveVolume * load;
            driveSource.pitch = Mathf.Lerp(0.9f, 1.25f, load);
        }

        private AudioSource CreateSource(string sourceName, bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0.4f;
            return source;
        }

        private AudioClip GetIgnitionClip()
        {
            if (ignitionClip == null && generatedIgnition == null) generatedIgnition = ProceduralAudio.Ignition();
            return ignitionClip != null ? ignitionClip : generatedIgnition;
        }

        private AudioClip GetEngineClip()
        {
            if (engineClip == null && generatedEngine == null) generatedEngine = ProceduralAudio.EngineLoop();
            return engineClip != null ? engineClip : generatedEngine;
        }

        private AudioClip GetDriveClip()
        {
            if (driveClip == null && generatedDrive == null) generatedDrive = ProceduralAudio.DriveLoop();
            return driveClip != null ? driveClip : generatedDrive;
        }
    }
}
