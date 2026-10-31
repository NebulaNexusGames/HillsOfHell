using System;
using System.Collections.Generic;
using CarGame.Items;
using UnityEngine;

namespace CarGame.Combat
{
    public struct DamageInfo
    {
        public float amount;
        public GameObject source;
        public Vector3 point;
        public Vector3 direction;

        public DamageInfo(float amount, GameObject source, Vector3 point, Vector3 direction)
        {
            this.amount = amount;
            this.source = source;
            this.point = point;
            this.direction = direction;
        }
    }

    public interface IDamageable
    {
        Health Health { get; }
    }

    /// <summary>
    /// Приёмник урона для объектов без собственного запаса прочности
    /// (например, двери): получает удар, но не теряет и не показывает ХП.
    /// </summary>
    public interface IDamageReceiver
    {
        void ReceiveDamage(DamageInfo info);
    }

    [DisallowMultipleComponent]
    public class Health : MonoBehaviour, IDamageable
    {
        public enum HpOwner
        {
            [InspectorName("Игрок")] Player = 0,
            [InspectorName("Машина")] Car = 1,
            [InspectorName("Враг")] Enemy = 2,
            [InspectorName("Постройка")] Structure = 3,
            [InspectorName("Расходник")] Consumable = 4
        }

        /// <summary>
        /// Кусок оверхила: запас хп сверх базы и его цвет на полосе здоровья.
        /// Кусок живёт, пока игрок не потеряет эти хп или пока оверхил не
        /// истечёт сам.
        /// </summary>
        public struct OverhealSegment
        {
            public float amount;
            public Color color;
        }

        [Header("Прочность")]
        [Tooltip("Максимальное здоровье")]
        [SerializeField] private float maxHealth = 100f;
        [Tooltip("Чьё это здоровье. Нужно, чтобы ключ и другие скрипты однозначно понимали, куда бить и что чинить")]
        [SerializeField] private HpOwner owner = HpOwner.Enemy;
        [Tooltip("Не получать урон после смерти")]
        [SerializeField] private bool safeWhenDead = true;

        [Header("Починка")]
        [Tooltip("FixOne: постройку можно чинить только одним металлом. Любой другой металл — как его отсутствие: удар ключом наносит урон, а не лечит")]
        [SerializeField] private bool fixOne;
        [Tooltip("Металл, которым чинится постройка при включённом FixOne")]
        [SerializeField] private MetalType fixOneMetal = MetalType.Scrap;

        [Header("Оверхил")]
        [Tooltip("Сколько оверхила (сверх базы) можно накопить максимум. 0 = без ограничений")]
        [SerializeField] private float maxOverheal;
        [Tooltip("С какой скоростью оверхил утекает сам, хп/сек. База при этом не теряется")]
        [SerializeField] private float overhealDecayPerSecond = 1f;

        /// <summary>Цвета новых кусков оверхила. Каждый следующий кусок — свой цвет.</summary>
        private static readonly Color[] OverhealColors =
        {
            new Color(0.30f, 0.65f, 1.00f),
            new Color(0.36f, 0.86f, 0.42f),
            new Color(0.72f, 0.42f, 0.96f),
            new Color(1.00f, 0.62f, 0.20f),
            new Color(1.00f, 0.42f, 0.72f),
            new Color(0.28f, 0.90f, 0.90f),
            new Color(0.95f, 0.90f, 0.30f)
        };

        private readonly List<OverhealSegment> overhealSegments = new List<OverhealSegment>();
        private int overhealColorCursor;
        private bool invulnerable;

        public event Action<Health, DamageInfo> Damaged;
        public event Action<Health, float> Healed;
        public event Action<Health> Died;
        public event Action<Health> Revived;

        public float Max => Mathf.Max(1f, maxHealth);
        public float Current { get; private set; }
        public float Normalized => Mathf.Clamp01(Current / Max);

        /// <summary>Доля базы (0..Max) на полосе при текущем оверхиле.</summary>
        public float BaseNormalized => Mathf.Clamp01(Mathf.Min(Current, Max) / BarCapacity);

        /// <summary>Доля всего запаса (база + оверхил) на полосе.</summary>
        public float TotalNormalized => Mathf.Clamp01(Current / BarCapacity);

        /// <summary>Знаменатель полосы: база или текущий запас, если он больше.</summary>
        public float BarCapacity => Mathf.Max(Max, Current);

        public bool IsDead { get; private set; }
        public bool IsFull => !IsDead && Current >= Max - 0.0001f;

        /// <summary>
        /// Неуязвимость: урон не проходит вовсе. Нужна, пока игрок сидит в машине —
        /// по нему не бьют ни пули, ни взрывы, ни ближний бой.
        /// </summary>
        public bool IsInvulnerable => invulnerable;
        public HpOwner Owner => owner;
        public bool IsPlayer => owner == HpOwner.Player;
        public bool IsCar => owner == HpOwner.Car;
        public bool IsEnemy => owner == HpOwner.Enemy;
        public bool IsStructure => owner == HpOwner.Structure;
        Health IDamageable.Health => this;

        /// <summary>FixOne: чинить только выбранным металлом, остальные не годятся.</summary>
        public bool FixOne => fixOne;
        /// <summary>Металл, которым можно чинить при включённом FixOne.</summary>
        public MetalType FixOneMetal => fixOneMetal;

        /// <summary>Есть ли запас сверх базы.</summary>
        public bool HasOverheal => !IsDead && Current > Max + 0.0001f;

