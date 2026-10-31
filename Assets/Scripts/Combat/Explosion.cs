using System.Collections.Generic;
using CarGame.Audio;
using CarGame.Player;
using UnityEngine;

namespace CarGame.Combat
{
    public static class Explosion
    {
        private static AudioClip cachedExplosion;
        private static Material cachedEffectMaterial;
        private static Mesh cachedParticleMesh;

        public struct Settings
        {
            public float radius;
            public float damage;
            public float minDamage;
            public float knockback;
            public float knockbackLift;
            public float maxKnockbackSpeed;
            public bool killPlayerInstantly;
            public bool protectPlayerInCar;
            public bool spawnEffect;
            public Color effectColor;
            public LayerMask mask;
        }

        public static AudioClip GetClip()
        {
            if (cachedExplosion == null) cachedExplosion = ProceduralAudio.Explosion();
            return cachedExplosion;
        }

        public static Settings Default(float radius, float damage, float knockback)
        {
            return new Settings
            {
                radius = radius,
                damage = damage,
                minDamage = damage * 0.35f,
                knockback = knockback,
                knockbackLift = 0.55f,
                maxKnockbackSpeed = 26f,
                killPlayerInstantly = false,
                protectPlayerInCar = false,
                spawnEffect = true,
                effectColor = new Color(1f, 0.55f, 0.15f),
                mask = ~0
            };
        }

        public static void Detonate(Vector3 center, Settings settings)
        {
            if (settings.spawnEffect) SpawnEffect(center, settings);
            ApplyDamage(center, settings);
            ApplyKnockback(center, settings);
            PlaySound(center, settings.radius);
        }

        private static void ApplyDamage(Vector3 center, Settings settings)
        {
            if (settings.damage <= 0f) return;

            Collider[] hits = Physics.OverlapSphere(center, settings.radius, settings.mask, QueryTriggerInteraction.Ignore);

            HashSet<Health> damaged = new HashSet<Health>();

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;

                Health health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;
                if (!damaged.Add(health)) continue;

                if (settings.protectPlayerInCar && IsPlayerInCar(health)) continue;

                Vector3 point = hit.ClosestPoint(center);
                Vector3 direction = point - center;
                float distance = direction.magnitude;

                float falloff = distance <= 0.001f ? 1f : 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, settings.radius));
                float amount = Mathf.Lerp(settings.minDamage, settings.damage, falloff);

                if (settings.killPlayerInstantly && health.IsPlayer) amount = Mathf.Max(amount, health.Max + 1f);

                health.TakeDamage(new DamageInfo(amount, null, point, direction.normalized));
            }
        }

        private static void ApplyKnockback(Vector3 center, Settings settings)
        {
            if (settings.knockback <= 0f) return;

            Collider[] hits = Physics.OverlapSphere(center, settings.radius, settings.mask, QueryTriggerInteraction.Ignore);

            HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null) continue;

                Rigidbody body = hit.attachedRigidbody;
                if (body == null || body.isKinematic) continue;
                if (!pushed.Add(body)) continue;

                Vector3 direction = body.worldCenterOfMass - center;
                direction.y += settings.knockbackLift;

                if (direction.sqrMagnitude < 0.0001f) continue;

                float distance = Vector3.Distance(body.worldCenterOfMass, center);
                float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, settings.radius));

                body.AddForce(direction.normalized * settings.knockback * falloff, ForceMode.Impulse);

                Vector3 velocity = body.linearVelocity;
                if (velocity.sqrMagnitude > settings.maxKnockbackSpeed * settings.maxKnockbackSpeed)
                    body.linearVelocity = velocity.normalized * settings.maxKnockbackSpeed;
            }
        }

        private static bool IsPlayerInCar(Health health)
        {
            if (!health.IsPlayer) return false;

            PlayerController controller = health.GetComponentInParent<PlayerController>();
            return controller != null && controller.IsInCar;
        }

        private static void SpawnEffect(Vector3 center, Settings settings)
        {
            GameObject source = new GameObject("ExplosionEffect");
            source.transform.position = center;

            ParticleSystem particles = source.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.duration = 0.4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(settings.radius * 1.4f, settings.radius * 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(settings.radius * 0.1f, settings.radius * 0.26f);
            main.startColor = new ParticleSystem.MinMaxGradient(settings.effectColor, Color.white);
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.15f;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)90) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.05f, settings.radius * 0.12f);

            ParticleSystem.ColorOverLifetimeModule colorOverLife = particles.colorOverLifetime;
            colorOverLife.enabled = true;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(settings.effectColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = particles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f));

            ParticleSystemRenderer renderer = source.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Material material = GetEffectMaterial(settings.effectColor);
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    renderer.mesh = GetParticleMesh();
                    renderer.alignment = ParticleSystemRenderSpace.Local;
                }
            }

            particles.Play();

            Object.Destroy(source, 2f);
        }

        private static Material GetEffectMaterial(Color color)
        {
            if (cachedEffectMaterial != null) return cachedEffectMaterial;

            Shader shader = Shader.Find("Universal Render Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            cachedEffectMaterial = new Material(shader);
            cachedEffectMaterial.name = "ExplosionEffectParticle";
            cachedEffectMaterial.hideFlags = HideFlags.HideAndDontSave;

            cachedEffectMaterial.SetColor("_BaseColor", color);
            cachedEffectMaterial.SetColor("_Color", color);

            if (cachedEffectMaterial.HasProperty("_Surface")) cachedEffectMaterial.SetFloat("_Surface", 1f);
            if (cachedEffectMaterial.HasProperty("_Blend")) cachedEffectMaterial.SetFloat("_Blend", 1f);

            cachedEffectMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cachedEffectMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");

            cachedEffectMaterial.renderQueue = 3000;

            return cachedEffectMaterial;
        }

        private static Mesh GetParticleMesh()
        {
            if (cachedParticleMesh != null) return cachedParticleMesh;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cachedParticleMesh = temp.GetComponent<MeshFilter>().sharedMesh;

            if (cachedParticleMesh != null)
            {
                cachedParticleMesh.hideFlags = HideFlags.HideAndDontSave;
                Object.Destroy(temp);
            }

            return cachedParticleMesh;
        }

        /// <summary>
        /// Проигрывает только звук, без визуального эффекта и без урона.
        /// Используется роботами, которые не должны взрываться.
        /// </summary>
        public static void PlayFallbackSound(Vector3 center, float volume)
        {
            AudioClip clip = GetClip();
            if (clip == null) return;

            AudioSource.PlayClipAtPoint(clip, center, Mathf.Clamp01(volume));
        }

        private static void PlaySound(Vector3 center, float radius)
        {
            AudioClip clip = GetClip();
            if (clip == null) return;

            GameObject source = new GameObject("ExplosionAudio");
            source.transform.position = center;

            AudioSource audio = source.AddComponent<AudioSource>();
            audio.clip = clip;
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.maxDistance = Mathf.Max(30f, radius * 8f);
            audio.minDistance = Mathf.Max(4f, radius * 2f);
            audio.volume = 1f;
            audio.Play();

            Object.Destroy(source, clip.length + 0.2f);
        }
    }
}
