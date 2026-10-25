using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Набор для VR-интерфейса: World Space Canvas, построенный кодом, с кнопками,
// которые жмутся как кнопки пульта (коллайдер + XRSimpleInteractable: палец/poke,
// луч контроллера с курком, щипок). Координаты — от верхнего левого угла
// родителя, в единицах холста (x вправо, y вниз); масштаб переводит их в метры.
//
// Используется главным меню. Дизайн — тот же, что у окна итогов миссии: тёмная
// скруглённая панель с тенью, циан-акцент, кнопки-плашки с обводкой, плитки с
// цветной кромкой (спрайты — UiStyle).
public class VrPanel
{
    public GameObject Корень { get; private set; }
    public RectTransform Rt { get; private set; }
    public CanvasGroup Группа { get; private set; }
    public readonly float W, H;

    public TMP_FontAsset Шрифт;
    public Color ЦветФона = new Color(0.04f, 0.07f, 0.10f, 0.94f);
    public Color ЦветТекста = new Color(0.90f, 0.94f, 0.97f);
    public Color ЦветВтор = new Color(0.52f, 0.60f, 0.68f);
    public Color ЦветАкцент = new Color(0.30f, 0.85f, 1.00f);
    public Color ЦветХорошо = new Color(0.35f, 0.95f, 0.50f);
    public Color ЦветСредне = new Color(1.00f, 0.78f, 0.25f);
    public Color ЦветПлохо = new Color(1.00f, 0.38f, 0.32f);
    public Color ЦветКромки = new Color(1f, 1f, 1f, 0.07f);   // тонкая обводка окон и плиток

    public const float РадиусОкна = 22f, РадиусКнопки = 10f;

    readonly Material _мат;
    readonly bool _поверх;

    class КнопкаЗапись
    {
        public Image Фон, Обводка; public XRSimpleInteractable И; public Color Цвет; public Func<bool> Выбрана;
        public BoxCollider Коробка; public Action Действие;
        public bool БылНавод; public float Уровень;
    }
    readonly List<КнопкаЗапись> _кнопки = new List<КнопкаЗапись>();
    static float _следНажатие;

    public VrPanel(string имя, float w, float h, float ширина_м, Scene сцена, TMP_FontAsset шрифт, bool поверхГеометрии)
    {
        W = w; H = h; Шрифт = шрифт; _поверх = поверхГеометрии;
        if (_поверх) ЦветФона.a = 1f;   // поверх кабины: сквозь фон просвечивали тексты мониторов
        Корень = new GameObject(имя, typeof(RectTransform));
        if (сцена.IsValid()) SceneManager.MoveGameObjectToScene(Корень, сцена);
        int ui = LayerMask.NameToLayer("UI");
        Корень.layer = ui >= 0 ? ui : 0;
        var canvas = Корень.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        Корень.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        Группа = Корень.AddComponent<CanvasGroup>();
        Rt = (RectTransform)Корень.transform;
        Rt.sizeDelta = new Vector2(w, h);
        Rt.localScale = Vector3.one * (ширина_м / w);
        if (_поверх) КонтроллерыНадОкнами.Окно(Корень);   // окно поверх кабины не закрывает контроллеры и луч
        УпорРук.Окно(Rt);   // руками: рука-призрак упирается в окно, а не проходит насквозь
        if (_поверх)
        {
            // Трюк world-space UI: шейдеры UI/TMP берут ZTest из unity_GUIZTestMode.
            _мат = new Material(Canvas.GetDefaultCanvasMaterial());
            _мат.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }
    }

    public void Уничтожить()
    {
        if (Корень != null) UnityEngine.Object.Destroy(Корень);
        if (_мат != null) UnityEngine.Object.Destroy(_мат);
        _кнопки.Clear();
    }

    // Подсветка наведения и «выбранных» кнопок (вкладки, карточки). Звать каждый кадр.
    // Подсветка плавная (~0.1 с): заливка густеет, обводка разгорается.
    public void Обновить()
    {
        float шаг = Time.unscaledDeltaTime / 0.1f;
        for (int i = _кнопки.Count - 1; i >= 0; i--)
        {
            var к = _кнопки[i];
            if (к.Фон == null) { _кнопки.RemoveAt(i); continue; }
            bool пальцем = НаведениеПальцем.Наведён(к.И);
            bool навод = к.И.isHovered || пальцем;
            // Действие может пересобрать или закрыть окно (список кнопок меняется) — дальше не идём.
            if (пальцем && ПальцемНасквозь(к)) { Нажать(к.Действие); return; }
            bool выбрана = к.Выбрана != null && к.Выбрана();
            к.Уровень = Mathf.MoveTowards(к.Уровень, навод ? 1f : выбрана ? 0.65f : 0f, шаг);
            Покрасить(к);
            if (навод && !к.БылНавод) UiSound.Наведение();
            к.БылНавод = навод;
        }
    }