        /// <summary>Весь оверхил сверх базы.</summary>
        public float Overheal => Mathf.Max(0f, Current - Max);

        /// <summary>Куски оверхила в порядке добавления: первый — сразу за базой.</summary>
        public IReadOnlyList<OverhealSegment> OverhealSegments => overhealSegments;

        private void Awake()
        {
            Current = Max;
            overhealSegments.Clear();
        }

        private void Update()
        {
            if (overhealDecayPerSecond <= 0f) return;
            if (IsDead) return;
            if (Current <= Max + 0.0001f) return;

            Current = Mathf.Max(Max, Current - overhealDecayPerSecond * Time.deltaTime);
            TrimOverheal(Current - Max);
        }

        public void Configure(float maximum, HpOwner hpOwner)
        {
            owner = hpOwner;
            Configure(maximum);
        }

        public void SetOwner(HpOwner hpOwner)
        {
            owner = hpOwner;
        }

        /// <summary>
        /// Включает/выключает неуязвимость. Неуязвимый Health не теряет здоровье
        /// от TakeDamage и не умирает — по игроку в машине урон просто не проходит.
        /// </summary>
        public void SetInvulnerable(bool value)
        {
            invulnerable = value;
        }

        public void Configure(float maximum)
        {
            maxHealth = Mathf.Max(1f, maximum);
            ResetTo(maxHealth);
        }

        public void Set(float value)
        {
            bool wasDead = IsDead;
            Current = Mathf.Clamp(value, 0f, Max);
            overhealSegments.Clear();
            IsDead = Current <= 0.0001f;

            if (IsDead == wasDead) return;
            if (IsDead) Died?.Invoke(this);
            else Revived?.Invoke(this);
        }

        public void TakeDamage(DamageInfo info)
        {
            if (info.amount <= 0f) return;
            if (invulnerable)
            {
                // Бессмертные объекты (двери) не теряют ХП, но могут реагировать
                // на урон через отдельный приёмник.
                IDamageReceiver receiver = GetComponentInParent<IDamageReceiver>();
                if (receiver != null) receiver.ReceiveDamage(info);
                return;
            }
            if (IsDead && safeWhenDead) return;

            Current = Mathf.Max(0f, Current - info.amount);
            TrimOverheal(Current - Max);
            Damaged?.Invoke(this, info);

            if (Current > 0.0001f || IsDead) return;

            IsDead = true;
            Died?.Invoke(this);
        }

        public float Heal(float amount)
        {
            return HealInternal(amount, false);
        }

        /// <summary>
        /// Лечение с оверхилом. Сначала база добивается до максимума, а остаток
        /// уходит в новый цветной кусок оверхила. По умолчанию оверхил не ограничен
        /// (<see cref="maxOverheal"/> = 0), поэтому можно накопить сколько угодно.
        /// </summary>
        public float ApplyHeal(float amount, bool allowOverheal)
        {
            return HealInternal(amount, allowOverheal);
        }

        private float HealInternal(float amount, bool allowOverheal)
        {
            if (amount <= 0f) return 0f;

            float before = Current;

            if (!allowOverheal)
            {
                Current = Mathf.Min(Max, Current + amount);
            }
            else
            {
                float baseNeeded = Mathf.Max(0f, Max - Current);
                float usedOnBase = Mathf.Clamp(amount, 0f, baseNeeded);
                Current += usedOnBase;

                float remaining = amount - usedOnBase;
                if (remaining > 0f && Current >= Max - 0.0001f)
                {
                    float capacity = maxOverheal > 0f
                        ? Mathf.Max(0f, maxOverheal - SumOverheal())
                        : float.PositiveInfinity;
                    float add = Mathf.Min(remaining, capacity);
                    if (add > 0f)
                    {
                        Current += add;
                        AddSegment(add);
                    }
                }
            }

            float healed = Current - before;
            if (healed <= 0f) return 0f;

            if (IsDead)
            {
                IsDead = false;
                Revived?.Invoke(this);
            }

            Healed?.Invoke(this, healed);
            return healed;
        }

        public void ResetTo(float value)
        {
            Current = Mathf.Clamp(value, 0f, Max);
            overhealSegments.Clear();
            IsDead = Current <= 0.0001f;
        }

        public void ResetToFull()
        {
            ResetTo(Max);
        }

        private float SumOverheal()
        {
            float total = 0f;
            for (int i = 0; i < overhealSegments.Count; i++)
                total += overhealSegments[i].amount;
            return total;
        }

        /// <summary>Обрезает куски оверхила с конца, чтобы их сумма не превышала запас.</summary>
        private void TrimOverheal(float targetExcess)
        {
            if (targetExcess < 0f) targetExcess = 0f;

            float total = SumOverheal();
            for (int i = overhealSegments.Count - 1; i >= 0 && total > targetExcess; i--)
            {
                OverhealSegment segment = overhealSegments[i];
                float overflow = total - targetExcess;

                if (segment.amount <= overflow)
                {
                    total -= segment.amount;
                    overhealSegments.RemoveAt(i);
                }
                else
                {
                    segment.amount -= overflow;
                    total = targetExcess;
                    overhealSegments[i] = segment;
                }
            }
        }

        private void AddSegment(float amount)
        {
            Color color = OverhealColors[overhealColorCursor % OverhealColors.Length];
            overhealColorCursor++;

            OverhealSegment segment = new OverhealSegment { amount = amount, color = color };
            overhealSegments.Add(segment);
        }
    }
}
