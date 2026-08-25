// =============================================================================
// TelemetryPanel.cs
// -----------------------------------------------------------------------------
// Приборная панель телеметрии в стиле реального пультового ПО (ENA Mobula,
// QGroundControl): вертикальный стек компактных строк, в каждой —
//
//     ПОДПИСЬ      текущее значение      ~~~~ мини-график (спарклайн)
//
// Смысл раскладки: крупное число отвечает на вопрос «сколько сейчас», а
// спарклайн рядом — на вопрос «куда идёт» (растёт, падает, колеблется).
// Вместе они занимают одну строку, поэтому на экран влезает 5-8 параметров
// одновременно — в отличие от одного большого графика, который показывает
// один параметр во всех подробностях.
//
// Одно и другое дополняют друг друга: панель для обзора, TelemetryGraph
// (с TelemetryGraphSwitcher) — для разбора динамики конкретного контура.
//
// Вся вёрстка строится в рантайме: в сцене нужен ОДИН объект с этим
// компонентом, вручную городить строки не надо.
//
// Настройка: UI Image-панель на Canvas -> удалить Image -> Add Component ->
// TelemetryPanel -> список Строки уже заполнен разумным набором.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class TelemetryPanel : MonoBehaviour
{
    [System.Serializable]
    public class Строка
    {
        public TelemetryChannel канал = TelemetryChannel.Глубина;
        [Tooltip("Подпись слева. Пусто — берётся имя канала.")]
        public string подпись = "";
        public Color цвет = new Color(0.30f, 0.85f, 1.00f);
        [Tooltip("Формат числа: F0 — целые, F1 — один знак, F2 — два")]
        public string формат = "F1";
        [Tooltip("Показывать мини-график справа")]
        public bool спарклайн = true;
    }

    [Header("Что показываем")]
    public List<Строка> Строки = new List<Строка>();

    [Header("Раскладка")]
    [Tooltip("Подогнать высоту строк так, чтобы все влезли в панель. " +
             "Иначе лишние строки уезжают за нижний край.")]
    public bool автоВысотаСтрок = true;
    [Tooltip("Высота одной строки (при выключенной автовысоте)")]
    public float высотаСтроки = 24f;
    [Tooltip("Зазор между строками")]
    public float зазор = 3f;
    [Tooltip("Отступ от краёв панели")]
    public float поля = 4f;
    [Tooltip("Какую долю ширины строки занимает спарклайн (0..0.8)")]
    [Range(0f, 0.8f)] public float доляСпарклайна = 0.4f;
    [Tooltip("Ширина колонки с подписью")]
    public float ширинаПодписи = 55f;

    [Header("Шрифты")]
    [Tooltip("Размер подписи при выключенной автовысоте")]
    public float размерПодписи = 11f;
    [Tooltip("Размер числа при выключенной автовысоте")]
    public float размерЗначения = 18f;
    [Tooltip("Какую долю высоты строки занимает число (при автовысоте). " +
             "Высота самих букв примерно 0.7 от этого значения — остальное межстрочный воздух.")]
    [Range(0.2f, 1.3f)] public float доляШрифтаЗначения = 0.85f;
    [Tooltip("Какую долю высоты строки занимает подпись (при автовысоте)")]
    [Range(0.15f, 1.0f)] public float доляШрифтаПодписи = 0.5f;
    public Color цветПодписи = new Color(0.62f, 0.72f, 0.82f);
    [Tooltip("Красить число в цвет строки (иначе — в цвет подписи)")]
    public bool числоВЦветСтроки = true;

    [Header("Разделители")]
    public bool рисоватьРазделители = true;
    public Color цветРазделителя = new Color(0.25f, 0.35f, 0.45f, 0.35f);

    [Header("Шрифт")]
    [Tooltip("Шрифт панели. Пусто — дефолтный TMP-шрифт. Поставь тот же ofont, что на мониторе систем, " +
             "чтобы панель была в одном стиле с первым монитором.")]
    public TMP_FontAsset шрифт;

    [Header("Обновление")]
    [Tooltip("Частота обновления чисел, раз/с")]
    public float частотаОбновления = 10f;

    private readonly List<TMP_Text> _значения = new List<TMP_Text>();
    private float _nextUpdate;

    // Тот же приём, что в TelemetryGraph: Unity при добавлении элемента в список
    // через «+» не применяет инициализаторы из кода, и цвет приходит прозрачным.
    static readonly Color[] ЗапасныеЦвета =
    {
        new Color(0.30f, 0.85f, 1.00f),
        new Color(1.00f, 0.80f, 0.25f),
        new Color(0.45f, 1.00f, 0.55f),
        new Color(1.00f, 0.45f, 0.45f),
    };

    Color ЦветСтроки(Строка с, int индекс)
        => с.цвет.a > 0.01f ? с.цвет : ЗапасныеЦвета[индекс % ЗапасныеЦвета.Length];

    void Start()
    {
        Построить();
    }

    /// <summary>
    /// Пересобрать панель. Раскладка строится один раз в Start, поэтому правки
    /// размеров во время игры сами не применятся: правой кнопкой по заголовку
    /// компонента -> «Перестроить панель», и видно результат сразу, без перезапуска.
    /// </summary>
    [ContextMenu("Перестроить панель")]
    public void Перестроить()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        _значения.Clear();
        Построить();
    }

    void Update()
    {
        if (частотаОбновления > 0f && Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + (частотаОбновления > 0f ? 1f / частотаОбновления : 0f);

        var rec = TelemetryRecorder.Instance;
        for (int i = 0; i < _значения.Count && i < Строки.Count; i++)
        {
            var с = Строки[i];
            float v = rec != null ? rec.GetLatest(с.канал) : float.NaN;
            string ед = TelemetryRecorder.UnitOf(с.канал);
            _значения[i].text = float.IsNaN(v)
                ? "—"
                : v.ToString(string.IsNullOrEmpty(с.формат) ? "F1" : с.формат)
                  + (ед.Length > 0 ? " " + ед : "");
        }
    }

    void Построить()
    {
        var корень = (RectTransform)transform;

        // ВАЖНО: расчётная раскладка живёт в ЛОКАЛЬНЫХ переменных и НЕ пишется
        // обратно в поля инспектора. Раньше автоподгонка перезаписывала
        // высотуСтроки/размерыШрифта своими числами — введённые вручную значения
        // затирались при каждом запуске, а в сцену сохранялся расчётный мусор.
        float hСтроки = высотаСтроки;
        float рЗначения = размерЗначения;
        float рПодписи = размерПодписи;
        float wПодписи = ширинаПодписи;

        // Делим доступную высоту поровну: при семи строках фиксированной высоты
        // последние просто уезжали за нижний край панели.
        if (автоВысотаСтрок && Строки.Count > 0)
        {
            float доступно = корень.rect.height - поля * 2f - зазор * (Строки.Count - 1);
            hСтроки = Mathf.Max(8f, доступно / Строки.Count);

            // Шрифт задаём ДОЛЕЙ от высоты строки: при уменьшении числа строк
            // места становится больше, и текст должен расти вместе с ним.
            рЗначения = hСтроки * доляШрифтаЗначения;
            рПодписи = hСтроки * доляШрифтаПодписи;

            // Колонка подписи растёт вместе со шрифтом (самое длинное слово —
            // «СКОРОСТЬ», 8 знаков), но не съедает больше четверти ширины панели:
            // главное на панели — число, а не его название.
            wПодписи = Mathf.Min(рПодписи * 5.5f, корень.rect.width * 0.25f);
        }

        for (int i = 0; i < Строки.Count; i++)
        {
            var с = Строки[i];
            Color цвет = ЦветСтроки(с, i);

            // --- контейнер строки: прижат к верху, едет вниз по индексу ---
            var строка = НовыйУзел($"Строка{i}", корень);
            строка.anchorMin = new Vector2(0f, 1f);
            строка.anchorMax = new Vector2(1f, 1f);
            строка.pivot = new Vector2(0.5f, 1f);
            // Ширина тянется за панелью, минус поля с обеих сторон; высота фиксирована.
            строка.sizeDelta = new Vector2(-поля * 2f, hСтроки);
            строка.anchoredPosition = new Vector2(0f, -поля - i * (hСтроки + зазор));

            // --- подпись слева ---
            var подпись = НовыйТекст("Подпись", строка, рПодписи, цветПодписи,
                                     TextAlignmentOptions.Left, шрифт);
            подпись.rectTransform.anchorMin = new Vector2(0f, 0f);
            подпись.rectTransform.anchorMax = new Vector2(0f, 1f);
            подпись.rectTransform.pivot = new Vector2(0f, 0.5f);
            подпись.rectTransform.sizeDelta = new Vector2(wПодписи, 0f);
            подпись.rectTransform.anchoredPosition = Vector2.zero;
            подпись.text = (string.IsNullOrEmpty(с.подпись) ? с.канал.ToString() : с.подпись).ToUpperInvariant();

            // --- число: занимает место между подписью и спарклайном, прижато вправо ---
            float праваяГраница = с.спарклайн ? доляСпарклайна : 0f;
            var значение = НовыйТекст("Значение", строка, рЗначения,
                                      числоВЦветСтроки ? цвет : цветПодписи,
                                      TextAlignmentOptions.Right, шрифт);
            значение.rectTransform.anchorMin = new Vector2(0f, 0f);
            значение.rectTransform.anchorMax = new Vector2(1f - праваяГраница, 1f);
            значение.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            значение.rectTransform.offsetMin = new Vector2(wПодписи + 2f, 0f);
            значение.rectTransform.offsetMax = new Vector2(-4f, 0f);
            значение.text = "—";
            _значения.Add(значение);

            // --- спарклайн справа ---
            if (с.спарклайн && доляСпарклайна > 0.01f)
            {
                var поле = НовыйУзел("Спарклайн", строка);
                поле.anchorMin = new Vector2(1f - доляСпарклайна, 0f);
                поле.anchorMax = new Vector2(1f, 1f);
                поле.offsetMin = new Vector2(0f, 2f);
                поле.offsetMax = new Vector2(0f, -2f);

                var г = поле.gameObject.AddComponent<TelemetryGraph>();
                г.Серии = new List<TelemetryGraph.Серия>
                {
                    new TelemetryGraph.Серия { канал = с.канал, цвет = цвет, толщина = 1.5f }
                };
                // Спарклайн — это форма кривой, а не измерительный прибор:
                // сетка, числа и заголовок только съедят место и внимание.
                г.рисоватьСетку = false;
                г.выделятьНоль = false;
                г.положениеЗаголовка = TelemetryGraph.ПоложениеЗаголовка.Скрыт;
                г.показыватьЧислаОси = false;
                г.показыватьЗначение = false;
                // Плавный масштаб: кривая всегда занимает всю высоту клетки,
                // ступенчатый режим в такой мелкой рамке смотрится дёргано.
                г.режимМасштаба = TelemetryGraph.РежимМасштаба.АвтоПлавно;
                г.минимальныйРазмах = 0.2f;
                г.raycastTarget = false;
            }

            // --- разделитель под строкой ---
            if (рисоватьРазделители && i < Строки.Count - 1)
            {
                var линия = НовыйУзел("Разделитель", строка);
                линия.anchorMin = new Vector2(0f, 0f);
                линия.anchorMax = new Vector2(1f, 0f);
                линия.pivot = new Vector2(0.5f, 1f);
                линия.sizeDelta = new Vector2(0f, 1f);
                линия.anchoredPosition = new Vector2(0f, -зазор * 0.5f);

                var img = линия.gameObject.AddComponent<UnityEngine.UI.Image>();
                img.color = цветРазделителя;
                img.raycastTarget = false;
            }
        }
    }

    static RectTransform НовыйУзел(string имя, RectTransform родитель)
    {
        var go = new GameObject(имя, typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        return rt;
    }

    static TMP_Text НовыйТекст(string имя, RectTransform родитель, float размер,
                               Color цвет, TextAlignmentOptions выравнивание, TMP_FontAsset шрифт)
    {
        var rt = НовыйУзел(имя, родитель);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (шрифт != null) t.font = шрифт;
        t.color = цвет;
        t.alignment = выравнивание;
        t.raycastTarget = false;

        // Без этого «98,2 м» переносится по пробелу на вторую строку, не влезает
        // по высоте строки — и TMP не показывает НИЧЕГО. Значение всегда в одну строку.
        t.enableWordWrapping = false;

        // Автоподбор в пределах заданного размера: текст занимает столько, сколько
        // просят, но если ячейка узкая — ужимается, а не исчезает. Так крупный
        // шрифт нельзя выставить «в никуда».
        t.enableAutoSizing = true;
        t.fontSizeMax = размер;
        t.fontSizeMin = Mathf.Max(1f, размер * 0.35f);
        t.fontSize = размер;
        return t;
    }

    // Набор по умолчанию — заполняется при добавлении компонента в инспекторе.
    void Reset()
    {
        Строки = new List<Строка>
        {
            new Строка { канал = TelemetryChannel.Глубина,   подпись = "Глубина", цвет = Циан,    формат = "F1" },
            new Строка { канал = TelemetryChannel.Курс,      подпись = "Курс",    цвет = Зелёный, формат = "F0" },
            new Строка { канал = TelemetryChannel.Тангаж,    подпись = "Тангаж",  цвет = Жёлтый,  формат = "F1" },
            new Строка { канал = TelemetryChannel.Крен,      подпись = "Крен",    цвет = Жёлтый,  формат = "F1" },
            new Строка { канал = TelemetryChannel.Скорость,  подпись = "Скорость",цвет = Циан,    формат = "F2" },
            new Строка { канал = TelemetryChannel.ТокНагрузки, подпись = "Ток",   цвет = Красный, формат = "F1" },
        };
    }

    static readonly Color Циан    = new Color(0.30f, 0.85f, 1.00f);
    static readonly Color Жёлтый  = new Color(1.00f, 0.80f, 0.25f);
    static readonly Color Зелёный = new Color(0.45f, 1.00f, 0.55f);
    static readonly Color Красный = new Color(1.00f, 0.45f, 0.45f);
}
