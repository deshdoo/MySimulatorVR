using UnityEngine;

// Настройки игрока (PlayerPrefs): громкость по категориям; и чем управлять — руками
// или контроллерами (только на сеанс; РежимВводаРига переключает риг XR Origin Hands).
public static class GameSettings
{
    public enum Ввод { Контроллеры = 1, Руки = 2 }

    const string Kгромкость = "vol_master", Kинтерфейс = "vol_ui",
                 Kокружение = "vol_ambient", Kдвигатели = "vol_motors", Kпомощь = "pilot_assist";

    public static float ГромкостьОкружения
    {
        get => PlayerPrefs.GetFloat(Kокружение, 0.7f);
        set { PlayerPrefs.SetFloat(Kокружение, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    public static float ГромкостьДвигателей
    {
        get => PlayerPrefs.GetFloat(Kдвигатели, 0.7f);
        set { PlayerPrefs.SetFloat(Kдвигатели, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    public static float Громкость
    {
        get => PlayerPrefs.GetFloat(Kгромкость, 0.8f);
        set { PlayerPrefs.SetFloat(Kгромкость, Mathf.Clamp01(value)); PlayerPrefs.Save(); Применить(); }
    }

    public static float ГромкостьИнтерфейса
    {
        get => PlayerPrefs.GetFloat(Kинтерфейс, 0.7f);
        set { PlayerPrefs.SetFloat(Kинтерфейс, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    // Режим ввода НЕ сохраняется между запусками: каждый запуск начинается с контроллеров,
    // руки выбираются в меню на этот сеанс. Сохранённые «Руки» включали руки сразу при
    // старте, и в эмуляторе в меню нечем было нажать.
    static Ввод _режимВвода = Ввод.Контроллеры;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void СброситьРежим() => _режимВвода = Ввод.Контроллеры;

    public static Ввод РежимВвода
    {
        get => _режимВвода;
        set { _режимВвода = value; РежимВводаРига.Применить(); }
    }

    // Помощь пилоту (RovPilotAssist): рычаг в нейтрали — аппарат сам держит глубину и курс.
    // Выкл — чистые рычаги. Действует сразу, в том числе в идущей миссии.
    public static bool ПомощьПилоту
    {
        get => PlayerPrefs.GetInt(Kпомощь, 1) == 1;
        set { PlayerPrefs.SetInt(Kпомощь, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static void Применить() => AudioListener.volume = Громкость;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ПриЗапуске() => Применить();

    // Числа на экранах — с десятичной точкой при любом языке системы: в шрифте окон нет
    // запятой, она бралась из запасного шрифта и «прыгала». Файлы для Excel (телеметрия,
    // испытания) задают русский формат явно и не меняются.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ТочкаВЧислах()
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = c;
        System.Globalization.CultureInfo.CurrentCulture = c;
    }
}

// Короткий щелчок интерфейса — генерируется кодом (без аудиофайлов), громкость —
// «Звуки интерфейса». Один AudioSource на всё приложение.
public static class UiSound
{
    static AudioSource _src;
    static AudioClip _клик, _наведение;

    public static void Клик() => Играть(ref _клик, 1400f, 0.05f, 0.35f);
    public static void Наведение() => Играть(ref _наведение, 900f, 0.025f, 0.12f);

    static void Играть(ref AudioClip клип, float частота, float длит, float громкость)
    {
        if (_src == null)
        {
            var go = new GameObject("UiSound");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.spatialBlend = 0f;
            _src.ignoreListenerPause = true;   // щёлкает и при паузе окна итогов
        }
        if (клип == null)
        {
            int rate = 44100, n = Mathf.CeilToInt(rate * длит);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float огибающая = Mathf.Exp(-t / (длит * 0.25f));
                data[i] = Mathf.Sin(2f * Mathf.PI * частота * t) * огибающая;
            }
            клип = AudioClip.Create("ui", n, 1, rate, false);
            клип.SetData(data, 0);
        }
        _src.PlayOneShot(клип, громкость * GameSettings.ГромкостьИнтерфейса);
    }
}

// Звуки, синтезированные кодом (без аудиофайлов): подводный фон, гул движителя,
// удар о препятствие, сигнал фиксации дефекта. Клипы создаются один раз и
// переиспользуются.
public static class ПроцЗвук
{
    static AudioClip _окружение, _мотор, _удар, _пинг;
    const int Rate = 44100;

    // Подводный фон: «коричневый» шум (низкочастотный гул толщи воды) с медленными
    // накатами + редкие тихие щелчки/пузырьки. 8 с, концы сшиты для бесшовного цикла.
    public static AudioClip Окружение()
    {
        if (_окружение != null) return _окружение;
        int n = Rate * 8;
        var d = new float[n];
        var rnd = new System.Random(7);
        float b = 0f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            b = Mathf.Clamp(b + ((float)rnd.NextDouble() * 2f - 1f) * 0.02f, -1f, 1f) * 0.998f;
            lp += (b - lp) * 0.05f;
            float t = (float)i / Rate;
            float накат = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * t / 8f) * Mathf.Sin(2f * Mathf.PI * t / 2.7f);
            d[i] = lp * 2.2f * накат;
        }
        for (int k = 0; k < 14; k++)   // пузырьки
        {
            int st = rnd.Next(n - Rate / 10);
            float f = 600f + (float)rnd.NextDouble() * 900f;
            for (int j = 0; j < Rate / 12; j++)
            {
                float tt = (float)j / Rate;
                d[st + j] += Mathf.Sin(2f * Mathf.PI * f * (1f + tt * 6f) * tt) * Mathf.Exp(-tt * 60f) * 0.08f;
            }
        }
        Сшить(d, Rate / 2);
        return _окружение = Клип("Окружение", d);
    }

    // Гул движителя — мягкий, но СЛЫШИМЫЙ: тон 110 Гц + тихая октава 220 Гц с
    // лёгким биением и «шелестом» воды. Без звенящих гармоник 480+ Гц (утомляли),
    // но и не чистый бас (65 Гц ноутбучные динамики и многие наушники не
    // воспроизводят — звук пропадал). 2 с, частоты кратны 0.5 Гц — цикл без щелчка.
    public static AudioClip Мотор()
    {
        if (_мотор != null) return _мотор;
        int n = Rate * 2;
        var d = new float[n];
        var rnd = new System.Random(3);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.04f;   // сильно сглаженный шум
            lp2 += (lp - lp2) * 0.04f;
            float биение = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 1.5f * t);
            d[i] = биение * (0.55f * Mathf.Sin(2f * Mathf.PI * 110f * t) + 0.2f * Mathf.Sin(2f * Mathf.PI * 220f * t))
                 + 1.2f * lp2;
        }
        Сшить(d, Rate / 4);
        return _мотор = Клип("Мотор", d);
    }

    // Глухой удар: низкий тон с быстрым спадом + щелчок.
    public static AudioClip Удар()
    {
        if (_удар != null) return _удар;
        int n = Rate / 2;
        var d = new float[n];
        var rnd = new System.Random(5);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            d[i] = Mathf.Sin(2f * Mathf.PI * (70f - 30f * t) * t) * Mathf.Exp(-t * 9f)
                 + ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f) * 0.5f;
        }
        return _удар = Клип("Удар", d);
    }

    // Сигнал фиксации дефекта: короткий «пинг» гидролокатора с эхом.
    public static AudioClip Пинг()
    {
        if (_пинг != null) return _пинг;
        int n = Rate * 3 / 2;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float тон = Mathf.Sin(2f * Mathf.PI * 1250f * t);
            d[i] = тон * (Mathf.Exp(-t * 7f) + 0.3f * Mathf.Exp(-Mathf.Max(0f, t - 0.35f) * 9f) * (t > 0.35f ? 1f : 0f)) * 0.6f;
        }
        return _пинг = Клип("Пинг", d);
    }

    static void Сшить(float[] d, int перехлёст)
    {
        int n = d.Length;
        for (int i = 0; i < перехлёст; i++)
        {
            float k = (float)i / перехлёст;
            d[i] = d[i] * k + d[n - перехлёст + i] * (1f - k);
        }
    }

    static AudioClip Клип(string имя, float[] d)
    {
        float макс = 0.0001f;
        foreach (var v in d) макс = Mathf.Max(макс, Mathf.Abs(v));
        for (int i = 0; i < d.Length; i++) d[i] = d[i] / макс * 0.9f;
        var c = AudioClip.Create(имя, d.Length, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}
