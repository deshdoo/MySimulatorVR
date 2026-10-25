using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Круглая VR-иконка, висящая в воздухе вертикально и всегда повёрнутая к оператору
// (как кнопки VR-интерфейсов): белосерый круг с тёмным знаком. Нажимается пальцем
// (касание) и лучом контроллера; при наведении чуть увеличивается, пока открыто её окно —
// обведена акцентным кольцом. Знак рисуется кодом: «?» — справка, «Дверь» — выход.
public class ИконкаКабины : MonoBehaviour
{
    public enum Знак { Вопрос, Дверь }

    const float Диаметр_м = 0.13f, Холст = 200f;
    static readonly Color Круг = new Color(0.86f, 0.87f, 0.89f, 0.95f);
    static readonly Color ЗнакЦвет = new Color(0.13f, 0.15f, 0.18f);
    static readonly Color Акцент = new Color(0.30f, 0.85f, 1.00f);

    Action _действие; Func<bool> _открыто;
    XRSimpleInteractable _и;
    Image _кольцо;
    float _масштаб = 1f, _следНажатие;

    public static ИконкаКабины Создать(string имя, Vector3 поз, Знак знак, Action действие, Func<bool> открыто, Scene сцена)
    {
        var go = new GameObject(имя, typeof(RectTransform));
        if (сцена.IsValid()) SceneManager.MoveGameObjectToScene(go, сцена);
        int ui = LayerMask.NameToLayer("UI");
        go.layer = ui >= 0 ? ui : 0;
        go.transform.position = поз;
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 100;
        go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(Холст, Холст);
        rt.localScale = Vector3.one * (Диаметр_м / Холст);
        УпорРук.Окно(rt);

        var и = go.AddComponent<ИконкаКабины>();
        и._действие = действие; и._открыто = открыто;

        и._кольцо = Картинка(rt, "Кольцо", Диск(128, 1f, 0f), Акцент, 1.12f).GetComponent<Image>();
        Картинка(rt, "Круг", Диск(128, 1f, 0.06f), Color.white, 1f);
        if (знак == Знак.Вопрос)
        {
            var t = new GameObject("Знак", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.gameObject.layer = go.layer;
            t.rectTransform.SetParent(rt, false);
            t.rectTransform.sizeDelta = new Vector2(Холст, Холст);
            t.text = "?"; t.fontSize = 150; t.fontStyle = FontStyles.Bold; t.color = ЗнакЦвет;
            t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        }
        else Картинка(rt, "Знак", ДверьОткрытая(128), Color.white, 1f);

        // Зона нажатия: коллайдер на весь круг + XR-интерактор (палец/луч, как кнопки VrPanel).
        var зона = new GameObject("Нажатие") { layer = go.layer };
        зона.transform.SetParent(go.transform, false);
        var box = зона.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0f, -10f);
        box.size = new Vector3(Холст, Холст, 40f);
        и._и = зона.AddComponent<XRSimpleInteractable>();
        зона.AddComponent<UnityEngine.XR.Interaction.Toolkit.Filtering.XRPokeFilter>();   // иначе палец не замечает (см. VrPanel)
        НаведениеПальцем.Добавить(и._и);
        и._и.selectEntered.AddListener(a => { Вибро.Импульс(a.interactorObject, Вибро.Кнопка); и.Нажать(); });
        и._и.hoverEntered.AddListener(a => { if (a.interactorObject is XRPokeInteractor) { Вибро.Импульс(a.interactorObject, Вибро.Кнопка); и.Нажать(); } });
        return и;
    }

    void Нажать()
    {
        if (Time.unscaledTime < _следНажатие) return;   // касание и продавливание — одно нажатие
        _следНажатие = Time.unscaledTime + 0.4f;
        UiSound.Клик();
        _действие?.Invoke();
    }

    void LateUpdate()
    {
        // Вертикально, лицом к оператору (поворот только вокруг вертикали).
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 от = Vector3.ProjectOnPlane(transform.position - cam.transform.position, Vector3.up);
            if (от.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(от, Vector3.up);
        }
        float цель = _и != null && (_и.isHovered || НаведениеПальцем.Наведён(_и)) ? 1.12f : 1f;
        _масштаб = Mathf.Lerp(_масштаб, цель, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        transform.localScale = Vector3.one * (Диаметр_м / Холст) * _масштаб;
        if (_кольцо != null) _кольцо.enabled = _открыто != null && _открыто();
    }

    // ---- картинки, нарисованные кодом ----
    static GameObject Картинка(RectTransform родитель, string имя, Texture2D tex, Color цвет, float масштаб)
    {
        var go = new GameObject(имя, typeof(RectTransform)) { layer = родитель.gameObject.layer };
        var r = (RectTransform)go.transform; r.SetParent(родитель, false);
        r.sizeDelta = new Vector2(Холст, Холст) * масштаб;
        var img = go.AddComponent<Image>();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.color = цвет; img.raycastTarget = false;
        return go;
    }

    // Диск (или кольцо, если внутр > 0) с мягким краем: белосерый круг иконки.
    static Texture2D Диск(int N, float внешн, float кайма)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float c = (N - 1) * 0.5f, R = c * внешн;
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(R - r);                                   // мягкий край
                Color col = Круг;
                if (кайма > 0f && r > R * (1f - кайма)) col = Color.Lerp(Круг, new Color(0.55f, 0.58f, 0.62f), 0.8f);   // тёмная кромка
                if (кайма <= 0f) col = Color.white;                               // кольцо подсветки (цвет — у Image)
                col.a *= a;
                px[y * N + x] = col;
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    // Знак «открытая дверь»: проём (рамка) и распахнутая створка в перспективе с ручкой.
    static Texture2D ДверьОткрытая(int N)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        Color32 пусто = new Color32(0, 0, 0, 0), тёмный = ЗнакЦвет;
        float s = N / 128f;
        Vector2[] створка = { new Vector2(42, 26) * s, new Vector2(42, 102) * s, new Vector2(70, 92) * s, new Vector2(70, 34) * s };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2(x, y);
                bool рамка = (x >= 42 * s && x <= 88 * s && y >= 26 * s && y <= 102 * s) &&
                             !(x > 47 * s && x < 83 * s && y > 31 * s && y < 97 * s);
                bool лист = ВЧетырёхугольнике(p, створка);
                bool ручка = (p - new Vector2(64, 64) * s).sqrMagnitude < 9f * s * s;
                px[y * N + x] = ручка ? new Color32(220, 222, 226, 255) : (рамка || лист) ? тёмный : пусто;
            }
        tex.SetPixels32(px); tex.Apply();
        return tex;
    }

    static bool ВЧетырёхугольнике(Vector2 p, Vector2[] q)
    {
        bool знак = false;
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = q[i], b = q[(i + 1) % 4];
            float кр = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            if (i == 0) знак = кр >= 0f; else if ((кр >= 0f) != знак) return false;
        }
        return true;
    }
}
