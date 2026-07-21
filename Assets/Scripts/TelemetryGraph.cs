// =============================================================================
// TelemetryGraph.cs
// -----------------------------------------------------------------------------
// График телеметрии для второго монитора пульта. Читает историю из
// TelemetryRecorder и рисует её линиями прямо в UI-меш (OnPopulateMesh) —
// без сторонних библиотек графиков.
//
// Одна панель может показывать НЕСКОЛЬКО кривых с общей осью Y. Основной сценарий
// (теория автоматического управления): на одном поле уставка и факт глубины —
// видно перерегулирование и время переходного процесса; на соседнем — ошибка e
// и управляющее воздействие u.
//
// Масштаб по Y (важно для читаемости): по умолчанию режим «АвтоСтупенями» —
// границы округляются до круглых чисел (шаг 1/2/5·10^n) и меняются ступенькой,
// только когда линия вышла за поле или стала сильно мельче поля. Непрерывная
// подгонка границ (режим «Авто») выглядит так, будто величина скачет, хотя
// скачет масштаб — на приборе это недопустимо.
//
// NaN в истории = разрыв линии (данных не было, например автопилот был выключен).
// Ноль и «нет данных» на графике не должны выглядеть одинаково.
//
// Подписи (заголовок, числа у делений, легенда) график создаёт себе сам в
// рантайме — руками TMP-объекты городить не нужно.
//
// Настройка: UI Image-панель на Canvas -> удалить Image -> Add Component ->
// TelemetryGraph -> заполнить Серии.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasRenderer))]
public class TelemetryGraph : MaskableGraphic
{
    public enum ПоложениеЗаголовка
    {
        Скрыт,
        СнизуПоЦентру,
        СверхуСлева
    }

    public enum РежимМасштаба
    {
        Фиксированный,  // жёсткие минY/максY — самый стабильный
        АвтоСтупенями,  // круглые числа, переезд ступенькой (по умолчанию)
        АвтоПлавно      // непрерывная подгонка — «дышит», но всегда впритык
    }

    [System.Serializable]
    public class Серия
    {
        public TelemetryChannel канал = TelemetryChannel.Глубина;
        public Color цвет = new Color(0.30f, 0.85f, 1.00f);
        [Tooltip("Толщина линии в пикселях UI")]
        public float толщина = 2f;
        [Tooltip("Подпись в легенде. Пусто — берётся имя канала.")]
        public string подпись = "";
    }

    [Header("Что рисуем")]
    public List<Серия> Серии = new List<Серия>();

    [Header("Заголовок")]
    [Tooltip("Название графика. Пусто — берётся имя первой серии.")]
    public string заголовок = "";

    [Header("Масштаб по Y")]
    public РежимМасштаба режимМасштаба = РежимМасштаба.АвтоСтупенями;
    [Tooltip("Нижняя граница в режиме «Фиксированный»")]
    public float минY = 0f;
    [Tooltip("Верхняя граница в режиме «Фиксированный»")]
    public float максY = 50f;
    [Tooltip("Минимальный размах, чтобы шум в пару сантиметров не растягивался на всю панель")]
    public float минимальныйРазмах = 1f;
    [Tooltip("Скорость подстройки границ в режиме «АвтоПлавно», ед/с")]
    public float скоростьМасштаба = 3f;

    [Header("Сетка")]
    public bool рисоватьСетку = true;
    [Tooltip("Сколько горизонтальных делений")]
    [Range(2, 10)] public int линийСетки = 5;
    public Color цветСетки = new Color(0.25f, 0.35f, 0.45f, 0.5f);
    public float толщинаСетки = 1f;
    [Tooltip("Выделить линию нуля (полезно для ошибки и управления)")]
    public bool выделятьНоль = true;
    public Color цветНуля = new Color(0.60f, 0.70f, 0.80f, 0.8f);

