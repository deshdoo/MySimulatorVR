using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Набор для VR-интерфейса: World Space Canvas, построенный кодом, с кнопками,
// которые жмутся как кнопки пульта (коллайдер + XRSimpleInteractable: палец/poke,
// луч контроллера с курком, щипок). Координаты — от верхнего левого угла
// родителя, в единицах холста (x вправо, y вниз); масштаб переводит их в метры.
//
// Используется главным меню. Дизайн — тот же, что у окна итогов миссии: тёмная
// панель, циан-акцент, плитки с цветной кромкой.
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

    readonly Material _мат;
    readonly bool _поверх;

    class КнопкаЗапись
    {
        public Image Фон; public XRSimpleInteractable И; public Color Цвет; public Func<bool> Выбрана;
        public bool БылНавод;
    }
    readonly List<КнопкаЗапись> _кнопки = new List<КнопкаЗапись>();
    static float _следНажатие;

    public VrPanel(string имя, float w, float h, float ширина_м, Scene сцена, TMP_FontAsset шрифт, bool поверхГеометрии)
    {
        W = w; H = h; Шрифт = шрифт; _поверх = поверхГеометрии;
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
    public void Обновить()
    {
        for (int i = _кнопки.Count - 1; i >= 0; i--)
        {
            var к = _кнопки[i];
            if (к.Фон == null) { _кнопки.RemoveAt(i); continue; }
            bool навод = к.И.isHovered;
            bool выбрана = к.Выбрана != null && к.Выбрана();
            float a = навод ? 1f : выбрана ? 0.6f : 0.2f;
            к.Фон.color = new Color(к.Цвет.r, к.Цвет.g, к.Цвет.b, a);
            if (навод && !к.БылНавод) UiSound.Наведение();
            к.БылНавод = навод;
        }
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

    public Image Прямоугольник(RectTransform родитель, float x, float y, float w, float h, Color цвет)
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
        return img;
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

    // Сделать любой прямоугольник нажимаемым (карточка, строка списка).
    public void ПодключитьНажатие(Image фон, float w, float h, Color цвет, Action действие, Func<bool> выбрана = null)
    {
        var go = фон.gameObject;
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(w / 2f, -h / 2f, -10f);   // пивот — верхний левый угол
        box.size = new Vector3(w, h, 40f);
        var и = go.AddComponent<XRSimpleInteractable>();
        и.selectEntered.AddListener(_ => Нажать(действие));
        // Касание пальцем жмёт сразу, как кнопки пульта.
        и.hoverEntered.AddListener(a => { if (a.interactorObject is XRPokeInteractor) Нажать(действие); });
        _кнопки.Add(new КнопкаЗапись { Фон = фон, И = и, Цвет = цвет, Выбрана = выбрана });
    }

    // Антидребезг: касание и продавливание одним пальцем не должны сработать дважды.
    static void Нажать(Action действие)
    {
        if (Time.unscaledTime < _следНажатие) return;
        _следНажатие = Time.unscaledTime + 0.3f;
        UiSound.Клик();
        действие?.Invoke();
    }

    // Плитка: подпись, крупное значение, строка-пояснение.
    public void Плитка(RectTransform родитель, float x, float y, float w, float h, string подпись, string значение,
                       Color цвет, string пояснение = null)
    {
        Прямоугольник(родитель, x, y, w, h, new Color(1f, 1f, 1f, 0.05f));
        Прямоугольник(родитель, x, y, 4, h, цвет);
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
        const float кн = 52f, зазор = 14f, число = 100f;
        float by = y + 32;
        float полосаX = x + кн + зазор;
        float bw = w - 2 * кн - 2 * зазор - число - зазор;
        float Доля() => Mathf.InverseLerp(мин, макс, получить());
        Прямоугольник(родитель, полосаX, by + 18, bw, 10, new Color(1f, 1f, 1f, 0.1f));
        var заливка = Прямоугольник(родитель, полосаX, by + 18, bw * Доля(), 10, ЦветАкцент);
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

    public static void Разместить(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}

// Иконки платформ, нарисованные кодом (без картинок из интернета и без чужих
// логотипов): очки VR, ПК + очки, клавиатура. Белые на прозрачном — красятся цветом.
public static class ИконкиПлатформ
{
    static Texture2D _quest, _pcvr, _клав;

    public static Texture2D Иконка(GameSettings.Платформа п)
    {
        switch (п)
        {
            case GameSettings.Платформа.Quest: return _quest ??= Нарисовать(Очки);
            case GameSettings.Платформа.ПКVR:  return _pcvr ??= Нарисовать(ПКиОчки);
            default:                           return _клав ??= Нарисовать(Клавиатура);
        }
    }

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

    // Очки VR: корпус со скруглением, две линзы-выреза, ремешок.
    static float Очки(float u, float v)
    {
        float корпус = Прямоуг(u, v, 0f, 0.05f, 0.78f, 0.4f, 0.22f);
        float линзы = Объед(Круг(u, v, -0.36f, 0.02f, 0.2f), Круг(u, v, 0.36f, 0.02f, 0.2f));
        float нос = Круг(u, v, 0f, -0.42f, 0.16f);
        float ремень = Прямоуг(u, v, 0f, 0.05f, 0.95f, 0.07f, 0.05f);
        return Вычесть(Вычесть(Объед(корпус, ремень), линзы), нос);
    }

    // ПК VR: монитор с подставкой и маленькие очки перед ним.
    static float ПКиОчки(float u, float v)
    {
        float экран = Вычесть(Прямоуг(u, v, 0.1f, 0.3f, 0.78f, 0.5f, 0.08f), Прямоуг(u, v, 0.1f, 0.33f, 0.66f, 0.38f, 0.04f));
        float ножка = Прямоуг(u, v, 0.1f, -0.3f, 0.08f, 0.14f, 0.02f);
        float основание = Прямоуг(u, v, 0.1f, -0.44f, 0.36f, 0.05f, 0.04f);
        float оч = Прямоуг(u, v, -0.45f, -0.55f, 0.42f, 0.22f, 0.12f);
        float линзы = Объед(Круг(u, v, -0.64f, -0.56f, 0.1f), Круг(u, v, -0.26f, -0.56f, 0.1f));
        float очки = Вычесть(оч, линзы);
        // Вокруг очков — зазор, чтобы не сливались с монитором.
        float база = Вычесть(Объед(Объед(экран, ножка), основание), Прямоуг(u, v, -0.45f, -0.55f, 0.5f, 0.3f, 0.16f));
        return Объед(база, очки);
    }

    // Клавиатура: корпус с сеткой клавиш и пробелом.
    static float Клавиатура(float u, float v)
    {
        float корпус = Прямоуг(u, v, 0f, 0f, 0.92f, 0.46f, 0.1f);
        float вырезы = 1f;
        for (int ряд = 0; ряд < 3; ряд++)
            for (int к = 0; к < 7; к++)
                вырезы = Объед(вырезы, Прямоуг(u, v, -0.66f + к * 0.22f, 0.26f - ряд * 0.2f, 0.08f, 0.07f, 0.02f));
        вырезы = Объед(вырезы, Прямоуг(u, v, 0f, -0.32f, 0.46f, 0.07f, 0.02f));
        return Вычесть(корпус, вырезы);
    }
}
