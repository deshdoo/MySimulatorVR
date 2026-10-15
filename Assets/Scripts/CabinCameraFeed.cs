using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Видео рабочих камер «Марлина» на мониторах пульта (сцена Cabin):
//   Monitor2 — камера КОННЕКТОРА, Monitor3 — камера ЗАХВАТА.
// Видео появляется поверх содержимого монитора (графики/гидролокатор, прогресс миссии),
// когда его велит показать коммутатор камер на аппарате (RovCameraRig: авто по действиям
// оператора, клавиша V или кнопка пульта ToggleCameras), и пропадает, когда не нужно.
// Подключается к мониторам сам при загрузке сцены — в сцене ничего настраивать не надо.
public class CabinCameraFeed : MonoBehaviour
{
    public enum Канал { Захват, Коннектор }
    public Канал канал;

    GameObject _вставка;
    RawImage _видео;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Подписаться()
    {
        SceneManager.sceneLoaded += (s, _) => Подключить();
        Подключить();
    }

    static void Подключить()
    {
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Канал? к = canvas.name == "Monitor2" ? Канал.Коннектор : canvas.name == "Monitor3" ? Канал.Захват : (Канал?)null;
            if (к == null || canvas.GetComponent<CabinCameraFeed>() != null) continue;
            var f = canvas.gameObject.AddComponent<CabinCameraFeed>();
            f.канал = к.Value;
        }
    }

    void Start()
    {
        var корень = (RectTransform)transform;
        _вставка = new GameObject("Видео камеры " + канал, typeof(RectTransform));
        var rt = (RectTransform)_вставка.transform;
        rt.SetParent(корень, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling();   // поверх графиков/гидролокатора/прогресса миссии
        _вставка.layer = gameObject.layer;

        var фон = Создать<Image>("Фон", rt); фон.color = Color.black; Растянуть(фон.rectTransform);
        _видео = Создать<RawImage>("Видео", rt); Растянуть(_видео.rectTransform);
        ДобавитьПомехи(rt);

        _вставка.SetActive(false);
    }

    void Update()
    {
        var rig = RovCameraRig.Instance;
        bool показать = rig != null && (канал == Канал.Захват ? rig.ПоказатьЗахват : rig.ПоказатьКоннектор);
        if (_вставка == null) return;
        if (_вставка.activeSelf != показать)
        {
            _вставка.SetActive(показать);
            if (показать) _вставка.transform.SetAsLastSibling();   // видео — поверх гидролокатора и графиков
        }
        if (показать)
        {
            var видео = канал == Канал.Захват ? rig.ВидеоЗахват : rig.ВидеоКоннектор;
            if (_видео.texture != видео) _видео.texture = видео;
        }
    }

    // Помехи поверх видео — как на основном экране (NoiseTV): тот же видео-шум, а его
    // прозрачность по уровню повреждения камеры задаёт тот же MonitorNoiseOverlay.
    void ДобавитьПомехи(RectTransform rt)
    {
        RawImage образец = null; MonitorNoiseOverlay настройки = null;
        foreach (var o in Object.FindObjectsByType<MonitorNoiseOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (o.transform.IsChildOf(transform)) continue;
            var ri = o.GetComponent<RawImage>() ?? o.GetComponentInChildren<RawImage>(true);
            if (ri != null && ri.texture != null) { образец = ri; настройки = o; break; }
        }
        if (образец == null) return;
        var помехи = Создать<RawImage>("Помехи", rt); Растянуть(помехи.rectTransform);
        помехи.texture = образец.texture;
        помехи.material = образец.material;
        помехи.raycastTarget = false;
        var c = образец.color; c.a = 0f; помехи.color = c;
        var шум = помехи.gameObject.AddComponent<MonitorNoiseOverlay>();
        шум.слойНаложения = помехи;
        шум.альфаПриНорме = настройки.альфаПриНорме;
        шум.альфаПриWarning = настройки.альфаПриWarning;
        шум.альфаПриCritical = настройки.альфаПриCritical;
        шум.скоростьПерехода = настройки.скоростьПерехода;
    }

    static T Создать<T>(string имя, Transform родитель) where T : Component
    {
        var go = new GameObject(имя, typeof(RectTransform), typeof(T));
        go.transform.SetParent(родитель, false);
        go.layer = родитель.gameObject.layer;
        return go.GetComponent<T>();
    }

    static void Растянуть(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
    }
}
