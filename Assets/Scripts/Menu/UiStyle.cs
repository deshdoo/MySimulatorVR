using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Общий вид VR-интерфейса: скруглённые плашки, тонкая обводка и мягкая тень.
// Спрайты рисуются кодом (SDF скруглённого прямоугольника со сглаживанием) и
// режутся 9-slice — один маленький спрайт годится для плашки любого размера.
// Радиус задаётся в единицах холста через pixelsPerUnitMultiplier.
public static class UiStyle
{
    // Радиус в текстуре: 32 px — гладкий край даже на крупных окнах.
    const int R = 32;

    static Sprite _заливка, _тень;
    static readonly Dictionary<int, Sprite> _обводки = new Dictionary<int, Sprite>();

    // Скруглить картинку: радиус и (для обводки) толщина — в единицах холста.
    public static void Скруглить(Image img, float радиус)
    {
        if (радиус <= 0f) return;
        img.sprite = _заливка ??= Создать(d => Покрытие(d));
        Нарезать(img, радиус);
    }

    public static void Обвести(Image img, float радиус, float толщина)
    {
        // Толщина в текстуре пропорциональна радиусу; кэш — по доле (шаг 1/64 радиуса).
        int доля = Mathf.Clamp(Mathf.RoundToInt(толщина / радиус * 64f), 1, 64);
        if (!_обводки.TryGetValue(доля, out var s))
        {
            float t = R * доля / 64f;
            s = Создать(d => Mathf.Min(Покрытие(d), 1f - Покрытие(d + t)));
            _обводки[доля] = s;
        }
        img.sprite = s;
        Нарезать(img, радиус);
    }

    // Тень: тёмное размытое пятно; кладётся под окно с запасом ~размытие по краям.
    public static void Тень(Image img, float размытие)
    {
        img.sprite = _тень ??= Создать(d => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-R, 0f, d)));
        Нарезать(img, размытие);
    }

    static void Нарезать(Image img, float радиус)
    {
        img.type = Image.Type.Sliced;
        img.fillCenter = true;
        img.pixelsPerUnitMultiplier = R / радиус;
    }

    // Сглаженное покрытие пикселя по расстоянию до края (отрицательно внутри).
    static float Покрытие(float d) => Mathf.Clamp01(0.5f - d);

    static Sprite Создать(System.Func<float, float> альфа)
    {
        int n = 2 * R + 2;   // центр в 2 пикселя растягивается
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UiStyle"
        };
        var px = new Color32[n * n];
        float c = n / 2f, полу = n / 2f - 1f;   // край в 1 px от границы — сглаживание не обрезается
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // SDF скруглённого квадрата со стороной n и радиусом R.
                float qx = Mathf.Abs(x + 0.5f - c) - полу + R, qy = Mathf.Abs(y + 0.5f - c) - полу + R;
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - R;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(альфа(d)) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(R, R, R, R));
    }
}
