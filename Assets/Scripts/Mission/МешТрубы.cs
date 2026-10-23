using System.Collections.Generic;
using UnityEngine;

// Меш магистрали — прямая труба через всё дно: бетон, стыки, аноды. Повреждения
// (скол, удар якоря, выкрошенный стык, утечка, сработанный анод) меняют только форму
// трубы — материал вокруг остаётся прежним. Цвет самого повреждения — отдельная тонкая
// накладка поверх, с плавной границей из текстуры (Детали). Так край не идёт «пилой»
// по треугольникам и фото не растягивается на скосах.
// Трещина и сеть — тоже отдельные тонкие меши поверх трубы.
//
// Развёртка: u — вдоль трубы, v — по окружности (0 — верх). Материалы (подмеши):
// 0 бетон, 1 стык, 2 анод, 3 сработанный анод.
public static class МешТрубы
{
    struct Ключ { public float S, R; }

    public static Mesh Построить(МагистральныйТрубопровод тр)
    {
        float R = тр.Радиус, от = тр.От, до = тр.До;
        var ключи = КлючиРадиуса(тр);

        // Кольца: сетка вдоль трубы, ключевые точки и частые кольца на повреждениях.
        var кольца = new List<float>();
        for (float s = от; s < до; s += тр.ШагПрофиля) кольца.Add(s);
        кольца.Add(до);
        foreach (var к in ключи) if (к.S > от && к.S < до) кольца.Add(к.S);
        foreach (var п in тр.Повреждения)
        {
            if (!Размеры(тр, п, out float пол, out _)) continue;
            for (float s = п.S - пол - 0.05f; s <= п.S + пол + 0.05f; s += 0.025f)
                if (s > от && s < до) кольца.Add(s);
        }
        кольца.Sort();
        for (int i = кольца.Count - 1; i > 0; i--)
            if (кольца[i] - кольца[i - 1] < 1e-3f) кольца.RemoveAt(i);

        int N = Mathf.Max(8, тр.СегментовПоОкружности), строк = N + 1;
        var верш = new List<Vector3>(кольца.Count * строк + 2 * строк);
        var uv = new List<Vector2>(верш.Capacity);
        var треуг = new List<int>[4];
        for (int i = 0; i < 4; i++) треуг[i] = new List<int>();

        for (int k = 0; k < кольца.Count; k++)
        {
            float s = кольца[k];
            тр.РамкаНа(s, out Vector3 c, out _, out Vector3 вверх, out Vector3 бок);
            float rБаз = РадиусНа(ключи, s, R);
            for (int j = 0; j < строк; j++)
            {
                float θ = j / (float)N * Mathf.PI * 2f;
                float r = Повредить(тр, s, θ, rБаз);
                верш.Add(c + (вверх * Mathf.Cos(θ) + бок * Mathf.Sin(θ)) * r);
                uv.Add(new Vector2(s / тр.Тайл, j / (float)N));
            }
            if (k == 0) continue;

            int мат = Материал(тр, 0.5f * (кольца[k - 1] + s));
            int b0 = (k - 1) * строк, b1 = k * строк;
            for (int j = 0; j < N; j++)
            {
                int a = b0 + j, b = b0 + j + 1, c2 = b1 + j, d = b1 + j + 1;
                треуг[мат].Add(a); треуг[мат].Add(b); треуг[мат].Add(c2);
                треуг[мат].Add(b); треуг[мат].Add(d); треуг[мат].Add(c2);
            }
        }

        // Торцевые заглушки (у края дна, в мути их не видно, но без дыр в меше).
        Торец(тр, от, false, N, R, верш, uv, треуг[0]);
        Торец(тр, до, true, N, R, верш, uv, треуг[0]);

        var меш = new Mesh { name = "Магистраль_бетон" };
        if (верш.Count > 65000) меш.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        меш.SetVertices(верш); меш.SetUVs(0, uv);
        меш.subMeshCount = 4;
        for (int i = 0; i < 4; i++) меш.SetTriangles(треуг[i], i);
        меш.RecalculateNormals();

        // Шов развёртки (j = 0 и j = N — одна и та же точка): усредняем нормали,
        // иначе вдоль верха трубы видна полоса.
        var норм = меш.normals;
        for (int k = 0; k < кольца.Count; k++)
        {
            int a = k * строк, b = a + N;
            Vector3 n = (норм[a] + норм[b]).normalized;
            норм[a] = n; норм[b] = n;
        }
        меш.normals = норм;
        меш.RecalculateTangents();
        меш.RecalculateBounds();
        return меш;
    }

