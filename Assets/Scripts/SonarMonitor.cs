using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Экран гидролокатора кругового обзора на мониторе пульта (страница переключателя
// графиков на Monitor2 — как отдельный монитор гидролокатора на надводном модуле
// «Марлина-350»). Картинка — текстура прибора RovSonar (сцена Main); поверх —
// кольца дальности с подписями, курсовая черта (нос аппарата — вверх), текущее
// положение антенны и строка состояния. Шкала дальности — клавиша H.
//
// Создаётся переключателем страниц (TelemetryGraphSwitcher) отдельным окном на весь монитор.
public class SonarMonitor : MonoBehaviour
{
    public Key клавишаШкалы = Key.H;

    RectTransform _область;
    GameObject _корень;
    RawImage _эхо, _сетка;
    RectTransform _антенна;
    TextMeshProUGUI _шапка;
    TextMeshProUGUI[] _метки;
    RovSonar _прибор;
    float _шкалаНарисована = -1f;

    // Отдельное окно гидролокатора на весь монитор — в том же оформлении, что монитор
    // «Состояние НПА»: заголовок по центру, две рамки-панели с подзаголовками «■ …».
    // Шрифт, цвета и рамка берутся с самого первого монитора (по именам его объектов),
    // размеры — пропорционально холсту, поэтому окна выглядят одинаково.
    // монитор — корневой прямоугольник холста монитора.
    public void Построить(RectTransform монитор)
    {
        _область = монитор;
        _корень = new GameObject("Окно гидролокатора", typeof(RectTransform));
        var rt = (RectTransform)_корень.transform;
        rt.SetParent(монитор, false);
        Растянуть(rt, 0f);
        _корень.layer = монитор.gameObject.layer;

        float W = монитор.rect.width, H = монитор.rect.height;
        if (W <= 1e-5f || H <= 1e-5f) { W = 1.6f; H = 0.9f; }

        // Образцы оформления — монитор «Состояние НПА».
        var обрЗаг = Найти<TMP_Text>("Заголовок - Состояние");
        var обрПодзаг = Найти<TMP_Text>("Системы");
        var обрРамка = Найти<Image>("SystemsPanelFrame");
        float k = 1f;   // масштаб холстов: наш / образца
        if (обрЗаг != null && обрЗаг.canvas != null)
        {
            float hОбр = ((RectTransform)обрЗаг.canvas.transform).rect.height;
            if (hОбр > 1e-5f) k = H / hОбр;
        }
        _шрифт = обрЗаг != null ? обрЗаг.font : null;
        var цветЗаг = обрЗаг != null ? обрЗаг.color : new Color(0.25f, 0.80f, 1f);
        var цветПодзаг = обрПодзаг != null ? обрПодзаг.color : цветЗаг;
        _цветПодписи = цветПодзаг;

        var фон = Создать<Image>("Фон", rt); фон.color = new Color(0.01f, 0.02f, 0.03f, 1f); Растянуть(фон.rectTransform, 0f);

        float отступ = H * 0.035f;
        float шапкаH = H * 0.15f;
        var заголовок = Текст("Заголовок", rt, обрЗаг != null ? обрЗаг.fontSize * k : шапкаH * 0.5f, TextAlignmentOptions.Center);
        заголовок.color = цветЗаг;
        заголовок.rectTransform.anchorMin = new Vector2(0, 1); заголовок.rectTransform.anchorMax = new Vector2(1, 1);
        заголовок.rectTransform.pivot = new Vector2(0.5f, 1);
        заголовок.rectTransform.anchoredPosition = Vector2.zero;
        заголовок.rectTransform.sizeDelta = new Vector2(0, шапкаH);
        заголовок.text = "ГИДРОЛОКАТОР";

        // Две панели: слева «Обзор» (круг), справа «Параметры».
        float телоH = H - шапкаH - отступ;
        float левШ = Mathf.Min(телоH, W * 0.58f);
        float правШ = W - левШ - отступ * 3f;
        float подзагH = телоH * 0.12f;
        float кеглПодзаг = обрПодзаг != null ? обрПодзаг.fontSize * k : подзагH * 0.6f;
        var левая = Панель(rt, отступ, отступ, левШ, телоH, "■ Обзор", обрРамка, k, кеглПодзаг, подзагH);
        var правая = Панель(rt, отступ * 2f + левШ, отступ, правШ, телоH, "■ Параметры", обрРамка, k, кеглПодзаг, подзагH);

        // Круг — в левой панели под подзаголовком.
        float сторона = Mathf.Min(левШ - отступ, телоH - подзагH - отступ);
        var поле = new GameObject("Поле", typeof(RectTransform)).GetComponent<RectTransform>();
        поле.SetParent(левая, false);
        поле.anchorMin = поле.anchorMax = new Vector2(0.5f, 0f);
        поле.pivot = new Vector2(0.5f, 0f);
        поле.anchoredPosition = new Vector2(0f, (телоH - подзагH - сторона) * 0.5f);
        поле.sizeDelta = new Vector2(сторона, сторона);

        _эхо = Создать<RawImage>("Эхо", поле); Растянуть(_эхо.rectTransform, 0f);
        _сетка = Создать<RawImage>("Сетка", поле); Растянуть(_сетка.rectTransform, 0f);
        _сетка.texture = Сетка(512);

        var ант = Создать<Image>("Антенна", поле);
        ант.color = new Color(0.4f, 1f, 0.5f, 0.6f);
        _антенна = ант.rectTransform;
        _антенна.anchorMin = _антенна.anchorMax = new Vector2(0.5f, 0.5f);
        _антенна.pivot = new Vector2(0.5f, 0f);
        _антенна.sizeDelta = new Vector2(сторона * 0.006f, сторона * 0.5f);

        // Параметры — в правой панели, шрифтом подзаголовков.
        _шапка = Текст("Параметры", правая, кеглПодзаг * 0.95f, TextAlignmentOptions.TopLeft);
        _шапка.rectTransform.anchorMin = Vector2.zero; _шапка.rectTransform.anchorMax = Vector2.one;
        _шапка.rectTransform.offsetMin = new Vector2(отступ, отступ);
        _шапка.rectTransform.offsetMax = new Vector2(-отступ * 0.5f, -(подзагH + отступ * 0.3f));
        _шапка.textWrappingMode = TextWrappingModes.NoWrap;
        _шапка.enableAutoSizing = false;
        _шапка.lineSpacing = 4f;

        // Подписи колец — по диагонали вправо-вверх, у самого кольца, мелко.
        _метки = new TextMeshProUGUI[4];
        for (int i = 0; i < 4; i++)
        {
            var m = Текст("Кольцо" + (i + 1), поле, сторона * 0.04f, TextAlignmentOptions.BottomLeft);
            m.rectTransform.anchorMin = m.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m.rectTransform.pivot = new Vector2(0f, 0f);
            m.rectTransform.sizeDelta = new Vector2(сторона * 0.2f, сторона * 0.05f);
            float r = сторона * 0.5f * (i + 1) / 4f * 0.7071f;
            m.rectTransform.anchoredPosition = new Vector2(r + сторона * 0.005f, r + сторона * 0.005f);
            m.color = new Color(0.45f, 0.85f, 0.55f, 0.9f);
            _метки[i] = m;
        }
        Показать(false);
    }

    TMP_FontAsset _шрифт;
    Color _цветПодписи = new Color(0.5f, 0.75f, 0.85f);

    static T Найти<T>(string имя) where T : Component
    {
        foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.name == имя) return c;
        return null;
    }

    // Панель с рамкой (как на мониторе «Состояние НПА») и подзаголовком сверху-слева.
    RectTransform Панель(RectTransform родитель, float x, float y, float w, float h, string подзаголовок,
                         Image обрРамка, float k, float кегль, float подзагH)
    {
        var рамка = Создать<Image>("Панель " + подзаголовок, родитель);
        var r = рамка.rectTransform;
        r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        if (обрРамка != null)
        {
            рамка.sprite = обрРамка.sprite; рамка.type = обрРамка.type; рамка.color = обрРамка.color;
            рамка.pixelsPerUnitMultiplier = обрРамка.pixelsPerUnitMultiplier / Mathf.Max(1e-4f, k);
            рамка.fillCenter = обрРамка.fillCenter;
        }
        else рамка.color = new Color(0.2f, 0.62f, 0.8f, 0.35f);
        рамка.raycastTarget = false;
        var t = Текст("Подзаголовок", r, кегль, TextAlignmentOptions.Left);
        t.color = _цветПодписи;
        t.rectTransform.anchorMin = new Vector2(0, 1); t.rectTransform.anchorMax = new Vector2(1, 1);
        t.rectTransform.pivot = new Vector2(0, 1);
        t.rectTransform.anchoredPosition = new Vector2(h * 0.04f, 0);
        t.rectTransform.sizeDelta = new Vector2(-h * 0.08f, подзагH);
        t.text = подзаголовок;
        return r;
    }


    public void Показать(bool видно)
    {
        if (_корень == null) return;
        _корень.SetActive(видно);
        if (видно) _корень.transform.SetAsLastSibling();   // окно поверх графиков и заголовка монитора
    }
    public bool Видно => _корень != null && _корень.activeSelf;

    void Update()
    {
        if (_прибор == null) _прибор = RovSonar.Instance;
        if (_корень == null || !_корень.activeSelf) return;

        if (клавишаШкалы != Key.None && Keyboard.current != null && Keyboard.current[KeyBindings.K(клавишаШкалы)].wasPressedThisFrame && _прибор != null)
            _прибор.СледующаяШкала();

        if (_прибор == null)
        {
            _шапка.text = "<color=#FF6060>Нет сигнала прибора</color>";
            return;
        }
        if (_эхо.texture != _прибор.ИзображениеЭхо) _эхо.texture = _прибор.ИзображениеЭхо;
        _антенна.localRotation = Quaternion.Euler(0f, 0f, -_прибор.УголАнтенны_град);

        float R = _прибор.Дальность_м;
        if (!Mathf.Approximately(R, _шкалаНарисована))
        {
            for (int i = 0; i < 4; i++) _метки[i].text = $"{R * (i + 1) / 4f:0.#} м";
            _шкалаНарисована = R;
        }
        string серый = "<color=#" + ColorUtility.ToHtmlStringRGB(_цветПодписи) + ">", конец = "</color>";
        _шапка.text =
            (RovSystems.cableBroken ? "<color=#FF6060><b>НЕТ СВЯЗИ</b></color>\n" : "") +
            $"{серый}Частота{конец}  <b>900 кГц</b>\n" +
            $"{серый}Шкала{конец}  <b>{R:0} м</b>\n" +
            $"{серый}Оборот{конец}  <b>{_прибор.ВремяОборота_с:0} с</b>\n" +
            $"{серый}Курс{конец}  <b>{RovSystems.heading_deg:000}°</b>\n" +
            $"{серый}Антенна{конец}  <b>{_прибор.УголАнтенны_град:000}°</b>\n\n" +
            $"{серый}H — шкала\nB — закрыть{конец}";
    }

    // ---- построение UI ----
    static T Создать<T>(string имя, Transform родитель) where T : Component
    {
        var go = new GameObject(имя, typeof(RectTransform), typeof(T));
        go.transform.SetParent(родитель, false);
        go.layer = родитель.gameObject.layer;
        return go.GetComponent<T>();
    }

    static void Растянуть(RectTransform r, float отступ)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(отступ, отступ); r.offsetMax = new Vector2(-отступ, -отступ);
    }

    TextMeshProUGUI Текст(string имя, Transform родитель, float кегль, TextAlignmentOptions выравнивание)
    {
        var t = Создать<TextMeshProUGUI>(имя, родитель);
        if (_шрифт != null) t.font = _шрифт;
        t.fontSize = кегль; t.alignment = выравнивание; t.color = new Color(0.85f, 0.95f, 0.85f);
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }

    // Прозрачная текстура: 4 кольца дальности, курсовая черта, перекрестие, засечки через 30°.
    static Texture2D Сетка(int N)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "СеткаГидролокатора" };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f;
        var линия = new Color32(90, 200, 110, 150);
        var нос = new Color32(120, 255, 140, 220);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy);
                Color32 p = new Color32(0, 0, 0, 0);
                for (int k = 1; k <= 4; k++) if (Mathf.Abs(r - c * k / 4f) < 0.9f) p = линия;
                if (r > c) p = new Color32(0, 0, 0, 255);                         // вне круга — чёрная маска
                if (Mathf.Abs(dx) < 0.8f && dy > 0f && r <= c) p = нос;            // курсовая черта (нос)
                if ((Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f) && r <= c && p.a == 0) p = new Color32(60, 140, 80, 90);
                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                if (r > c - 10f && r <= c && Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 30f) * 30f)) < 0.5f) p = линия;
                px[y * N + x] = p;
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }
}