    [Header("Подписи (создаются автоматически)")]
    [Tooltip("Где показать название графика")]
    public ПоложениеЗаголовка положениеЗаголовка = ПоложениеЗаголовка.СнизуПоЦентру;
    [Tooltip("Числа у горизонтальных делений")]
    public bool показыватьЧислаОси = true;
    [Tooltip("Текущее значение кривой в правом верхнем углу")]
    public bool показыватьЗначение = true;
    [Tooltip("Писать перед значением имя канала («Глубина 87,4 м» вместо «87,4 м»)")]
    public bool имяПередЗначением = false;
    public float размерШрифта = 9f;
    [Tooltip("Во сколько раз текущее значение крупнее остальных подписей")]
    public float крупностьЗначения = 1.4f;
    public Color цветПодписей = new Color(0.65f, 0.78f, 0.88f);
    [Tooltip("Формат текущего значения: F0 — целые, F1 — один знак после запятой")]
    public string форматЧисел = "F1";

    [Header("Обновление")]
    [Tooltip("Частота перерисовки, кадров/с. Меньше = дешевле, график всё равно плавный.")]
    public float частотаОбновления = 20f;

    private float _viewMin, _viewMax;
    private bool _viewInit;
    private float _nextRedraw;
    private bool _warnedNoRecorder;

    private RectTransform _подписиРодитель;
    private TMP_Text _заголовокТекст;
    private readonly List<TMP_Text> _числаОси = new List<TMP_Text>();
    private readonly List<TMP_Text> _легенда = new List<TMP_Text>();

    // Запасные цвета: Unity при добавлении элемента в список через «+» в
    // инспекторе НЕ применяет инициализаторы полей из кода — цвет приходит
    // прозрачным (0,0,0,0), и линия рисуется, но её не видно.
    static readonly Color[] ЗапасныеЦвета =
    {
        new Color(0.30f, 0.85f, 1.00f),   // циан
        new Color(1.00f, 0.80f, 0.25f),   // жёлтый
        new Color(0.45f, 1.00f, 0.55f),   // зелёный
        new Color(1.00f, 0.45f, 0.45f),   // красный
    };

    Color ЦветСерии(Серия s, int индекс)
        => s.цвет.a > 0.01f ? s.цвет : ЗапасныеЦвета[индекс % ЗапасныеЦвета.Length];

    static float ТолщинаСерии(Серия s) => s.толщина > 0.01f ? s.толщина : 2f;

    static string ИмяСерии(Серия s)
        => string.IsNullOrEmpty(s.подпись) ? s.канал.ToString() : s.подпись;

    protected override void Awake()
    {
        base.Awake();
        _viewMin = минY;
        _viewMax = максY;
    }

    protected override void Start()
    {
        base.Start();
        // Только если их ещё нет: переключатель страниц мог успеть вызвать
        // Перестроить() раньше нашего Start (порядок Start между компонентами
        // Unity не гарантирует). Без этой проверки строился второй набор подписей
        // поверх первого, а ссылка на первый терялась — и он оставался на экране.
        if (Application.isPlaying && _подписиРодитель == null) ПостроитьПодписи();
    }

    void Update()
    {
        if (частотаОбновления > 0f && Time.unscaledTime < _nextRedraw) return;
        _nextRedraw = Time.unscaledTime + (частотаОбновления > 0f ? 1f / частотаОбновления : 0f);

        ОбновитьДиапазон();
        ОбновитьПодписи();
        SetVerticesDirty();
    }

    // ------------------------------------------------------------------
    // Масштаб
    // ------------------------------------------------------------------

