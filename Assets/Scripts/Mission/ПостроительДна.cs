using System.Collections.Generic;
using UnityEngine;

// Дно под магистраль. Руками в редакторе ничего делать не нужно:
//   • в редакторе ПредпросмотрДна сам записывает дно в ассет Assets/Terrain/Дно_трассы.asset
//     после каждой перекомпиляции — в режиме сцены видно то же, что в Play;
//   • при запуске, если рельеф уже сгенерирован этой версией, он не трогается; иначе
//     дно строится на копии данных рельефа (ассет не меняется).
//
// Что за дно:
//   • рельеф расширяется до 240 × 240 м вокруг своей середины, если он меньше
//     (трасса 120 м не помещается в 100 м; дальше 10–20 м под водой всё равно ничего
//     не видно);
//   • естественная илистая равнина с неровностью разных масштабов (ТрассаДна.Естественный);
//   • два слоя фото-текстур ambientCG (CC0): ил и пятна песка. Гладкость слоёв —
//     постоянная и почти нулевая: иначе Unity берёт её из альфы текстуры, у JPG это 1,
//     и дно блестит, как мокрое стекло;
//   • объекты, стоявшие на старом дне (аппарат, водоросли и т. п.), поднимаются или
//     опускаются вместе с новым дном.
public static class ПостроительДна
{
    // Меняется вместе с формулами дна — по имени данных видно, что рельеф уже свежий.
    public const string ИмяДанных = "Дно трассы v9";
    public const float МинРазмер_м = 240f;
    public const float ДиапазонВысот_м = 40f;   // вертикальный размер рельефа; дно — посередине
    const float ШагСетки_м = 0.25f;

    public static bool Построить(ТрассаДна.Трасса т, GameObject труба)
    {
        var terrain = РельефПод(Центр(т));
        if (terrain == null) { Debug.LogWarning("[Дно] Под трассой нет Terrain — дно не построено."); return false; }
        ОтключитьНевидимыеКоллайдеры(terrain);
        if (terrain.terrainData.name == ИмяДанных) return true;   // уже сгенерировано в редакторе

        Рассчитать(terrain, т, out float уровень, out Vector3 o, out Vector3 size);
        var наДне = ОбъектыНаДне(terrain, труба);
        var td = Object.Instantiate(terrain.terrainData);
        Сгенерировать(td, т, o, size, уровень, Слои(Слой));
        Назначить(terrain, td, o);
        Сдвинуть(terrain, наДне, rb: true);
        return true;
    }

    public static Vector3 Центр(ТрассаДна.Трасса т) =>
        new Vector3(т.Начало.x + т.Ось.x * т.Длина * 0.5f, 0f, т.Начало.y + т.Ось.y * т.Длина * 0.5f);

    // Уровень дна, начало и размер нового рельефа. Если рельеф уже наш — уровень и
    // размер берутся из него (иначе каждая перегенерация чуть сдвигала бы дно).
    public static void Рассчитать(Terrain terrain, ТрассаДна.Трасса т, out float уровень, out Vector3 o, out Vector3 size)
    {
        var старые = terrain.terrainData;
        Vector3 староеO = terrain.GetPosition(), староеSize = старые.size;
        if (старые.name.StartsWith("Дно трассы"))
            уровень = староеO.y + ДиапазонВысот_м * 0.5f;
        else
        {
            // Средняя высота старого дна вдоль трассы.
            уровень = 0f; int n = 0;
            for (float s = 0f; s <= т.Длина; s += 2f)
            {
                Vector2 p = т.Начало + т.Ось * s;
                var p3 = new Vector3(p.x, 0f, p.y);
                if (!Внутри(староеO, староеSize, p3)) continue;
                уровень += terrain.SampleHeight(p3) + староеO.y; n++;
            }
            уровень = n > 0 ? уровень / n : староеO.y;
        }
        size = new Vector3(Mathf.Max(староеSize.x, МинРазмер_м), ДиапазонВысот_м, Mathf.Max(староеSize.z, МинРазмер_м));
        Vector3 сер = староеO + староеSize * 0.5f;
        o = new Vector3(сер.x - size.x * 0.5f, уровень - ДиапазонВысот_м * 0.5f, сер.z - size.z * 0.5f);
    }

    // Слои дна по порядку: ил, песок, тёмный ил, скала. Источник слоя — функция (в игре
    // слой создаётся в памяти, в редакторе — ассетом).
    public static readonly string[] ИменаСлоёв = { "Ил", "Песок", "Тёмный ил", "Скала" };

