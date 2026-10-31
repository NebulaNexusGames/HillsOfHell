using UnityEngine;

namespace CarGame.Audio
{
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        public static AudioClip Impact(bool heavy)
        {
            float duration = heavy ? 0.34f : 0.17f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            float decay = heavy ? 10f : 19f;
            float baseFrequency = heavy ? 105f : 205f;
            float[] partials = heavy ? new[] { 1f, 2.41f, 3.83f, 5.17f } : new[] { 1f, 1.93f, 2.87f, 4.31f };
            float[] gains = heavy ? new[] { 1f, 0.7f, 0.45f, 0.28f } : new[] { 0.8f, 0.6f, 0.4f, 0.24f };

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-decay * t);

                float tone = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    float frequency = baseFrequency * partials[p];
                    tone += gains[p] * Mathf.Sin(2f * Mathf.PI * frequency * t);
                }

                float thud = Mathf.Sin(2f * Mathf.PI * 52f * t) * 0.55f * Mathf.Exp(-13f * t);
                float click = Random.value * 0.7f * Mathf.Exp(-48f * t);
                data[i] = (tone * 0.35f + thud + click) * envelope;
            }

            FadeEdges(data, 0.0025f);
            return Build("ImpactGenerated", data);
        }

        public static AudioClip Whiff()
        {
            float duration = 0.26f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] data = new float[count];
            float lowpassed = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / duration;
                float envelope = Mathf.Sin(Mathf.PI * progress);
                float frequency = Mathf.Lerp(320f, 1900f, progress);

                float noise = Random.value;
                lowpassed += 0.25f * (noise - lowpassed);

                float carrier = Mathf.Sin(2f * Mathf.PI * frequency * t);
                data[i] = (carrier * 0.55f + (noise - lowpassed) * 1.7f) * envelope;
            }

            FadeEdges(data, 0.004f);
            Normalize(data, 0.5f);
            return Build("WhiffGenerated", data);
        }

        public static AudioClip EngineLoop()
        {
            int count = SampleRate;
            float fundamental = 50f;
            float[] gains = { 1f, 0.6f, 0.45f, 0.32f, 0.24f, 0.17f, 0.12f, 0.08f };
            float[] data = new float[count];

            for (int h = 1; h <= gains.Length; h++)
            {
                float frequency = fundamental * h;
                float gain = gains[h - 1];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += gain * Saw(2f * Mathf.PI * frequency * t);
                }
            }

            AddPeriodicNoise(data, 2f, 10, 240f, 2800f, 0.16f);
            data = LowPass(data, 2200f);
            Normalize(data, 0.55f);
            return Build("EngineLoopGenerated", data);
        }

        public static AudioClip DriveLoop()
        {
            int count = SampleRate * 2;
            float[] data = new float[count];

            AddPeriodicNoise(data, 2f, 22, 35f, 950f, 1f);
            data = LowPass(data, 1400f);

            float[] rumble = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                rumble[i] = Saw(2f * Mathf.PI * 28f * t) * 0.35f + Saw(2f * Mathf.PI * 56f * t) * 0.2f;
            }

            for (int i = 0; i < count; i++) data[i] = data[i] * 0.8f + rumble[i];

            data = LowPass(data, 900f);
            Normalize(data, 0.5f);
            return Build("DriveLoopGenerated", data);
        }

        public static AudioClip Ignition()
        {
            int count = Mathf.CeilToInt(0.85f * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / 0.85f;
                float frequency = Mathf.Lerp(48f, 128f, progress * progress);
                float starter = Saw(2f * Mathf.PI * frequency * t) * 0.5f * (1f - progress * 0.55f);
                float catchHit = progress > 0.62f ? (1f - (progress - 0.62f) / 0.38f) : 0f;
                float hit = catchHit * (Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.5f + Random.value * 0.4f);
                data[i] = starter + hit * 0.8f;
            }

            FadeEdges(data, 0.01f);
            Normalize(data, 0.6f);
            return Build("IgnitionGenerated", data);
        }

        public static AudioClip Explosion()
        {
            float duration = 1.1f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / duration;

                float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(85f, 26f, progress) * t) * 0.85f;
                float crack = Random.value * 2f - 1f;

                data[i] = (body + crack * 1.15f) * Mathf.Exp(-3.4f * t);
            }

            data = LowPass(data, 3400f);
            FadeEdges(data, 0.004f);
            Normalize(data, 0.85f);
            return Build("ExplosionGenerated", data);
        }

        public static AudioClip Gunshot(bool heavy)
        {
            float duration = heavy ? 0.24f : 0.1f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float decay = heavy ? 16f : 42f;

                float tone = Mathf.Sin(2f * Mathf.PI * (heavy ? 118f : 265f) * t) * 0.6f;
                float crack = (Random.value * 2f - 1f) * 1.2f;

                data[i] = (tone + crack) * Mathf.Exp(-decay * t);
            }

            data = LowPass(data, heavy ? 2600f : 5200f);
            FadeEdges(data, 0.002f);
            Normalize(data, heavy ? 0.75f : 0.5f);
            return Build(heavy ? "GunshotHeavyGenerated" : "GunshotGenerated", data);
        }

        public static AudioClip Laser()
        {
            float duration = 0.7f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / duration;

                float frequency = Mathf.Lerp(1750f, 240f, progress);
                float carrier = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.8f;
                float overtone = Mathf.Sin(2f * Mathf.PI * frequency * 2.51f * t) * 0.3f;
                float shimmer = (Random.value * 2f - 1f) * 0.35f;

                data[i] = (carrier + overtone + shimmer) * Mathf.Exp(-4.6f * t);
            }

            data = LowPass(data, 5200f);
            FadeEdges(data, 0.003f);
            Normalize(data, 0.6f);
            return Build("LaserGenerated", data);
        }

        /// <summary>
        /// Гул терминала под панель спавна: ровно двухсекундный бесшовный цикл.
        ///
        /// Бесшовность здесь не украшение, а требование: цикл крутится без
        /// остановки, пока открыта панель, и любой щелчок на стыке повторялся бы
        /// два раза в секунду. Поэтому все частоты — и партиалы, и шум, и прилив —
        /// прижаты к кратным 1/длительности (0.5 Гц при двух секундах): за петлю
        /// укладывается целое число колебаний, и последний отсчёт стыкуется с
        /// первым без разрыва. Сглаживание круговое (SmoothCircular): обычный
        /// LowPass на стыке петли щёлкает.
        /// </summary>
        public static AudioClip TerminalLoop()
        {
            float duration = 2f;
            int count = SampleRate * 2;
            float[] data = new float[count];

            float unit = 1f / duration;
            float[] partials = { 110f, 165f, 220f, 330f, 495f };
            float[] gains = { 1f, 0.45f, 0.3f, 0.16f, 0.08f };

            for (int p = 0; p < partials.Length; p++)
            {
                float frequency = Mathf.Round(partials[p] / unit) * unit;
                float gain = gains[p];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += gain * Mathf.Sin(2f * Mathf.PI * frequency * t);
                }
            }

            // Медленный прилив: целое число циклов на петлю — тоже условие
            // бесшовности, иначе громкость прыгнула бы ровно на стыке.
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float swell = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t);
                data[i] *= Mathf.Lerp(0.7f, 1f, swell);
            }

            AddPeriodicNoise(data, duration, 6, 700f, 2400f, 0.12f);
            data = SmoothCircular(data, 2, 3);
            Normalize(data, 0.45f);
            return Build("TerminalLoopGenerated", data);
        }

        /// <summary>
        /// Гул магазина под панель терминала: ещё один двухсекундный бесшовный
        /// цикл, но светлее терминального — мажорный аккорд G (G-B-D) против
        /// тёмного гудения. Требования те же, что у TerminalLoop: каждая частота
        /// кратна 1/длительности, цикл обрабатывается круговым сглаживанием, а
        /// прилив укладывается целым числом колебаний на петлю.
        /// </summary>
        public static AudioClip ShopLoop()
        {
            float duration = 2f;
            int count = SampleRate * 2;
            float[] data = new float[count];

            float unit = 1f / duration;
            float[] partials = { 196f, 246.94f, 293.66f, 392f, 587.33f };
            float[] gains = { 1f, 0.5f, 0.34f, 0.2f, 0.1f };

            for (int p = 0; p < partials.Length; p++)
            {
                float frequency = Mathf.Round(partials[p] / unit) * unit;
                float gain = gains[p];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += gain * Mathf.Sin(2f * Mathf.PI * frequency * t);
                }
            }

            // Мягкий прилив в четверть такта, медленнее, чем у терминала.
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float swell = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t * 0.5f);
                data[i] *= Mathf.Lerp(0.72f, 1f, swell);
            }

            AddPeriodicNoise(data, duration, 8, 900f, 3400f, 0.08f);
            data = SmoothCircular(data, 2, 3);
            Normalize(data, 0.4f);
            return Build("ShopLoopGenerated", data);
        }

        /// <summary>
        /// Гул под панель терминала-обменника: тот же двухсекундный бесшовный
        /// цикл, но минорный (ля-минор) — обмен и переработка звучат серьёзнее
        /// магазина. Все частоты кратны 1/длительности, сглаживание круговое,
        /// поэтому стык петли не щёлкает.
        /// </summary>
        public static AudioClip ExchangeLoop()
        {
            float duration = 2f;
            int count = SampleRate * 2;
            float[] data = new float[count];

            float unit = 1f / duration;
            float[] partials = { 220f, 261.63f, 329.63f, 440f, 659.26f };
            float[] gains = { 1f, 0.5f, 0.4f, 0.18f, 0.08f };

            for (int p = 0; p < partials.Length; p++)
            {
                float frequency = Mathf.Round(partials[p] / unit) * unit;
                float gain = gains[p];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += gain * Mathf.Sin(2f * Mathf.PI * frequency * t);
                }
            }

            // Мягкий прилив в четверть такта, как у магазина.
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float swell = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t * 0.5f);
                data[i] *= Mathf.Lerp(0.72f, 1f, swell);
            }

            AddPeriodicNoise(data, duration, 8, 700f, 2800f, 0.07f);
            data = SmoothCircular(data, 2, 3);
            Normalize(data, 0.42f);
            return Build("ExchangeLoopGenerated", data);
        }

        /// <summary>
        /// Пополнение депозита: короткий светлый звон монеты — растущий и сразу
        /// затухающий. Играется в BoltDepositor при вкладе Golden Bolt.
        /// </summary>
        public static AudioClip Deposit()
        {
            float duration = 0.18f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-16f * t);
                float ring = Mathf.Sin(2f * Mathf.PI * 1180f * t) * 0.5f
                           + Mathf.Sin(2f * Mathf.PI * 1770f * t) * 0.3f;
                float shimmer = (Random.value * 2f - 1f) * 0.25f * Mathf.Exp(-40f * t);
                data[i] = (ring + shimmer) * envelope;
            }

            FadeEdges(data, 0.003f);
            Normalize(data, 0.55f);
            return Build("DepositGenerated", data);
        }

        /// <summary>
        /// Рычаг автомата: сухой металлический щелчок со взводом пружины.
        /// Играется в момент дёргания рычага, до вращения барабанов.
        /// </summary>
        public static AudioClip SlotMachineLever()
        {
            float duration = 0.16f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float clank = Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.55f * Mathf.Exp(-22f * t);
                float spring = Mathf.Sin(2f * Mathf.PI * 240f * t) * 0.3f * Mathf.Exp(-30f * t);
                float ratt = (Random.value * 2f - 1f) * 0.4f * Mathf.Exp(-45f * t);
                data[i] = clank + spring + ratt;
            }

            FadeEdges(data, 0.002f);
            Normalize(data, 0.6f);
            return Build("SlotMachineLeverGenerated", data);
        }

        /// <summary>
        /// Вращение барабанов автомата: лёгкий треск с металлом. Ровно
        /// двухсекундный бесшовный цикл — барабаны крутятся, пока панель на
        /// экране, и щелчок на стыке повторялся бы каждые две секунды. Поэтому
        /// все партиалы и шумовые голоса кратны 1/длительности, а сглаживание
        /// круговое (SmoothCircular), как у TerminalLoop.
        /// </summary>
        public static AudioClip SlotMachineSpin()
        {
            float duration = 2f;
            int count = SampleRate * 2;
            float[] data = new float[count];

            float unit = 1f / duration;
            float[] partials = { 95f, 190f, 285f, 470f };
            float[] gains = { 1f, 0.45f, 0.2f, 0.12f };

            for (int p = 0; p < partials.Length; p++)
            {
                float frequency = Mathf.Round(partials[p] / unit) * unit;
                float gain = gains[p];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += gain * Saw(2f * Mathf.PI * frequency * t);
                }
            }

            AddPeriodicNoise(data, duration, 9, 500f, 2200f, 0.1f);
            data = SmoothCircular(data, 3, 2);
            Normalize(data, 0.5f);
            return Build("SlotMachineSpinGenerated", data);
        }

        /// <summary>
        /// Джекпот: короткая мажорная фанфара-арпеджио вверх со звенящим
        /// хвостом. Играется, когда автомат вернул ставку и бонус болтами.
        /// </summary>
        public static AudioClip SlotMachineJackpot()
        {
            float duration = 0.7f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];
            float[] notes = { 261.63f, 329.63f, 392f, 523.25f, 659.25f };

            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * 0.1f;
                float note = notes[n];
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)SampleRate - start;
                    if (t < 0f) continue;

                    float envelope = Mathf.Exp(-9f * t);
                    float tone = Mathf.Sin(2f * Mathf.PI * note * t) * 0.5f
                               + Mathf.Sin(2f * Mathf.PI * note * 2f * t) * 0.22f;
                    data[i] += tone * envelope;
                }
            }

            Normalize(data, 0.6f);
            return Build("SlotMachineJackpotGenerated", data);
        }

        /// <summary>
        /// Удвоение ставки: светлый мажорный перезвон, короче и тише джекпота.
        /// </summary>
        public static AudioClip SlotMachineWin()
        {
            float duration = 0.45f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];
            float[] notes = { 523.25f, 659.25f, 783.99f };

            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * 0.08f;
                float note = notes[n];
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)SampleRate - start;
                    if (t < 0f) continue;

                    float envelope = Mathf.Exp(-12f * t);
                    data[i] += Mathf.Sin(2f * Mathf.PI * note * t) * 0.45f * envelope;
                }
            }

            FadeEdges(data, 0.003f);
            Normalize(data, 0.55f);
            return Build("SlotMachineWinGenerated", data);
        }

        /// <summary>
        /// Проигрыш: плавно сползающий вниз низкий тон с лёгким дребезгом.
        /// </summary>
        public static AudioClip SlotMachineLose()
        {
            float duration = 0.55f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = Mathf.Clamp01(t / duration);
                float frequency = Mathf.Lerp(220f, 82f, progress);
                float envelope = Mathf.Exp(-2.6f * t);
                float wob = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.5f;
                float buzz = Mathf.Sin(2f * Mathf.PI * frequency * 0.5f * t) * 0.25f;
                data[i] = (wob + buzz) * envelope;
            }

            FadeEdges(data, 0.004f);
            Normalize(data, 0.55f);
            return Build("SlotMachineLoseGenerated", data);
        }

        /// <summary>
        /// Тик обратного отсчёта аннигиляции: короткий острый сигнал, всё
        /// чаще по мере приближения взрыва.
        /// </summary>
        public static AudioClip SlotMachineTick()
        {
            float duration = 0.09f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-30f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.5f
                           + Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.25f;
                data[i] = tone * envelope;
            }

            FadeEdges(data, 0.002f);
            Normalize(data, 0.6f);
            return Build("SlotMachineTickGenerated", data);
        }

        /// <summary>
        /// Тряска светотряса: сухой дребезг корпуса с несколькими щелчками,
        /// будто внутри перекатывается грузик. Играется на каждый клик ЛКМ и
        /// повторяется, пока фонарик трясут зажатой кнопкой, поэтому клип
        /// короткий и не «наезжает» на себя.
        /// </summary>
        public static AudioClip ShakeRattle()
        {
            float duration = 0.24f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];
            float[] clickTimes = { 0.004f, 0.062f, 0.128f, 0.186f };

            float lowpassed = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;

                float noise = Random.value * 2f - 1f;
                lowpassed += 0.4f * (noise - lowpassed);
                float rattle = (noise - lowpassed) * 1.3f * Mathf.Exp(-7f * t);

                float clicks = 0f;
                for (int c = 0; c < clickTimes.Length; c++)
                {
                    float since = t - clickTimes[c];
                    if (since < 0f) continue;
                    clicks += Mathf.Sin(2f * Mathf.PI * 168f * since) * Mathf.Exp(-24f * since);
                }

                data[i] = rattle + clicks * 0.75f;
            }

            FadeEdges(data, 0.003f);
            Normalize(data, 0.55f);
            return Build("ShakeRattleGenerated", data);
        }

        public static AudioClip DoorOpen()
        {
            float duration = 0.55f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] data = new float[count];
            float lowpassed = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float progress = t / duration;
                float envelope = Mathf.Sin(Mathf.PI * progress);

                float frequency = Mathf.Lerp(180f, 430f, progress);
                float squeak = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float wobble = 0.55f + 0.35f * Mathf.Sin(2f * Mathf.PI * 7f * t);

                float noise = Random.value;
                lowpassed += 0.08f * (noise - lowpassed);
                float creak = (noise - lowpassed) * 0.8f;

                data[i] = (squeak * wobble * 0.5f + creak) * envelope;
            }

            FadeEdges(data, 0.01f);
            Normalize(data, 0.6f);
            return Build("DoorOpenGenerated", data);
        }

        public static AudioClip DoorClose()
        {
            float duration = 0.3f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float thud = Mathf.Sin(2f * Mathf.PI * 92f * t) * Mathf.Exp(-18f * t);
                float click = (Random.value * 2f - 1f) * 0.55f * Mathf.Exp(-60f * t);
                float rattle = Mathf.Sin(2f * Mathf.PI * 214f * t) * 0.22f * Mathf.Exp(-26f * t);
                data[i] = thud + click + rattle;
            }

            FadeEdges(data, 0.002f);
            Normalize(data, 0.7f);
            return Build("DoorCloseGenerated", data);
        }

        private static AudioClip Build(string clipName, float[] data)
        {
            AudioClip clip = AudioClip.Create(clipName, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Saw(float phase)
        {
            float wrapped = Mathf.Repeat(phase, 2f * Mathf.PI) / (2f * Mathf.PI);
            return wrapped * 2f - 1f;
        }

        private static void AddPeriodicNoise(float[] data, float duration, int voices, float minFrequency, float maxFrequency, float gain)
        {
            for (int v = 0; v < voices; v++)
            {
                float frequency = Mathf.Round(Random.Range(minFrequency, maxFrequency) * duration) / duration;
                if (frequency < 1f) continue;

                float phase = Random.value * 2f * Mathf.PI;
                float amplitude = gain / Mathf.Sqrt(voices);
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += amplitude * Mathf.Sin(2f * Mathf.PI * frequency * t + phase);
                }
            }
        }

        /// Бесшовный аналог LowPass для зацикленных треков.
        ///
        /// LowPass не годится: он рекуррентный и на стыке петли опирается на
        /// состояние, которого у кольца нет. Несколько прогонов подряд это не
        /// чинит — фильтр сходится к фиксированной точке, но не кольцевой, и
        /// щелчок остаётся (замер: шаг на стыке в 2.3 раза больше худшего
        /// шага внутри петли, то есть щелчок громче самой музыки).
        ///
        /// Здесь индексация по модулю длины, поэтому y[n] тождественно y[0] по
        /// построению, а не по счастливому совпадению. Три прохода окна шириной
        /// 5 дают гауссову форму и срез около 3 кГц — примерно как был задуман
        /// LowPass на 3200 Гц.
        /// </summary>
        private static float[] SmoothCircular(float[] data, int radius, int passes)
        {
            int n = data.Length;
            if (radius < 1 || passes < 1) return data;

            int width = radius * 2 + 1;
            for (int p = 0; p < passes; p++)
            {
                float[] next = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        // Остаток с приведением к положительному: без него
                        // отрицательные k дали бы минус и вылет за массив.
                        int j = (i + k) % n;
                        if (j < 0) j += n;
                        sum += data[j];
                    }

                    next[i] = sum / width;
                }

                data = next;
            }

            return data;
        }

        private static float[] LowPass(float[] data, float cutoff)
        {
            float alpha = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SampleRate);
            float keep = 1f - alpha;
            float[] result = (float[])data.Clone();

            for (int pass = 0; pass < 3; pass++)
            {
                float previous = result[result.Length - 1];
                for (int i = 0; i < result.Length; i++)
                {
                    previous = keep * previous + alpha * result[i];
                    result[i] = previous;
                }
            }

            return result;
        }

        /// <summary>
        /// Шаг по твёрдой поверхности: короткий низкий щелчок с шумом.
        /// </summary>
        public static AudioClip Footstep(float grit = 0.35f, float tone = 118f)
        {
            float duration = 0.11f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            grit = Mathf.Clamp01(grit);
            tone = Mathf.Clamp(tone, 40f, 600f);

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-30f * t);
                float body = Mathf.Sin(2f * Mathf.PI * tone * t) * 0.6f * Mathf.Exp(-16f * t);
                float crunch = (Random.value * 2f - 1f) * grit * Mathf.Exp(-42f * t);
                data[i] = (body + crunch) * envelope;
            }

            FadeEdges(data, 0.002f);
            Normalize(data, 0.55f);

            string name = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "FootstepGenerated_g{0:0.00}_t{1:0}",
                grit,
                tone);

            return Build(name, data);
        }

        /// <summary>
        /// Прыжок: короткий выдох вверх с шлеском одежды в начале.
        /// </summary>
        public static AudioClip Jump()
        {
            float duration = 0.2f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float progress = Mathf.Clamp01(t / duration);
                float envelope = Mathf.Sin(Mathf.PI * Mathf.Min(1f, progress * 1.15f));
                float sweep = Mathf.Lerp(300f, 620f, progress);
                float tone = Mathf.Sin(2f * Mathf.PI * sweep * t) * 0.45f;
                float cloth = (Random.value * 2f - 1f) * 0.35f * Mathf.Exp(-24f * t);
                data[i] = (tone + cloth) * envelope;
            }

            FadeEdges(data, 0.003f);
            Normalize(data, 0.5f);
            return Build("JumpGenerated", data);
        }

        /// <summary>
        /// Реакция на урон: глухой низкий удар.
        /// </summary>
        public static AudioClip Hurt()
        {
            float duration = 0.28f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];

            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-11f * t);
                float low = Mathf.Sin(2f * Mathf.PI * 78f * t) * 0.7f;
                float mid = Mathf.Sin(2f * Mathf.PI * 165f * t) * 0.3f * Mathf.Exp(-18f * t);
                float grit = (Random.value * 2f - 1f) * 0.35f * Mathf.Exp(-30f * t);
                data[i] = (low + mid + grit) * envelope;
            }

            FadeEdges(data, 0.003f);
            Normalize(data, 0.6f);
            return Build("HurtGenerated", data);
        }

        /// <summary>
        /// Удача (три иконки LuckUp): восходящее искристое арпеджио с лёгким
        /// вибрато и высоким «звёздным» отблеском. Светлее и волшебнее обычного
        /// удвоения — исход удачи должен слышаться отдельно от выигрыша.
        /// </summary>
        public static AudioClip SlotMachineLuckUp()
        {
            float duration = 0.6f;
            float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];
            float[] notes = { 587.33f, 739.99f, 880f, 1108.73f, 1174.66f };

            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * 0.07f;
                float note = notes[n];
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)SampleRate - start;
                    if (t < 0f) continue;

                    float envelope = Mathf.Exp(-8f * t);
                    float vibrato = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                    float tone = Mathf.Sin(2f * Mathf.PI * note * vibrato * t) * 0.42f
                               + Mathf.Sin(2f * Mathf.PI * note * 2f * t) * 0.16f;
                    data[i] += tone * envelope;
                }
            }

            // Высокий отблеск поверх арпеджио — «искры» удачи.
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)SampleRate;
                data[i] += Mathf.Sin(2f * Mathf.PI * 2637f * t) * 0.12f * Mathf.Exp(-6f * t);
            }

            FadeEdges(data, 0.004f);
            Normalize(data, 0.55f);
            return Build("SlotMachineLuckUpGenerated", data);
        }

        /// <summary>
        /// Фоновый трек панели автомата: неспешный «лаунж»-аккорд Fmaj7. Ровно
        /// двухсекундный бесшовный цикл, как у TerminalLoop/ShopLoop: все
        /// партиалы кратны 1/длительности, шумовые голоса тоже, а сглаживание
        /// круговое (SmoothCircular), поэтому на стыке петли нет щелчка.
        /// </summary>
        public static AudioClip SlotMachineLoop()
        {
            float duration = 2f;
            int count = SampleRate * 2;
            float[] data = new float[count];

            float unit = 1f / duration;
            float[] partials = { 174.61f, 220f, 261.63f, 329.63f, 349.23f };
            float[] gains = { 1f, 0.55f, 0.38f, 0.22f, 0.12f };

            for (int p = 0; p < partials.Length; p++)
            {
                float frequency = Mathf.Round(partials[p] / unit) * unit;
                float gain = gains[p];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    data[i] += gain * (Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.7f
                                     + Saw(2f * Mathf.PI * frequency * t) * 0.12f);
                }
            }

            // Медленный прилив в половину такта — целое число циклов на петлю.
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float swell = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t * 0.5f);
                data[i] *= Mathf.Lerp(0.75f, 1f, swell);
            }

            AddPeriodicNoise(data, duration, 6, 800f, 3000f, 0.06f);
            data = SmoothCircular(data, 2, 3);
            Normalize(data, 0.42f);
            return Build("SlotMachineLoopGenerated", data);
        }

        private static void Normalize(float[] data, float target)
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak < 0.0001f) return;

            float scale = target / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= scale;
        }

        private static void FadeEdges(float[] data, float edge)
        {
            int count = Mathf.Min(Mathf.CeilToInt(edge * SampleRate), data.Length);
            for (int i = 0; i < count; i++)
            {
                float factor = i / (float)count;
                data[i] *= factor;
                data[data.Length - 1 - i] *= factor;
            }
        }
    }
}
