using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

// Видео рук: в режиме «Руки» на месте рук видно не модель, а настоящие руки — картинку
// с камер гарнитуры (passthrough), вырезанную по форме кисти (до запястья: дальше XR
// Hands руку не отслеживает). Всё вокруг — игра, как раньше.
//
// Как устроено:
//   • пакет Unity OpenXR: Meta + AR Foundation: ARSession и ARCameraManager на камере
//     рига включают видео с камер (функции OpenXR включает редактор сам —
//     ВидеоРукНастройка). Гарнитура кладёт видео ПОД кадр игры и смешивает по альфе;
//   • перед камерой — квадрат с шейдером AlphaOne: альфа кадра = 1 везде (иначе видео
//     проступало бы сквозь полупрозрачные окна и частицы);
//   • руки рисуются шейдером HandPassthrough: внутри альфа 0 (видно камеру), по краю —
//     мягкий светящийся ободок. Материал рук меняет РукиПризраки.
// Только на самой гарнитуре (APK на Quest): в редакторе и через Quest Link видео с камер
// пакетом Meta не передаётся — там руки остаются «призрачными».
public class ВидеоРук : MonoBehaviour
{
    // Видео с камер реально идёт — руки надо рисовать «окном» (читает РукиПризраки).
    public static bool Активно { get; private set; }
    public static Material МатРуки { get; private set; }

    ARSession _сессия;
    ARCameraManager _камера;
    XROrigin _риг;
    GameObject _альфа;
    bool _постБыл, _постВыключен;
    float _след;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Создать()
    {
        if (Application.platform != RuntimePlatform.Android) return;   // видео с камер — только на гарнитуре
        if (FindFirstObjectByType<ВидеоРук>() != null) return;
        var go = new GameObject("ВидеоРук");
        DontDestroyOnLoad(go);
        go.AddComponent<ВидеоРук>();
    }

    void Update()
    {
        if (Time.unscaledTime < _след) return;
        _след = Time.unscaledTime + 0.5f;

        bool нужно = GameSettings.РежимВвода == GameSettings.Ввод.Руки;
        if (_риг == null || _риг.Camera == null) { _риг = FindFirstObjectByType<XROrigin>(); _камера = null; _альфа = null; }
        if (_риг == null || _риг.Camera == null) { Активно = false; return; }
        var cam = _риг.Camera;

        if (нужно)
        {
            if (_сессия == null)
            {
                _сессия = FindFirstObjectByType<ARSession>();
                if (_сессия == null)
                {
                    var s = new GameObject("AR Session (видео рук)");
                    DontDestroyOnLoad(s);
                    _сессия = s.AddComponent<ARSession>();
                }
            }
            _сессия.enabled = true;
            if (_камера == null) _камера = cam.GetComponent<ARCameraManager>();
            if (_камера == null) _камера = cam.gameObject.AddComponent<ARCameraManager>();
            _камера.enabled = true;
            ВыключитьПост(cam, true);
        }
        else
        {
            if (_камера != null) _камера.enabled = false;
            ВыключитьПост(cam, false);
        }

        bool было = Активно;
        Активно = нужно && _камера != null && _камера.subsystem != null && _камера.subsystem.running;
        if (Активно != было) Debug.Log($"[Видео рук] Видео с камер {(Активно ? "идёт — руки показываются вживую" : "выключено — руки-призраки")}.");
        Квадрат(cam).SetActive(Активно);
        if (Активно && МатРуки == null)
        {
            var sh = Shader.Find("Custom/HandPassthrough");
            if (sh != null) МатРуки = new Material(sh) { name = "Рука: видео (auto)" };
            else { Активно = false; Debug.LogWarning("[Видео рук] Нет шейдера Custom/HandPassthrough."); }
        }
    }

    // Постобработка камеры рига стирает альфу кадра (в URP выключен вывод альфы после
    // неё) — на время видео рук она выключается, потом возвращается как была.
    void ВыключитьПост(Camera cam, bool выключить)
    {
        var данные = cam.GetUniversalAdditionalCameraData();
        if (данные == null) return;
        if (выключить && !_постВыключен) { _постБыл = данные.renderPostProcessing; данные.renderPostProcessing = false; _постВыключен = true; }
        else if (!выключить && _постВыключен) { данные.renderPostProcessing = _постБыл; _постВыключен = false; }
    }

    // Квадрат AlphaOne прямо перед камерой, с запасом шире поля зрения обоих глаз.
    GameObject Квадрат(Camera cam)
    {
        if (_альфа != null) return _альфа;
        _альфа = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _альфа.name = "Альфа кадра = 1 (видео рук)";
        Destroy(_альфа.GetComponent<Collider>());
        _альфа.transform.SetParent(cam.transform, false);
        float d = cam.nearClipPlane + 0.01f;
        _альфа.transform.localPosition = new Vector3(0f, 0f, d);
        _альфа.transform.localScale = Vector3.one * d * 8f;
        var r = _альфа.GetComponent<MeshRenderer>();
        var sh = Shader.Find("Custom/AlphaOne");
        if (sh != null) r.sharedMaterial = new Material(sh) { name = "Альфа = 1 (auto)" };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _альфа.layer = cam.gameObject.layer;
        _альфа.SetActive(false);
        return _альфа;
    }
}