    public static TerrainLayer[] Слои(System.Func<string, TerrainLayer> слой)
    {
        var слои = new TerrainLayer[ИменаСлоёв.Length];
        for (int i = 0; i < слои.Length; i++) слои[i] = слой(ИменаСлоёв[i]);
        return слои;
    }

    public static void Сгенерировать(TerrainData td, ТрассаДна.Трасса т, Vector3 o, Vector3 size, float уровень, TerrainLayer[] слои)
    {
        td.name = ИмяДанных;
        int res = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(size.x / ШагСетки_м)) + 1, 513, 2049);
        td.heightmapResolution = res;
        td.size = size;
        var h = new float[res, res];
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                var p = new Vector2(o.x + i / (float)(res - 1) * size.x, o.z + j / (float)(res - 1) * size.z);
                h[j, i] = Mathf.Clamp01((уровень + ТрассаДна.Естественный(т, p) - o.y) / ДиапазонВысот_м);
            }
        td.SetHeights(0, 0, h);

        if (System.Array.Exists(слои, l => l == null))
        {
            Debug.LogWarning("[Дно] Нет текстур дна в Resources/ДноТрассы — оставлены старые слои рельефа.");
            return;
        }
        // Раскраска по уклону: на крутом (стенки каньона, склоны холмов) — скала, на
        // ровном — ил с пятнами песка и тёмного ила. Уклон — по соседним точкам высоты.
        td.terrainLayers = слои;
        td.alphamapResolution = 1024;
        int ar = td.alphamapResolution;
        var карта = new float[ar, ar, 4];
        float шаг = size.x / (res - 1);
        for (int j = 0; j < ar; j++)
            for (int i = 0; i < ar; i++)
            {
                int hi = Mathf.Clamp(Mathf.RoundToInt(i / (float)(ar - 1) * (res - 1)), 1, res - 2);
                int hj = Mathf.Clamp(Mathf.RoundToInt(j / (float)(ar - 1) * (res - 1)), 1, res - 2);
                float dx = (h[hj, hi + 1] - h[hj, hi - 1]) * ДиапазонВысот_м / (2f * шаг);
                float dz = (h[hj + 1, hi] - h[hj - 1, hi]) * ДиапазонВысот_м / (2f * шаг);
                float уклон = Mathf.Sqrt(dx * dx + dz * dz);
                var p = new Vector2(o.x + i / (float)(ar - 1) * size.x, o.z + j / (float)(ar - 1) * size.z);
                float скала = ТрассаДна.Каменистость(т, p, уклон);
                float остаток = 1f - скала;
                float мох = ТрассаДна.ТёмныйИл(p) * 0.7f * остаток;
                float ил = ТрассаДна.Ил(p) * (остаток - мох);
                карта[j, i, 0] = ил;
                карта[j, i, 1] = остаток - мох - ил;
                карта[j, i, 2] = мох;
                карта[j, i, 3] = скала;
            }
        td.SetAlphamaps(0, 0, карта);
    }

    public static void Назначить(Terrain terrain, TerrainData td, Vector3 o)
    {
        terrain.terrainData = td;
        var col = terrain.GetComponent<TerrainCollider>();
        if (col != null) col.terrainData = td;
        terrain.transform.position = o;
    }

    // Слой из фото ambientCG в Resources. Цвет подправлен под сине-зелёный подводный
    // свет (у ambientCG песок пляжный, мох ярко-зелёный).
    public static TerrainLayer Слой(string имя)
    {
        string путь; Color оттенок; float плитка;
        switch (имя)
        {
            case "Ил":    путь = "ДноТрассы/Ground054_2K-JPG";  оттенок = new Color(0.45f, 0.50f, 0.48f); плитка = 2f; break;
            case "Песок": путь = "ДноТрассы/Ground093C_2K-JPG"; оттенок = new Color(0.55f, 0.57f, 0.52f); плитка = 2f; break;
            // Пятна тёмного ила вместо мха: зелёный мох делал дно похожим на сушу.
            case "Тёмный ил": путь = "ДноТрассы/Ground054_2K-JPG"; оттенок = new Color(0.30f, 0.33f, 0.34f); плитка = 3.5f; break;
            default:      путь = "ДноТрассы/Rock059_2K-JPG";    оттенок = new Color(0.70f, 0.64f, 0.67f); плитка = 4f; break;
        }
        var цвет = Resources.Load<Texture2D>(путь + "_Color");
        if (цвет == null) return null;
        return new TerrainLayer
        {
            name = имя,
            diffuseTexture = цвет,
            normalMapTexture = Resources.Load<Texture2D>(путь + "_NormalGL"),
            normalScale = 1f,
            tileSize = new Vector2(плитка, плитка),
            diffuseRemapMin = Vector4.zero,
            diffuseRemapMax = new Vector4(оттенок.r, оттенок.g, оттенок.b, 1f),
            metallic = 0f,
            smoothnessSource = TerrainLayerSmoothnessSource.Constant,
            smoothness = 0.05f,
        };
    }

    // В сцене есть пустой «Terrain» без данных рельефа, но с коллайдером старого дна —
    // невидимый пол, в который упирался бы аппарат. На время игры выключаем.
    static void ОтключитьНевидимыеКоллайдеры(Terrain наш)
    {
        foreach (var col in Object.FindObjectsByType<TerrainCollider>(FindObjectsSortMode.None))
        {
            if (col.gameObject == наш.gameObject || !col.enabled) continue;
            var t = col.GetComponent<Terrain>();
            if (t != null && t.terrainData != null && t.enabled) continue;
            col.enabled = false;
            Debug.Log($"[Дно] Выключен невидимый коллайдер рельефа «{col.name}» (без видимого рельефа).");
        }
    }

    // Отрезок прямой через центр, который лежит внутри рельефа (труба идёт от края до
    // края дна), в метрах вдоль направления от центра: [от, до].
    public static bool ПролётПоДну(Vector3 центр, Vector3 ось, out float от, out float до)
    {
        от = до = 0f;
        var t = РельефПод(центр);
        if (t == null) return false;
        Vector3 o = t.GetPosition(), size = t.terrainData.size;
        от = float.NegativeInfinity; до = float.PositiveInfinity;
        for (int k = 0; k < 3; k += 2)   // x и z
        {
            float c = центр[k], d = ось[k], min = o[k] + 1f, max = o[k] + size[k] - 1f;
            if (Mathf.Abs(d) < 1e-5f) continue;
            float t1 = (min - c) / d, t2 = (max - c) / d;
            от = Mathf.Max(от, Mathf.Min(t1, t2));
            до = Mathf.Min(до, Mathf.Max(t1, t2));
        }
        return до > от;
    }

    // Корневые объекты сцены, стоящие на дне (низ — не дальше 1,5 м под и 2,5 м над грунтом).
    public static List<(Transform tr, Vector3 низ, float старое)> ОбъектыНаДне(Terrain terrain, GameObject труба)
    {
        var список = new List<(Transform, Vector3, float)>();
        Vector3 o = terrain.GetPosition(), size = terrain.terrainData.size;
        foreach (var root in terrain.gameObject.scene.GetRootGameObjects())
        {
            if (root == terrain.gameObject || root == труба) continue;
            var рендеры = root.GetComponentsInChildren<Renderer>();
            if (рендеры.Length == 0) continue;
            Bounds b = рендеры[0].bounds;
            foreach (var r in рендеры) b.Encapsulate(r.bounds);
            if (b.size.x > 60f || b.size.z > 60f) continue;
            Vector3 низ = new Vector3(b.center.x, b.min.y, b.center.z);
            if (!Внутри(o, size, низ)) continue;
            float грунт = terrain.SampleHeight(низ) + o.y;
            if (низ.y < грунт - 1.5f || низ.y > грунт + 2.5f) continue;   // судно, вода, кабина — не на дне
            список.Add((root.transform, низ, грунт));
        }
        return список;
    }

    public static void Сдвинуть(Terrain terrain, List<(Transform tr, Vector3 низ, float старое)> наДне, bool rb)
    {
        var список = new System.Text.StringBuilder();
        foreach (var (tr, низ, старое) in наДне)
        {
            float dy = terrain.SampleHeight(низ) + terrain.GetPosition().y - старое;
            if (Mathf.Abs(dy) < 0.01f) continue;
            tr.position += Vector3.up * dy;
            if (rb && tr.TryGetComponent(out Rigidbody тело)) тело.position = tr.position;
            список.Append($"\n  {tr.name}: {dy:+0.00;-0.00} м");
        }
        Vector3 size = terrain.terrainData.size;
        Debug.Log($"[Дно] Рельеф {size.x:0}×{size.z:0} м, уровень дна {terrain.GetPosition().y + ДиапазонВысот_м * 0.5f:0.0} м. " +
                  $"Объекты на дне сдвинуты:{(список.Length == 0 ? " нет" : список.ToString())}");
    }

    public static Terrain РельефПод(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
            if (t.terrainData != null && Внутри(t.GetPosition(), t.terrainData.size, p)) return t;
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            if (t.terrainData != null && Внутри(t.GetPosition(), t.terrainData.size, p)) return t;
        return null;
    }

    static bool Внутри(Vector3 o, Vector3 size, Vector3 p) =>
        p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z;
}