    // Профиль радиуса вдоль трубы — ломаная: R везде, кроме утопленных стыков
    // (с фаской бетона) и узких канавок по краям браслета анода.
    static List<Ключ> КлючиРадиуса(МагистральныйТрубопровод тр)
    {
        float R = тр.Радиус, w = тр.ШиринаСтыка_м * 0.5f;
        const float фаска = 0.04f;
        var ключи = new List<Ключ>();
        foreach (float c in тр.Стыки)
        {
            ключи.Add(new Ключ { S = c - w - фаска, R = R });
            ключи.Add(new Ключ { S = c - w, R = R - тр.УтоплениеСтыка_м });
            ключи.Add(new Ключ { S = c + w, R = R - тр.УтоплениеСтыка_м });
            ключи.Add(new Ключ { S = c + w + фаска, R = R });
        }
        foreach (var a in тр.Аноды)
            foreach (float x in new[] { a.x, a.y })
            {
                ключи.Add(new Ключ { S = x - 0.01f, R = R });
                ключи.Add(new Ключ { S = x, R = R - 0.01f });
                ключи.Add(new Ключ { S = x + 0.01f, R = R });
            }
        ключи.Sort((a, b) => a.S.CompareTo(b.S));
        return ключи;
    }

    static float РадиусНа(List<Ключ> ключи, float s, float R)
    {
        int lo = 0, hi = ключи.Count - 1;
        if (hi < 0 || s <= ключи[0].S || s >= ключи[hi].S) return R;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (ключи[m].S <= s) lo = m; else hi = m; }
        float дл = ключи[hi].S - ключи[lo].S;
        return дл < 1e-6f ? ключи[lo].R : Mathf.Lerp(ключи[lo].R, ключи[hi].R, (s - ключи[lo].S) / дл);
    }

    // 0 бетон, 1 стык, 2 анод, 3 сработанный анод. Границы — кольцами, без «пилы».
    static int Материал(МагистральныйТрубопровод тр, float s)
    {
        float w = тр.ШиринаСтыка_м * 0.5f;
        foreach (float c in тр.Стыки) if (Mathf.Abs(s - c) < w) return 1;
        foreach (var a in тр.Аноды)
            if (s > a.x && s < a.y)
            {
                foreach (var п in тр.Повреждения)
                    if (п.Тип == PipelineDefect.ТипДефекта.Анод && п.S > a.x && п.S < a.y) return 3;
                return 2;
            }
        return 0;
    }

    // ── Форма повреждений ──────────────────────────────────────────────────────

    // Полуразмеры пятна повреждения: вдоль трубы и по окружности, м. false — повреждение
    // форму трубы не меняет (трещина, сеть) или меняет без пятна (анод).
    static bool Размеры(МагистральныйТрубопровод тр, МагистральныйТрубопровод.Повреждение п, out float вдоль, out float поОкружности)
    {
        switch (п.Тип)
        {
            case PipelineDefect.ТипДефекта.СколБетона: вдоль = 0.3f * п.Размер; поОкружности = 0.4f * п.Размер; return true;
            case PipelineDefect.ТипДефекта.Утечка: вдоль = 0.14f; поОкружности = 0.14f; return true;
            case PipelineDefect.ТипДефекта.СледЯкоря: вдоль = 0.9f; поОкружности = 0.75f; return true;
            case PipelineDefect.ТипДефекта.ПовреждениеСтыка: вдоль = тр.ШиринаСтыка_м * 0.5f; поОкружности = 0.45f * п.Размер; return true;
            case PipelineDefect.ТипДефекта.Анод: вдоль = тр.ДлинаАнода_м * 0.5f; поОкружности = 0f; return true;
            default: вдоль = поОкружности = 0f; return false;
        }
    }

    // Маска пятна: > 0 — внутри, чем больше, тем глубже. Край неровный, но мягкий.
    static float Маска(МагистральныйТрубопровод тр, МагистральныйТрубопровод.Повреждение п, float ds, float dt)
    {
        Размеры(тр, п, out float a, out float b);
        float шум = Mathf.PerlinNoise(ds * 4f + п.Seed, dt * 4f + п.Seed * 0.37f) - 0.5f;
        if (п.Тип == PipelineDefect.ТипДефекта.ПовреждениеСтыка)
            return Mathf.Min(1f - Кв(dt / b) + шум * 0.3f, 1f - Mathf.Abs(ds) / (a * 0.85f));
        return 1f - Кв(ds / a) - Кв(dt / b) + шум * 0.3f;
    }

    static float Повредить(МагистральныйТрубопровод тр, float s, float θ, float r)
    {
        float R = тр.Радиус, дно = тр.РадиусИзол;
        foreach (var п in тр.Повреждения)
        {
            if (!Размеры(тр, п, out float пол, out _)) continue;
            float ds = s - п.S;
            if (Mathf.Abs(ds) > пол * 1.15f) continue;
            if (п.Тип == PipelineDefect.ТипДефекта.Анод)
            {
                // Сработанный браслет — изъеден неравномерно по всей окружности.
                if (Mathf.Abs(ds) < пол)
                    r = Mathf.Min(r, R - 0.01f - Mathf.PerlinNoise(s * 14f + п.Seed, θ * 3f) * 0.03f);
                continue;
            }
            float dt = Mathf.DeltaAngle(п.Угол * Mathf.Rad2Deg, θ * Mathf.Rad2Deg) * Mathf.Deg2Rad * R;
            float m = Маска(тр, п, ds, dt);
            if (m <= 0f) continue;
            switch (п.Тип)
            {
                case PipelineDefect.ТипДефекта.СколБетона:
                {
                    // Скошенный край и неровное дно скола: бетон выкрашивается кусками,
                    // до изоляции — только в самой глубине.
                    float комки = Mathf.PerlinNoise(ds * 22f + п.Seed, dt * 22f) * 0.35f;
                    float f = Mathf.Clamp01(Гладко(m / 0.35f) * (0.75f + комки) + Гладко(0.5f, 0.75f, m) * 0.3f);
                    r = Mathf.Min(r, R - f * (R - дно));
                    break;
                }
                case PipelineDefect.ТипДефекта.Утечка:
                    // Форму не меняет: газ идёт через трещинку в бетоне, видно пятно и пузыри.
                    break;
                case PipelineDefect.ТипДефекта.СледЯкоря:
                    // Бетон сорван, сталь вдавлена.
                    r = Mathf.Min(r, R - Гладко(m / 0.25f) * (R - дно) - 0.07f * Гладко(m / 0.7f));
                    break;
                case PipelineDefect.ТипДефекта.ПовреждениеСтыка:
                    r = Mathf.Min(r, Mathf.Lerp(r, дно - 0.002f, Гладко(m / 0.2f)));
                    break;
            }
        }
        return r;
    }

    static float Кв(float x) => x * x;
    static float Гладко(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float Гладко(float a, float b, float x) => Гладко((x - a) / (b - a));

    static void Торец(МагистральныйТрубопровод тр, float s, bool конец, int N, float R,
                      List<Vector3> верш, List<Vector2> uv, List<int> треуг)
    {
        тр.РамкаНа(s, out Vector3 c, out _, out Vector3 вверх, out Vector3 бок);
        int центр = верш.Count;
        верш.Add(c); uv.Add(new Vector2(0.5f, 0.5f));
        for (int j = 0; j <= N; j++)
        {
            float θ = j / (float)N * Mathf.PI * 2f;
            верш.Add(c + (вверх * Mathf.Cos(θ) + бок * Mathf.Sin(θ)) * R);
            uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(θ), 0.5f + 0.5f * Mathf.Sin(θ)));
        }
        for (int j = 0; j < N; j++)
        {
            int a = центр + 1 + j, b = центр + 2 + j;
            if (конец) { треуг.Add(центр); треуг.Add(a); треуг.Add(b); }
            else       { треуг.Add(центр); треуг.Add(b); треуг.Add(a); }
        }
    }

    // ── Детали поверх трубы: накладки повреждений, трещины, сеть ───────────────

    public static void Детали(МагистральныйТрубопровод тр, Transform родитель)
    {
        var ключи = КлючиРадиуса(тр);
        var трещины = new List<Vector3>(); var трУв = new List<Vector2>(); var трТр = new List<int>();
        foreach (var п in тр.Повреждения)
        {
            switch (п.Тип)
            {
                case PipelineDefect.ТипДефекта.Трещина: Трещина(тр, п, трещины, трУв, трТр); break;
                case PipelineDefect.ТипДефекта.ПостороннийПредмет: Сеть(тр, п, родитель); break;
                case PipelineDefect.ТипДефекта.Анод: break;
                default: Накладка(тр, п, ключи, родитель); break;
            }
        }
        if (трТр.Count > 0)
        {
            var m = new Mesh { name = "Трещины" };
            m.SetVertices(трещины); m.SetUVs(0, трУв); m.SetTriangles(трТр, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            Объект("Трещины", родитель, m, ПроцедурныеМатериалы.Трещина());
        }
    }

    // Накладка повреждения: сетка, повторяющая форму трубы в пятне (на 4 мм выше),
    // и своя текстура, где граница пятна — плавная, по той же маске, что и форма.
    static void Накладка(МагистральныйТрубопровод тр, МагистральныйТрубопровод.Повреждение п, List<Ключ> ключи, Transform родитель)
    {
        if (!Размеры(тр, п, out float a, out float b)) return;
        float R = тр.Радиус;
        float Ea = a * 1.15f + 0.02f, Eb = b * 1.2f + 0.03f;
        const int NU = 40, NV = 40;
        var верш = new List<Vector3>((NU + 1) * (NV + 1));
        var uv = new List<Vector2>(верш.Capacity);
        var треуг = new List<int>(NU * NV * 6);
        for (int i = 0; i <= NU; i++)
            for (int j = 0; j <= NV; j++)
            {
                float u = i / (float)NU, v = j / (float)NV;
                float s = п.S + Mathf.Lerp(-Ea, Ea, u);
                float θ = п.Угол + Mathf.Lerp(-Eb, Eb, v) / R;
                тр.РамкаНа(s, out Vector3 c, out _, out Vector3 вверх, out Vector3 бок);
                float r = Повредить(тр, s, θ, РадиусНа(ключи, s, R)) + 0.004f;
                верш.Add(c + (вверх * Mathf.Cos(θ) + бок * Mathf.Sin(θ)) * r);
                uv.Add(new Vector2(u, v));
            }
        for (int i = 0; i < NU; i++)
            for (int j = 0; j < NV; j++)
            {
                int i0 = i * (NV + 1) + j, i1 = i0 + 1, i2 = i0 + NV + 1, i3 = i2 + 1;
                треуг.Add(i0); треуг.Add(i1); треуг.Add(i2);
                треуг.Add(i1); треуг.Add(i3); треуг.Add(i2);
            }
        var m = new Mesh { name = "Накладка_" + п.Тип };
        m.SetVertices(верш); m.SetUVs(0, uv); m.SetTriangles(треуг, 0);
        m.RecalculateNormals(); m.RecalculateBounds();

        bool сталь = п.Тип == PipelineDefect.ТипДефекта.СледЯкоря || п.Тип == PipelineDefect.ТипДефекта.ПовреждениеСтыка;
        var tex = ТекстураПятна(тр, п, Ea, Eb);
        Объект("Накладка_" + п.Тип, родитель, m, ПроцедурныеМатериалы.Накладка("Повреждение " + п.Тип, tex, сталь ? 0.25f : 0.05f));
    }

    // Цвет пятна по маске: свежий излом бетона (светлее поверхности, с зёрнами
    // заполнителя) → в глубине чёрная изоляция; у удара якоря и стыка в середине —
    // сталь с ржавчиной. Альфа — граница пятна, плавно (для отсечения по 0,5).
    static Texture2D ТекстураПятна(МагистральныйТрубопровод тр, МагистральныйТрубопровод.Повреждение п, float Ea, float Eb)
    {
        const int n = 512;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Пятно " + п.Тип, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        Color бетон = new Color(0.50f, 0.49f, 0.46f), изоляция = new Color(0.06f, 0.06f, 0.06f), заливка = new Color(0.19f, 0.19f, 0.18f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float ds = Mathf.Lerp(-Ea, Ea, x / (n - 1f)), dt = Mathf.Lerp(-Eb, Eb, y / (n - 1f));
                float m = Маска(тр, п, ds, dt);
                float зерно = Mathf.PerlinNoise(ds * 70f + п.Seed, dt * 70f);
                float пятна = Mathf.PerlinNoise(ds * 9f - п.Seed, dt * 9f + 3f);
                Color излом = бетон * (0.85f + 0.3f * пятна);
                излом = Color.Lerp(излом, излом * 0.6f, Гладко(0.62f, 0.7f, зерно));          // зёрна заполнителя
                излом *= Mathf.Lerp(0.75f, 1f, Гладко(0f, 0.12f, m));                          // тень у кромки
                float ржа = Mathf.PerlinNoise(ds * 25f + 7f, dt * 25f - п.Seed);
                Color сталь = Color.Lerp(new Color(0.22f, 0.11f, 0.05f), new Color(0.50f, 0.27f, 0.11f), ржа);
                сталь = Color.Lerp(сталь, new Color(0.38f, 0.38f, 0.37f), Гладко(0.55f, 0.7f, Mathf.PerlinNoise(ds * 4f, dt * 30f)) * 0.6f);   // царапины металла
                Color c;
                switch (п.Тип)
                {
                    case PipelineDefect.ТипДефекта.Утечка:
                    {
                        // Отверстие, вокруг — бурый налёт и обесцвеченный бетон от газа.
                        // Пятно неровное (по шуму), цвет — чуть темнее и бурее бетона.
                        Color налёт = Color.Lerp(new Color(0.30f, 0.26f, 0.21f), new Color(0.38f, 0.33f, 0.26f), пятна);
                        c = Color.Lerp(налёт, налёт * 0.25f, Гладко(0.8f, 0.9f, m) * Гладко(0.55f, 0.7f, зерно));   // тёмные точки трещинки
                        float край = m - 0.15f - (зерно - 0.5f) * 0.5f - (пятна - 0.5f) * 0.4f;
                        c.a = Mathf.Clamp01(0.5f + край * 12f);
                        px[y * n + x] = c;
                        continue;
                    }
                    case PipelineDefect.ТипДефекта.СледЯкоря:
                        c = Color.Lerp(излом, сталь, Гладко(0.18f, 0.3f, m));
                        break;
                    case PipelineDefect.ТипДефекта.ПовреждениеСтыка:
                        c = Color.Lerp(заливка * (0.8f + 0.4f * пятна), сталь, Гладко(0.06f, 0.14f, m));
                        break;
                    default:
                    {
                        // Излом бетона; на средней глубине — ржавая арматурная сетка (ячейка
                        // ~10 см, проволока ~6 мм), в самой глубине — чёрная изоляция пятнами.
                        float сетка = Mathf.Min(Mathf.Abs(Mathf.Repeat(ds + 0.05f, 0.1f) - 0.05f), Mathf.Abs(Mathf.Repeat(dt + 0.05f, 0.1f) - 0.05f));
                        float проволока = 1f - Гладко(0.002f, 0.004f, сетка);
                        float видна = Гладко(0.18f, 0.28f, m) * (1f - Гладко(0.65f, 0.8f, m));
                        c = Color.Lerp(излом, сталь * 0.9f, проволока * видна);
                        float дно = Гладко(0.74f, 0.82f, m + (пятна - 0.5f) * 0.2f);
                        c = Color.Lerp(c, изоляция, дно * 0.9f);
                        break;
                    }
                }
                c.a = Mathf.Clamp01(0.5f + m * 25f);
                px[y * n + x] = c;
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // Кольцевая трещина в бетоне (от изгиба при укладке): тёмная извилистая линия
    // шириной несколько миллиметров по верхней части трубы.
    static void Трещина(МагистральныйТрубопровод тр, МагистральныйТрубопровод.Повреждение п,
                        List<Vector3> верш, List<Vector2> uv, List<int> треуг)
    {
        var rng = new System.Random(п.Seed + 7);
        float рнд() => (float)rng.NextDouble() - 0.5f;
        // Основная трещина — ломаная, идёт по окружности со случайными изломами; плюс
        // 2–3 коротких ответвления под углом.
        Линия(тр, п.S, п.Угол - 1.1f * п.Размер, п.Угол + 1.1f * п.Размер, 0.007f, rng, верш, uv, треуг);
        int ветвей = 2 + rng.Next(2);
        for (int k = 0; k < ветвей; k++)
        {
            float θ0 = п.Угол + рнд() * 1.6f * п.Размер;
            float s0 = п.S + рнд() * 0.02f;
            float уклон = (rng.Next(2) == 0 ? -1f : 1f) * (0.08f + 0.1f * (float)rng.NextDouble());
            Линия(тр, s0, θ0, θ0 + 0.18f + 0.2f * (float)rng.NextDouble(), 0.003f, rng, верш, uv, треуг, уклон);
        }
    }

    // Отрезок трещины от угла θ0 до θ1: шаг 1 см, на каждом шаге случайный излом вдоль трубы,
    // ширина гуляет и сходит на нет к концам. Уклон — сколько метров вдоль трубы на радиан.
    static void Линия(МагистральныйТрубопровод тр, float s0, float θ0, float θ1, float ширинаМакс, System.Random rng,
                      List<Vector3> верш, List<Vector2> uv, List<int> треуг, float уклон = 0f)
    {
        float R = тр.Радиус + 0.0015f;
        int шагов = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(θ1 - θ0) * R / 0.01f));
        int база = верш.Count;
        float s = s0, смещ = 0f;
        for (int i = 0; i <= шагов; i++)
        {
            float t = i / (float)шагов;
            float θ = Mathf.Lerp(θ0, θ1, t);
            смещ += ((float)rng.NextDouble() - 0.5f) * 0.008f - смещ * 0.08f;
            s = s0 + уклон * (θ - θ0) + смещ;
            float край = Mathf.Min(t, 1f - t) * 2f;
            float ширина = ширинаМакс * Mathf.Sqrt(Mathf.Clamp01(край)) * (0.5f + Mathf.PerlinNoise(θ * 12f, s0 * 50f)) + 0.0006f;
            тр.РамкаНа(s, out Vector3 c, out Vector3 ось, out Vector3 вверх, out Vector3 бок);
            Vector3 p = c + (вверх * Mathf.Cos(θ) + бок * Mathf.Sin(θ)) * R;
            верш.Add(p - ось * ширина * 0.5f); верш.Add(p + ось * ширина * 0.5f);
            uv.Add(new Vector2(0f, t)); uv.Add(new Vector2(1f, t));
            if (i == 0) continue;
            int a = база + (i - 1) * 2, b = a + 1, c2 = a + 2, d = a + 3;
            треуг.Add(a); треуг.Add(c2); треуг.Add(b);
            треуг.Add(b); треуг.Add(c2); треуг.Add(d);
        }
    }

    // Обрывок рыболовной сети, наброшенный на трубу: лежит поверх и свисает по бокам.
    static void Сеть(МагистральныйТрубопровод тр, МагистральныйТрубопровод.Повреждение п, Transform родитель)
    {
        const int NU = 20, NV = 36;
        float длина = 2.2f * п.Размер, R = тр.Радиус + 0.02f;
        float размах = 2.6f;                       // рад; за π/2 сеть свисает по боку
        var верш = new List<Vector3>(); var uv = new List<Vector2>(); var треуг = new List<int>();
        for (int i = 0; i <= NU; i++)
            for (int j = 0; j <= NV; j++)
            {
                float u = i / (float)NU, v = j / (float)NV;
                float φ = (v - 0.5f) * 2f * размах;
                float s = п.S + (u - 0.5f) * длина + 0.12f * Mathf.Sin(φ * 2f + п.Seed) * (Mathf.Abs(φ) / размах);
                тр.РамкаНа(s, out Vector3 c, out _, out Vector3 вверх, out Vector3 бок);
                Vector3 p;
                if (Mathf.Abs(φ) <= Mathf.PI * 0.5f)
                    p = c + (вверх * Mathf.Cos(φ) + бок * Mathf.Sin(φ)) * R;
                else
                {
                    float вниз = (Mathf.Abs(φ) - Mathf.PI * 0.5f) * R;
                    p = c + бок * Mathf.Sign(φ) * R - Vector3.up * вниз;
                    float дно = тр.ГрунтПодТрубой(s) + 0.02f;
                    if (p.y < дно) { p += бок * Mathf.Sign(φ) * (дно - p.y); p.y = дно; }   // лишнее ложится на грунт
                }
                p += Vector3.up * 0.02f * (Mathf.PerlinNoise(u * 6f + п.Seed, v * 6f) - 0.5f);
                верш.Add(p);
                uv.Add(new Vector2(u * длина / 0.08f, v * 2f * размах * R / 0.08f));   // ячея 8 см
            }
        for (int i = 0; i < NU; i++)
            for (int j = 0; j < NV; j++)
            {
                int a = i * (NV + 1) + j, b = a + 1, c = a + NV + 1, d = c + 1;
                треуг.Add(a); треуг.Add(b); треуг.Add(c);
                треуг.Add(b); треуг.Add(d); треуг.Add(c);
            }
        var m = new Mesh { name = "Сеть" };
        m.SetVertices(верш); m.SetUVs(0, uv); m.SetTriangles(треуг, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        Объект("Сеть", родитель, m, ПроцедурныеМатериалы.Сеть());
    }

    static void Объект(string имя, Transform родитель, Mesh m, Material мат)
    {
        var go = new GameObject(имя);
        go.layer = родитель.gameObject.layer;
        go.transform.SetParent(родитель, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterial = мат;
    }
}
