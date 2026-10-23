using System.Collections.Generic;
using UnityEngine;

// Материалы и мелкие меши магистрали, нарисованные в коде, — без ассетов текстур.
// Развёртка трубы: u — вдоль трубы, v — по окружности (0 — верх, 0,5 — низ). Поэтому
// по v можно «положить» то, что видно на реальной трубе после пары лет на дне:
// сверху — налёт осевшего ила, снизу и у грунта — тёмное обрастание.
// Шум периодический, чтобы текстура повторялась вдоль трубы без шва.
public static class ПроцедурныеМатериалы
{
    const int Размер = 512;

    public static Material Бетон()        => СЗерном(Материал("Бетон оболочки", РисоватьБетон, 0f, 0.12f), new Vector2(6f, 9f));
    public static Material Стык()         => СЗерном(Материал("Заливка стыка", РисоватьСтык, 0f, 0.3f), new Vector2(10f, 9f));
    public static Material Анод()         => Плотнее(Материал("Браслетный анод", РисоватьАнод, 0.3f, 0.2f));
    public static Material АнодСработан() => Плотнее(Материал("Сработанный анод", РисоватьСработанныйАнод, 0.25f, 0.12f));

    // Браслет — 0,6 м из 3 м развёртки: без повтора рисунок растягивался бы вдоль в 5 раз.
    static Material Плотнее(Material m)
    {
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(5f, 1f));
        return m;
    }

    // Накладка повреждения: своя текстура, прозрачное отсекается — граница пятна плавная.
    public static Material Накладка(string имя, Texture2D t, float гладкость)
    {
        var m = НовыйМатериал(имя);
        Текстура(m, t);
        Гладкость(m, 0f, гладкость);
        Отсечение(m, двусторонний: false);
        return СЗерном(m, new Vector2(4f, 4f));
    }

    // Чёткость вблизи: основная текстура даёт рисунок (налёт, обрастание, пятна) на всю
    // окружность, а поверх неё часто повторяется мелкое зерно бетона — detail-карты URP
    // Lit (цвет × зерно × 2, серый 0,5 — без изменений; плюс свои нормали). Так с 1–2 м
    // поверхность не расплывается, и не нужна огромная текстура.
    static Material СЗерном(Material m, Vector2 плотность)
    {
        if (!m.HasProperty("_DetailAlbedoMap")) return m;
        ПодготовитьЗерно();
        m.SetTexture("_DetailAlbedoMap", _зерно);
        m.SetTextureScale("_DetailAlbedoMap", плотность);
        m.SetTexture("_DetailNormalMap", _зерноНормали);
        m.SetFloat("_DetailAlbedoMapScale", 1f);
        m.SetFloat("_DetailNormalMapScale", 1f);
        m.EnableKeyword("_DETAIL_MULX2");
        return m;
    }

    static Texture2D _зерно, _зерноНормали;

    // Зерно: мелкие поры, песчинки заполнителя, шероховатость. Периодическое — без шва.
    static void ПодготовитьЗерно()
    {
        if (_зерно != null) return;
        const int n = 512;
        _зерно = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Зерно бетона", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        var px = new Color[n * n];
        var h = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float шер = Фрактал(u, v, 32, 32, 3, 201);
                float песок = Фрактал(u, v, 128, 128, 2, 203);
                float пора = Гладко(0.74f, 0.8f, Фрактал(u, v, 48, 48, 2, 205));
                float яркость = 0.5f + (шер - 0.5f) * 0.25f + (песок - 0.5f) * 0.2f - пора * 0.18f;
                px[y * n + x] = new Color(яркость, яркость, яркость, 1f);
                h[y * n + x] = шер * 0.5f + песок * 0.35f - пора * 0.6f;
            }
        _зерно.SetPixels(px);
        _зерно.Apply(true);
        _зерноНормали = КартаНормалей(h, "Зерно бетона", n, 10f);
        _зерноНормали.anisoLevel = 4;
    }

    public static Material Трещина()
    {
        var m = НовыйМатериал("Трещина");
        Цвет(m, new Color(0.04f, 0.04f, 0.035f));
        Гладкость(m, 0f, 0f);
        return m;
    }

    // Сеть — ячея 8 см на одну клетку текстуры, прозрачное отсекается, видна с обеих сторон.
    public static Material Сеть()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Ячея сети", wrapMode = TextureWrapMode.Repeat };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool нить = x < 4 || y < 4;
                float шум = Фрактал(x / (float)n, y / (float)n, 4, 4, 2, 51);
                Color c = Color.Lerp(new Color(0.22f, 0.32f, 0.28f), new Color(0.35f, 0.38f, 0.30f), шум);
                c.a = нить ? 1f : 0f;
                px[y * n + x] = c;
            }
        t.SetPixels(px); t.Apply(true, true);

        var m = НовыйМатериал("Рыболовная сеть");
        Текстура(m, t);
        Гладкость(m, 0f, 0.1f);
        Отсечение(m, двусторонний: true);
        return m;
    }

    static void Отсечение(Material m, bool двусторонний)
    {
        if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
        if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", двусторонний ? 0f : 2f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }

    delegate void Рисовалка(float u, float v, out Color цвет, out float высота);

    static Material НовыйМатериал(string имя)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(sh) { name = имя };
    }

    static void Цвет(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
    }

    static void Текстура(Material m, Texture2D t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        m.mainTexture = t;
    }

    static void Гладкость(Material m, float металл, float гладкость)
    {
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", металл);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", гладкость);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", гладкость);
    }

    static Material Материал(string имя, Рисовалка рис, float металл, float гладкость)
    {
        var цвет = new Texture2D(Размер, Размер, TextureFormat.RGBA32, true) { name = имя, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        var пиксели = new Color[Размер * Размер];
        var высоты = new float[Размер * Размер];
        for (int y = 0; y < Размер; y++)
            for (int x = 0; x < Размер; x++)
            {
                рис(x / (float)Размер, y / (float)Размер, out Color c, out float h);
                пиксели[y * Размер + x] = c;
                высоты[y * Размер + x] = h;
            }
        цвет.SetPixels(пиксели);
        цвет.Apply(true, true);

        var m = НовыйМатериал(имя);
        Текстура(m, цвет);
        Гладкость(m, металл, гладкость);
        if (m.HasProperty("_BumpMap"))
        {
            m.SetTexture("_BumpMap", КартаНормалей(высоты, имя, Размер, 6f));
            m.EnableKeyword("_NORMALMAP");
        }
        return m;
    }

    // Нормали из карты высот. Каналы: r = x, g = y, b = z, a = 1 — читается и
    // распаковкой AG (ПК), и RGB (мобильные), без импорта как Normal Map.
    static Texture2D КартаНормалей(float[] h, string имя, int n, float сила)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = имя + " (нормали)", wrapMode = TextureWrapMode.Repeat };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = h[y * n + (x + 1) % n] - h[y * n + (x + n - 1) % n];
                float dy = h[((y + 1) % n) * n + x] - h[((y + n - 1) % n) * n + x];
                var н = new Vector3(-dx * сила, -dy * сила, 1f).normalized;
                px[y * n + x] = new Color(н.x * 0.5f + 0.5f, н.y * 0.5f + 0.5f, н.z * 0.5f + 0.5f, 1f);
            }
        t.SetPixels(px);
        t.Apply(true, true);
        return t;
    }

    // ── Рисовалки ──────────────────────────────────────────────────────────────

    // 0 — верх трубы, 1 — низ.
    static float Низ(float v) => 0.5f - 0.5f * Mathf.Cos(v * Mathf.PI * 2f);

    // Ил сверху и обрастание снизу — общие для всех частей трубы.
    static Color НалётИОбрастание(Color c, float u, float v, int seed)
    {
        float низ = Низ(v);
        float n1 = Фрактал(u, v, 6, 10, 4, seed + 11);
        float n2 = Фрактал(u, v, 24, 40, 3, seed + 17);
        float обраст = Гладко(0.55f, 0.85f, низ + (n1 - 0.5f) * 0.4f);
        // Обрастание — тёмное серо-зелёное, ил — серый: бежевые тона делали трубу коричневой.
        Color зелень = Color.Lerp(new Color(0.20f, 0.23f, 0.21f), new Color(0.28f, 0.30f, 0.27f), n2);
        c = Color.Lerp(c, зелень, обраст * 0.75f);
        float ил = Гладко(0.45f, 0.9f, 1f - низ + (n1 - 0.5f) * 0.3f);
        c = Color.Lerp(c, new Color(0.50f, 0.50f, 0.48f), ил * (0.2f + 0.2f * n2));
        return c;
    }

    static void РисоватьБетон(float u, float v, out Color цвет, out float высота)
    {
        float пятна = Фрактал(u, v, 8, 12, 5, 1);
        float поры = Фрактал(u, v, 160, 220, 2, 2);
        Color c = Color.Lerp(new Color(0.42f, 0.43f, 0.43f), new Color(0.54f, 0.55f, 0.55f), пятна);   // серый бетон
        float пора = Гладко(0.8f, 0.86f, поры);
        c *= 1f - 0.18f * пора;
        цвет = НалётИОбрастание(c, u, v, 3);
        цвет.a = 1f;
        высота = пятна * 0.4f - пора * 0.2f + Фрактал(u, v, 64, 96, 2, 4) * 0.25f;
    }

    static void РисоватьСтык(float u, float v, out Color цвет, out float высота)
    {
        float n = Фрактал(u, v, 16, 24, 3, 21);
        Color c = Color.Lerp(new Color(0.13f, 0.13f, 0.12f), new Color(0.22f, 0.21f, 0.19f), n);
        цвет = НалётИОбрастание(c, u, v, 23);
        цвет.a = 1f;
        высота = n * 0.2f;
    }

    // Алюминиево-цинковый сплав: тусклый серый металл с белыми пятнами отложений,
    // по бокам — щели между двумя полубраслетами.
    static void РисоватьАнод(float u, float v, out Color цвет, out float высота)
    {
        float n = Фрактал(u, v, 12, 18, 4, 31);
        float отлож = Гладко(0.58f, 0.7f, Фрактал(u, v, 20, 30, 3, 33));
        Color c = Color.Lerp(new Color(0.36f, 0.37f, 0.37f), new Color(0.46f, 0.47f, 0.46f), n);   // тусклый алюминиевый сплав
        c = Color.Lerp(c, new Color(0.62f, 0.61f, 0.58f), отлож * 0.35f);   // налёт — едва заметный
        bool вЩели = Mathf.Min(Mathf.Abs(v - 0.25f), Mathf.Abs(v - 0.75f)) < 0.004f;
        if (вЩели) c = new Color(0.08f, 0.08f, 0.08f);
        цвет = НалётИОбрастание(c, u, v, 35);
        цвет.a = 1f;
        высота = отлож * 0.4f + n * 0.1f - (вЩели ? 0.6f : 0f);
    }

    static void РисоватьСработанныйАнод(float u, float v, out Color цвет, out float высота)
    {
        // Сработанный алюминиевый анод: тусклый тёмно-серый металл, весь в мелких оспинах
        // (сплав растворяется неравномерно), белёсый налёт продуктов растворения — мелкими
        // крапинами, не пятнами.
        float n = Фрактал(u, v, 24, 32, 4, 91);
        float оспины = Гладко(0.62f, 0.74f, Фрактал(u, v, 90, 120, 2, 93));
        float крапины = Гладко(0.78f, 0.86f, Фрактал(u, v, 60, 80, 2, 97));
        Color c = Color.Lerp(new Color(0.26f, 0.27f, 0.27f), new Color(0.38f, 0.38f, 0.37f), n);
        c = Color.Lerp(c, new Color(0.12f, 0.12f, 0.12f), оспины * 0.8f);
        c = Color.Lerp(c, new Color(0.70f, 0.69f, 0.65f), крапины * 0.6f);
        цвет = НалётИОбрастание(c, u, v, 95);
        цвет.a = 1f;
        высота = n * 0.3f - оспины * 0.7f + крапины * 0.2f;
    }

    // ── Шум ────────────────────────────────────────────────────────────────────

    static float Фрактал(float u, float v, int частU, int частV, int октав, int seed)
    {
        float сумма = 0f, амп = 0.5f, норма = 0f;
        for (int o = 0; o < октав; o++)
        {
            int пu = частU << o, пv = частV << o;
            сумма += Шум(u * пu, v * пv, пu, пv, seed + o * 101) * амп;
            норма += амп;
            амп *= 0.5f;
        }
        return сумма / норма;
    }

    static float Шум(float x, float y, int периодX, int периодY, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Хэш(xi, yi, периодX, периодY, seed), b = Хэш(xi + 1, yi, периодX, периодY, seed);
        float c = Хэш(xi, yi + 1, периодX, периодY, seed), d = Хэш(xi + 1, yi + 1, периодX, периодY, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Хэш(int x, int y, int пx, int пy, int seed)
    {
        x = ((x % пx) + пx) % пx; y = ((y % пy) + пy) % пy;
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h & 0xffffff) / 16777215f;
        }
    }

    static float Гладко(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }
}