    // Палец прошёл лицевую плоскость кнопки спереди назад в её пределах — нажатие. Своё, в
    // дополнение к poke XRI: в окнах поверх кабины касание пальцем срабатывало не всегда.
    // Лицо окна смотрит на оператора, т.е. в −z холста; кнопка — от z = 0 вглубь.
    static bool ПальцемНасквозь(КнопкаЗапись к)
    {
        if (к.Коробка == null || !к.Коробка.enabled) return false;
        var t = к.Коробка.transform;
        Vector3 ц = к.Коробка.center, пол = к.Коробка.size * 0.5f;
        for (int с = 0; с < 2; с++)
        {
            if (!НаведениеПальцем.Палец(с, out Vector3 сейчас, out Vector3 было)) continue;
            Vector3 a = t.InverseTransformPoint(было), b = t.InverseTransformPoint(сейчас);
            if (a.z >= 0f || b.z < 0f) continue;
            if (Mathf.Abs(b.x - ц.x) <= пол.x && Mathf.Abs(b.y - ц.y) <= пол.y) return true;
        }
        return false;
    }

    static void Покрасить(КнопкаЗапись к)
    {
        Color c = к.Цвет;
        к.Фон.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.12f, 0.62f, к.Уровень));
        if (к.Обводка != null) к.Обводка.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.32f, 1f, к.Уровень));
    }

    // Пустой контейнер на всю площадь родителя (страница меню).
    public RectTransform Контейнер(RectTransform родитель, string имя = "Стр")
    {
        var go = new GameObject(имя, typeof(RectTransform));
        go.layer = Корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, 0, 0, родитель.rect.width > 0 ? родитель.rect.width : W, родитель.rect.height > 0 ? родитель.rect.height : H);
        return rt;
    }

    public static void Очистить(RectTransform контейнер)
    {
        for (int i = контейнер.childCount - 1; i >= 0; i--)
        {
            var c = контейнер.GetChild(i).gameObject;
            c.SetActive(false);
            UnityEngine.Object.Destroy(c);
        }
    }

    // радиус > 0 — скруглённая плашка (UiStyle), иначе обычный прямоугольник (полосы, линии, графики).
    public Image Прямоугольник(RectTransform родитель, float x, float y, float w, float h, Color цвет, float радиус = 0f)
    {
        var go = new GameObject("Rect", typeof(RectTransform));
        go.layer = Корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, x, y, w, h);
        var img = go.AddComponent<Image>();
        img.color = цвет;
        img.raycastTarget = false;
        if (_мат != null) img.material = _мат;
        UiStyle.Скруглить(img, радиус);
        return img;
    }

    // Контур скруглённого прямоугольника (рамка карточки, фото).
    public Image Обводка(RectTransform родитель, float x, float y, float w, float h, Color цвет, float радиус, float толщина = 1.5f)
    {
        var img = Прямоугольник(родитель, x, y, w, h, цвет);
        UiStyle.Обвести(img, радиус, толщина);
        return img;
    }

    // Подложка окна на весь холст: мягкая тень, скруглённый фон, тонкая кромка и
    // акцентная полоса сверху (отступает от скруглений). Возвращает фон.
    public Image Окно(Color? полоса = null)
    {
        const float размытие = 36f;
        var тень = Прямоугольник(Rt, -размытие, -размытие + 10f, W + 2 * размытие, H + 2 * размытие, new Color(0f, 0f, 0f, 0.45f));
        UiStyle.Тень(тень, размытие);
        var фон = Прямоугольник(Rt, 0, 0, W, H, ЦветФона, РадиусОкна);
        Обводка(Rt, 0, 0, W, H, ЦветКромки, РадиусОкна, 1.5f);
        Прямоугольник(Rt, РадиусОкна + 14f, 0, W - 2 * (РадиусОкна + 14f), 3, полоса ?? ЦветАкцент, 1.5f);
        return фон;
    }

    public RawImage Картинка(RectTransform родитель, float x, float y, float w, float h, Texture tex, Color? цвет = null)
    {
        var go = new GameObject("Img", typeof(RectTransform));
        go.layer = Корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, x, y, w, h);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.color = цвет ?? Color.white;
        img.raycastTarget = false;
        if (_мат != null) img.material = _мат;
        return img;
    }

    public TMP_Text Текст(RectTransform родитель, float x, float y, float w, float h, float кегль, Color цвет,
                          TextAlignmentOptions выравн = TextAlignmentOptions.TopLeft, bool перенос = true)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.layer = Корень.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(родитель, false);
        Разместить(rt, x, y, w, h);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (Шрифт != null) t.font = Шрифт;
        t.fontSize = кегль;
        t.color = цвет;
        t.alignment = выравн;
        t.enableWordWrapping = перенос;
        t.overflowMode = перенос ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        if (_поверх) t.fontMaterial.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
        return t;
    }

    // Кнопка: фон-подсветка + надпись. выбрана — для вкладок/карточек (горит постоянно).
    public TMP_Text Кнопка(RectTransform родитель, float x, float y, float w, float h, string надпись, Color цвет,
                           Action действие, Func<bool> выбрана = null, float кегль = 24f)
    {
        var фон = Прямоугольник(родитель, x, y, w, h, цвет);
        var t = Текст((RectTransform)фон.transform, 0, 0, w, h, кегль, Color.white, TextAlignmentOptions.Center);
        t.text = надпись;
        ПодключитьНажатие(фон, w, h, цвет, действие, выбрана);
        return t;
    }

    // Сделать любой прямоугольник нажимаемым (карточка, строка списка). Плашка
    // скругляется и получает обводку цвета кнопки.
    public void ПодключитьНажатие(Image фон, float w, float h, Color цвет, Action действие, Func<bool> выбрана = null)
    {
        float радиус = Mathf.Min(РадиусКнопки, Mathf.Min(w, h) * 0.3f);
        if (фон.sprite == null) UiStyle.Скруглить(фон, радиус);
        var обводка = Обводка((RectTransform)фон.transform, 0, 0, w, h, цвет, радиус, 1.5f);
        var go = фон.gameObject;
        var box = go.AddComponent<BoxCollider>();
        // Тонкая зона у самой поверхности (2 мм перед окном, 8 мм за ним): палец жмёт там,
        // где рука-призрак упирается в окно (УпорРук), а не за сантиметры до него; лучу
        // толщина не важна.
        box.center = new Vector3(w / 2f, -h / 2f, 3f);   // пивот — верхний левый угол
        box.size = new Vector3(w, h, 10f);
        var и = go.AddComponent<XRSimpleInteractable>();
        // Палец (XRPokeInteractor рига) замечает только объекты с poke-фильтром — так
        // настроен риг (Require Poke Filter). У кнопок пульта в сцене он стоит, а у кнопок
        // из кода его не было: рука проходила насквозь, жал только луч с курком/щипком.
        go.AddComponent<XRPokeFilter>();
        НаведениеПальцем.Добавить(и);   // руками: загорается, когда палец ближе 10 см
        и.selectEntered.AddListener(a => Нажать(действие, a.interactorObject));
        // Касание пальцем жмёт сразу, как кнопки пульта.
        и.hoverEntered.AddListener(a => { if (a.interactorObject is XRPokeInteractor) Нажать(действие, a.interactorObject); });
        var к = new КнопкаЗапись { Фон = фон, Обводка = обводка, И = и, Цвет = цвет, Выбрана = выбрана, Коробка = box, Действие = действие };
        к.Уровень = выбрана != null && выбрана() ? 0.65f : 0f;
        Покрасить(к);
        _кнопки.Add(к);
    }

    // Не принимать нажатия ближайшие сек: окно появилось прямо под пальцем (меню на ладони).
    public static void ЗаблокироватьНажатия(float сек) => _следНажатие = Mathf.Max(_следНажатие, Time.unscaledTime + сек);

    // Антидребезг: касание и продавливание одним пальцем не должны сработать дважды.
    static void Нажать(Action действие, IXRInteractor кто = null)
    {
        if (Time.unscaledTime < _следНажатие) return;
        _следНажатие = Time.unscaledTime + 0.3f;
        UiSound.Клик();
        Вибро.Импульс(кто, Вибро.Кнопка);
        действие?.Invoke();
    }

    // Плитка: подпись, крупное значение, строка-пояснение.
    public void Плитка(RectTransform родитель, float x, float y, float w, float h, string подпись, string значение,
                       Color цвет, string пояснение = null)
    {
        Прямоугольник(родитель, x, y, w, h, new Color(1f, 1f, 1f, 0.045f), 8f);
        Обводка(родитель, x, y, w, h, ЦветКромки, 8f, 1.2f);
        Прямоугольник(родитель, x + 6, y + 10, 4, h - 20, цвет, 2f);   // цветная кромка = статус
        Текст(родитель, x + 16, y + 8, w - 24, 22, 16, ЦветВтор, перенос: false).text = подпись;
        var v = Текст(родитель, x + 16, y + 28, w - 24, 36, 28, цвет, перенос: false);
        v.fontStyle = FontStyles.Bold;
        v.text = значение;
        if (!string.IsNullOrEmpty(пояснение))
            Текст(родитель, x + 16, y + h - 24, w - 24, 20, 14, ЦветВтор, перенос: false).text = пояснение;
    }

    // Регулятор громкости: подпись слева сверху, ниже — [–] [████░░] [+]  80 %.
    // Каждая часть в своей колонке, ничего не налезает. VR-слайдеры неудобны — шаги по 10 %.
    public void Регулятор(RectTransform родитель, float x, float y, float w, string подпись, Func<float> получить, Action<float> задать)
        => Регулятор(родитель, x, y, w, подпись, 0f, 1f, 0.1f, получить, задать, v => Mathf.RoundToInt(v * 100f) + " %");

    // То же для любого диапазона [мин, макс] с шагом: полоса показывает положение в диапазоне.
    public void Регулятор(RectTransform родитель, float x, float y, float w, string подпись, float мин, float макс, float шаг,
                          Func<float> получить, Action<float> задать, Func<float, string> формат, Action послеИзменения = null)
    {
        Текст(родитель, x, y, w, 28, 21, ЦветТекста, перенос: false).text = подпись;
        const float кн = 52f, зазор = 14f, число = 120f;   // «с лагом», «−10 Н» — влезают
        float by = y + 32;
        float полосаX = x + кн + зазор;
        float bw = w - 2 * кн - 2 * зазор - число - зазор;
        float Доля() => Mathf.InverseLerp(мин, макс, получить());
        Прямоугольник(родитель, полосаX, by + 18, bw, 10, new Color(1f, 1f, 1f, 0.1f), 5f);
        var заливка = Прямоугольник(родитель, полосаX, by + 18, bw * Доля(), 10, ЦветАкцент, 5f);
        float плюсX = полосаX + bw + зазор;
        var значение = Текст(родитель, плюсX + кн + зазор, by + 8, число, 32, 22, ЦветТекста, TextAlignmentOptions.TopRight, false);
        void Обн()
        {
            заливка.rectTransform.sizeDelta = new Vector2(bw * Доля(), 10);
            значение.text = формат(получить());
            послеИзменения?.Invoke();
        }
        void Шаг(int знак)
        {
            float v = Mathf.Round((получить() + знак * шаг) / шаг) * шаг;   // без накопления ошибки округления
            задать(Mathf.Clamp(v, мин, макс)); Обн();
        }
        Обн();
        Кнопка(родитель, x, by, кн, 46, "–", ЦветВтор, () => Шаг(-1), кегль: 30);
        Кнопка(родитель, плюсX, by, кн, 46, "+", ЦветВтор, () => Шаг(+1), кегль: 30);
    }

    // Флажок «нет / есть»: подпись и сразу за ней квадрат; включено — квадрат горит и в
    // нём галочка. Для параметров с двумя положениями, где ползунок ни к чему.
    public void Флажок(RectTransform родитель, float x, float y, string подпись, Func<bool> получить, Action<bool> задать,
                       Action послеИзменения = null)
    {
        const float кв = 46f;
        var t = Текст(родитель, x, y, 360, кв, 21, ЦветТекста, TextAlignmentOptions.MidlineLeft, false);
        t.text = подпись;
        float bx = x + Mathf.Min(t.preferredWidth, 360f) + 18f;
        var фон = Прямоугольник(родитель, bx, y, кв, кв, ЦветАкцент);
        // Галочка из двух штрихов: знака «✓» в шрифте нет.
        var галочка = new GameObject("Галочка", typeof(RectTransform));
        галочка.layer = Корень.layer;
        var grt = (RectTransform)галочка.transform;
        grt.SetParent(фон.transform, false);
        Разместить(grt, 0, 0, кв, кв);
        Штрих(grt, new Vector2(13f, 24f), new Vector2(20f, 31f), 5f, Color.white);
        Штрих(grt, new Vector2(20f, 31f), new Vector2(34f, 15f), 5f, Color.white);
        галочка.SetActive(получить());
        ПодключитьНажатие(фон, кв, кв, ЦветАкцент, () =>
        {
            задать(!получить());
            галочка.SetActive(получить());
            послеИзменения?.Invoke();
        }, получить);
    }

    // Отрезок со скруглёнными концами от a до b (x вправо, y вниз, как у Разместить).
    void Штрих(RectTransform родитель, Vector2 a, Vector2 b, float толщина, Color цвет)
    {
        var rt = Прямоугольник(родитель, 0, 0, 1, толщина, цвет, толщина * 0.5f).rectTransform;
        Vector2 d = new Vector2(b.x - a.x, a.y - b.y);   // у RectTransform y смотрит вверх
        Vector2 н = d.normalized;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(a.x, -a.y) - н * толщина * 0.5f;   // концы заходят за точки — стык без щели
        rt.sizeDelta = new Vector2(d.magnitude + толщина, толщина);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    public static void Разместить(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}

// Иконки режима управления, нарисованные кодом (без картинок из интернета и без
// чужих логотипов): контроллер и ладонь. Белые на прозрачном — красятся цветом.
public static class ИконкиВвода
{
    static Texture2D _контроллер, _рука;

    public static Texture2D Иконка(GameSettings.Ввод в) =>
        в == GameSettings.Ввод.Руки ? _рука ??= Нарисовать(Рука) : _контроллер ??= Нарисовать(Контроллер);

    const int N = 128;

    static Texture2D Нарисовать(Func<float, float, float> форма)
    {
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // Координаты -1..1, y вверх; сглаживание края по SDF.
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float d = форма(u, v);
                float a = Mathf.Clamp01(0.5f - d * N * 0.5f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    // SDF-примитивы (отрицательно внутри).
    static float Прямоуг(float u, float v, float cx, float cy, float hw, float hh, float r)
    {
        float qx = Mathf.Abs(u - cx) - hw + r, qy = Mathf.Abs(v - cy) - hh + r;
        return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
    }
    static float Круг(float u, float v, float cx, float cy, float r) => Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - r;
    static float Объед(float a, float b) => Mathf.Min(a, b);
    static float Вычесть(float a, float b) => Mathf.Max(a, -b);

    // Поворот точки вокруг центра (для наклонных деталей).
    static void Повернуть(ref float u, ref float v, float cx, float cy, float град)
    {
        float a = град * Mathf.Deg2Rad, c = Mathf.Cos(a), sn = Mathf.Sin(a);
        float x = u - cx, y = v - cy;
        u = cx + x * c - y * sn; v = cy + x * sn + y * c;
    }

    // Контроллер Touch (без кольца, как у Quest 3): округлая головка со стиком и двумя
    // кнопками, рукоять наклонена вниз-назад.
    static float Контроллер(float u, float v)
    {
        float головка = Прямоуг(u, v, 0f, 0.32f, 0.46f, 0.3f, 0.28f);
        float ру = u, рв = v;
        Повернуть(ref ру, ref рв, 0.05f, -0.2f, 14f);
        float рукоять = Прямоуг(ру, рв, 0.05f, -0.38f, 0.17f, 0.44f, 0.16f);
        float кнопки = Объед(Круг(u, v, -0.17f, 0.34f, 0.11f), Объед(Круг(u, v, 0.17f, 0.45f, 0.065f), Круг(u, v, 0.2f, 0.22f, 0.065f)));
        return Вычесть(Объед(головка, рукоять), кнопки);
    }

    // Ладонь: четыре пальца, большой палец отведён в сторону.
    static float Рука(float u, float v)
    {
        float ладонь = Прямоуг(u, v, 0.05f, -0.36f, 0.36f, 0.32f, 0.14f);
        float пальцы = Прямоуг(u, v, -0.22f, 0.26f, 0.085f, 0.36f, 0.085f);
        пальцы = Объед(пальцы, Прямоуг(u, v, -0.01f, 0.32f, 0.085f, 0.4f, 0.085f));
        пальцы = Объед(пальцы, Прямоуг(u, v, 0.2f, 0.27f, 0.085f, 0.36f, 0.085f));
        пальцы = Объед(пальцы, Прямоуг(u, v, 0.38f, 0.14f, 0.075f, 0.28f, 0.075f));
        float бу = u, бв = v;
        Повернуть(ref бу, ref бв, -0.33f, -0.3f, -38f);
        float большой = Прямоуг(бу, бв, -0.45f, -0.12f, 0.085f, 0.26f, 0.085f);
        return Объед(Объед(ладонь, пальцы), большой);
    }
}