    void ОбновитьДиапазон()
    {
        if (режимМасштаба == РежимМасштаба.Фиксированный)
        {
            _viewMin = минY;
            _viewMax = максY;
            return;
        }

        var rec = TelemetryRecorder.Instance;
        if (rec == null || rec.Count == 0) return;

        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        foreach (var s in Серии)
        {
            for (int i = 0; i < rec.Count; i++)
            {
                float v = rec.GetSample(s.канал, i);
                if (float.IsNaN(v)) continue;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
        }
        if (float.IsInfinity(lo) || float.IsInfinity(hi)) return;   // одни NaN — оставляем как было

        // Гарантируем минимальный размах, иначе шум растянется на всю панель.
        if (hi - lo < минимальныйРазмах)
        {
            float mid = (hi + lo) * 0.5f;
            lo = mid - минимальныйРазмах * 0.5f;
            hi = mid + минимальныйРазмах * 0.5f;
        }

        if (режимМасштаба == РежимМасштаба.АвтоПлавно)
        {
            float pad = (hi - lo) * 0.1f;
            lo -= pad; hi += pad;
            if (!_viewInit) { _viewMin = lo; _viewMax = hi; _viewInit = true; return; }
            float k = скоростьМасштаба <= 0f ? 1f : 1f - Mathf.Exp(-скоростьМасштаба * Time.unscaledDeltaTime);
            _viewMin = Mathf.Lerp(_viewMin, lo, k);
            _viewMax = Mathf.Lerp(_viewMax, hi, k);
            return;
        }

        // --- АвтоСтупенями ---
        // Пересчитываем границы только когда это действительно нужно:
        //   - линия вышла за пределы поля, или
        //   - данные стали заметно мельче поля (иначе кривая вырождается в черту).
        // Между этими событиями масштаб стоит намертво — глазу не за что цепляться.
        bool вышлиЗаПоле = !_viewInit || lo < _viewMin || hi > _viewMax;
        bool сталоМелко = _viewInit && (hi - lo) < (_viewMax - _viewMin) * 0.45f;
        if (!вышлиЗаПоле && !сталоМелко) return;

        // Округляем границы до круглых чисел, чтобы деления читались:
        // шаг из ряда 1 / 2 / 5 · 10^n, границы кратны шагу.
        int делений = Mathf.Max(1, линийСетки - 1);
        float шаг = КруглыйШаг((hi - lo) / делений);
        float нижняя = Mathf.Floor(lo / шаг) * шаг;
        float верхняя = Mathf.Ceil(hi / шаг) * шаг;

        // Добиваем до нужного числа делений, чтобы сетка была равномерной.
        while ((верхняя - нижняя) / шаг < делений - 0.001f) верхняя += шаг;

        _viewMin = нижняя;
        _viewMax = верхняя;
        _viewInit = true;
    }

    // Ближайшее «круглое» значение шага: 1, 2, 5, 10, 20, 50, 0.1, 0.2, 0.5 ...
    static float КруглыйШаг(float сырой)
    {
        if (сырой <= 0f || float.IsNaN(сырой)) return 1f;
        float степень = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(сырой)));
        float мантисса = сырой / степень;                       // 1..10
        float округл = мантисса <= 1f ? 1f : мантисса <= 2f ? 2f : мантисса <= 5f ? 5f : 10f;
        return округл * степень;
    }

    // ------------------------------------------------------------------
    // Подписи (создаются в рантайме, чтобы не городить их руками в сцене)
    // ------------------------------------------------------------------

    /// <summary>
    /// Пересобрать подписи и сбросить масштаб. Вызывать после того, как список
    /// Серии или настройки масштаба поменяли из кода (см. TelemetryGraphSwitcher):
    /// подписи создаются под конкретный набор серий и сами не обновятся.
    /// </summary>
    public void Перестроить()
    {
        if (!Application.isPlaying) return;

        if (_подписиРодитель != null) Destroy(_подписиРодитель.gameObject);
        _подписиРодитель = null;
        _заголовокТекст = null;
        _числаОси.Clear();
        _легенда.Clear();

        // Масштаб от прошлой страницы к новой величине отношения не имеет.
        _viewInit = false;
        _viewMin = минY;
        _viewMax = максY;

        ПостроитьПодписи();
    }

    void ПостроитьПодписи()
    {
        // Подчищаем наборы, оставшиеся от прошлых перестроений: если такой
        // осиротел, он висит поверх новых подписей и цифры наезжают друг на друга.
        for (int i = rectTransform.childCount - 1; i >= 0; i--)
        {
            var ребёнок = rectTransform.GetChild(i);
            if (ребёнок.name == "Подписи") Destroy(ребёнок.gameObject);
        }

        var go = new GameObject("Подписи", typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;          // не сохраняем в сцену
        _подписиРодитель = (RectTransform)go.transform;
        _подписиРодитель.SetParent(rectTransform, false);
        _подписиРодитель.anchorMin = Vector2.zero;
        _подписиРодитель.anchorMax = Vector2.one;
        _подписиРодитель.offsetMin = Vector2.zero;
        _подписиРодитель.offsetMax = Vector2.zero;

        if (положениеЗаголовка == ПоложениеЗаголовка.СнизуПоЦентру)
            _заголовокТекст = НоваяПодпись("Заголовок", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                                           new Vector2(0f, 2f), TextAlignmentOptions.Bottom, цветПодписей, размерШрифта);
        else if (положениеЗаголовка == ПоложениеЗаголовка.СверхуСлева)
            _заголовокТекст = НоваяПодпись("Заголовок", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                           new Vector2(4f, -2f), TextAlignmentOptions.TopLeft, цветПодписей, размерШрифта);

        if (показыватьЧислаОси)
            for (int i = 0; i < линийСетки; i++)
                _числаОси.Add(НоваяПодпись($"Ось{i}", new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                                           Vector2.zero, TextAlignmentOptions.Left, цветПодписей, размерШрифта));

        if (показыватьЗначение)
        {
            float крупный = размерШрифта * Mathf.Max(1f, крупностьЗначения);
            for (int i = 0; i < Серии.Count; i++)
                _легенда.Add(НоваяПодпись($"Значение{i}", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                          new Vector2(-4f, -2f - i * (крупный + 2f)),
                                          TextAlignmentOptions.TopRight, ЦветСерии(Серии[i], i), крупный));
        }

    }

    TMP_Text НоваяПодпись(string имя, Vector2 anchor, Vector2 pivot, Vector2 позиция,
                          TextAlignmentOptions выравнивание, Color цвет, float размер)
    {
        var go = new GameObject(имя, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        var rt = (RectTransform)go.transform;
        rt.SetParent(_подписиРодитель, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(140f, размер + 4f);
        rt.anchoredPosition = позиция;

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = размер;
        t.color = цвет;
        t.alignment = выравнивание;
        t.raycastTarget = false;
        return t;
    }

    void ОбновитьПодписи()
    {
        if (_подписиРодитель == null) return;

        var rec = TelemetryRecorder.Instance;

        if (_заголовокТекст != null)
        {
            string имя = !string.IsNullOrEmpty(заголовок)
                ? заголовок
                : (Серии.Count > 0 ? ИмяСерии(Серии[0]) : "Телеметрия");
            string ед = Серии.Count > 0 ? TelemetryRecorder.UnitOf(Серии[0].канал) : "";
            _заголовокТекст.text = ед.Length > 0 ? $"{имя}, {ед}" : имя;
        }

        // Числа у делений: снизу вверх, ровно там же, где линии сетки.
        // Формат берём по величине деления, а не общий: при шаге 50 писать
        // «200,0» — лишний шум, нужно «200».
        if (_числаОси.Count > 0)
        {
            Rect r = rectTransform.rect;
            int делений = Mathf.Max(1, _числаОси.Count - 1);
            string формат = ФорматДляШага((_viewMax - _viewMin) / делений);

            for (int i = 0; i < _числаОси.Count; i++)
            {
                float t = (float)i / делений;
                float значение = Mathf.Lerp(_viewMin, _viewMax, t);
                _числаОси[i].text = значение.ToString(формат);
                _числаОси[i].rectTransform.anchoredPosition = new Vector2(3f, t * r.height);
            }
        }

        for (int i = 0; i < _легенда.Count && i < Серии.Count; i++)
        {
            var s = Серии[i];
            float v = rec != null ? rec.GetLatest(s.канал) : float.NaN;
            string ед = TelemetryRecorder.UnitOf(s.канал);
            string значение = float.IsNaN(v)
                ? "—"
                : v.ToString(форматЧисел) + (ед.Length > 0 ? " " + ед : "");
            _легенда[i].text = имяПередЗначением ? $"{ИмяСерии(s)}  {значение}" : значение;
            _легенда[i].color = ЦветСерии(s, i);
        }
    }

    // Сколько знаков после запятой имеет смысл при таком шаге сетки.
    static string ФорматДляШага(float шаг)
    {
        шаг = Mathf.Abs(шаг);
        if (шаг >= 1f)   return "F0";
        if (шаг >= 0.1f) return "F1";
        return "F2";
    }

    // ------------------------------------------------------------------
    // Отрисовка
    // ------------------------------------------------------------------

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        var rec = TelemetryRecorder.Instance;

        if (рисоватьСетку) РисоватьСетку(vh, r);

        if (rec == null)
        {
            if (!_warnedNoRecorder)
            {
                _warnedNoRecorder = true;
                Debug.LogWarning($"[TelemetryGraph] на «{name}»: в сцене нет активного TelemetryRecorder — " +
                                 "рисуется только сетка. Создай пустой GameObject и добавь на него TelemetryRecorder.", this);
            }
            return;
        }
        if (rec.Count < 2) return;

        if (Mathf.Abs(_viewMax - _viewMin) < 1e-6f) return;

        for (int si = 0; si < Серии.Count; si++)
        {
            var s = Серии[si];
            Color цветЛинии = ЦветСерии(s, si);
            float толщинаЛинии = ТолщинаСерии(s);

            // Точки идут слева (старые) направо (свежие).
            Vector2 prev = Vector2.zero;
            bool hasPrev = false;

            // Ось X привязана к ВРЕМЕНИ, а не к номеру сэмпла: один шаг записи =
            // одна и та же ширина на экране всегда. Перо идёт слева направо, как на
            // самописце: сначала заполняет пустое поле, а когда упрётся в правый край,
            // окно начинает ехать. Растягивать неполный буфер на всю ширину нельзя —
            // тогда первые секунды рисуются так, будто прошла целая минута.
            int окноСэмплов = Mathf.Max(1, rec.Capacity - 1);

            for (int i = 0; i < rec.Count; i++)
            {
                float v = rec.GetSample(s.канал, i);
                if (float.IsNaN(v)) { hasPrev = false; continue; }   // разрыв линии

                float tx = (float)i / окноСэмплов;
                float ty = Mathf.InverseLerp(_viewMin, _viewMax, v);
                Vector2 p = new Vector2(r.xMin + tx * r.width, r.yMin + ty * r.height);

                if (hasPrev) ЛиниюВМеш(vh, prev, p, толщинаЛинии, цветЛинии);
                prev = p;
                hasPrev = true;
            }
        }
    }

    void РисоватьСетку(VertexHelper vh, Rect r)
    {
        for (int i = 0; i < линийСетки; i++)
        {
            float t = линийСетки > 1 ? (float)i / (линийСетки - 1) : 0.5f;
            float y = r.yMin + t * r.height;
            ЛиниюВМеш(vh, new Vector2(r.xMin, y), new Vector2(r.xMax, y), толщинаСетки, цветСетки);
        }

        // Ноль рисуем поверх сетки и только если он попал в видимый диапазон.
        if (выделятьНоль && _viewMin < 0f && _viewMax > 0f)
        {
            float t0 = Mathf.InverseLerp(_viewMin, _viewMax, 0f);
            float y0 = r.yMin + t0 * r.height;
            ЛиниюВМеш(vh, new Vector2(r.xMin, y0), new Vector2(r.xMax, y0), толщинаСетки * 1.5f, цветНуля);
        }
    }

    // Отрезок как четырёхугольник: UI умеет рисовать только треугольники,
    // поэтому «толстая линия» — это прямоугольник, развёрнутый вдоль отрезка.
    static void ЛиниюВМеш(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-6f) return;

        Vector2 n = new Vector2(-dir.y, dir.x) / len * (Mathf.Max(0.1f, width) * 0.5f);

        var v = UIVertex.simpleVert;
        v.color = color;

        int idx = vh.currentVertCount;
        v.position = a - n; vh.AddVert(v);
        v.position = a + n; vh.AddVert(v);
        v.position = b + n; vh.AddVert(v);
        v.position = b - n; vh.AddVert(v);

        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }
}
