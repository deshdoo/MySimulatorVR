using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

// Тело оператора в кабине — для контроллеров (у рук стиков нет, по кабине ходят ногами):
//   • левый стик — шаг по кабине, медленно (0,6 м/с), с шагом вбок; стены и предметы не
//     пускают — риг с CharacterController, голову держит CabinBounds;
//   • правый стик — поворот рывком на 30° (плавный поворот в VR укачивает);
//   • нажатие левого стика — назад в кресло пилота (где голова была в начале миссии);
//   • Y — справка «?» (окно среды и работы приборов).
// Из всего передвижения XRI включены только шаг (XRI Left Locomotion/Move) и поворот
// рывком (XRI Right Locomotion/Snap Turn); телепорт, «тащить мир» за Grip, прыжок на A,
// поворот левым стиком выключены (ControllerBindings). Проверяется каждый кадр: риг сам
// включает свои действия, когда гарнитура снова видит контроллер.
// В меню не работает — там передвижение выключает MainMenu.
public class ХодьбаПоКабине : MonoBehaviour
{
    const float Скорость_мс = 0.6f, Поворот_град = 30f;
    static readonly string[] Включить = { "XRI Left Locomotion/Move", "XRI Right Locomotion/Snap Turn" };

    XROrigin _риг;
    float _следПоиск;
    bool _былоМеню = true;
    Vector3 _кресло, _креслоВперёд;   // голова пилота в начале миссии
    bool _естьКресло;
    static ХодьбаПоКабине _я;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Запуск()
    {
        if (_я != null) return;
        var go = new GameObject("ХодьбаПоКабине");
        DontDestroyOnLoad(go);
        _я = go.AddComponent<ХодьбаПоКабине>();
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= _следПоиск || _риг == null) Найти();
        bool меню = MainMenu.Открыто;
        // Пока ждём кнопку для переназначения (меню или настройки в миссии), действия не
        // включаем: Input System переназначает только выключенное действие.
        Действия(!меню && !НастройкиВИгре.ЖдёмКнопку);
        if (_риг == null || _риг.Camera == null) return;

        if (_былоМеню && !меню) Invoke(nameof(ЗапомнитьКресло), 0.5f);   // после возврата из меню в кабину
        _былоМеню = меню;
        if (меню || MissionManager.Instance == null || НастройкиВИгре.ЖдёмКнопку) return;

        if (ControllerBindings.ВКресло.WasPressedThisFrame() && _естьКресло) ВКресло();
        if (ControllerBindings.Справка.WasPressedThisFrame()) CabinPanels.ПереключитьСреду();
    }

    void Найти()
    {
        _следПоиск = Time.unscaledTime + 1f;   // риг меняется при загрузке сцены
        _риг = FindFirstObjectByType<XROrigin>();
        if (_риг == null) return;
        foreach (var п in _риг.GetComponentsInChildren<ContinuousMoveProvider>(true))
        {
            п.moveSpeed = Скорость_мс;
            п.enableStrafe = true;
            п.enableFly = false;
        }
        foreach (var п in _риг.GetComponentsInChildren<SnapTurnProvider>(true))
        {
            п.turnAmount = Поворот_град;
            п.enableTurnLeftRight = true;
            п.enableTurnAround = false;
        }
    }

    // Передвижение XRI: всё выключено, кроме шага и поворота рывком (их — только если можно).
    static void Действия(bool можноВключать)
    {
        var xri = ControllerBindings.XRI;
        if (xri == null) return;
        foreach (var имяКарты in new[] { "XRI Left Locomotion", "XRI Right Locomotion" })
        {
            var карта = xri.FindActionMap(имяКарты);
            if (карта == null) continue;
            foreach (var a in карта.actions)
            {
                bool нужно = System.Array.IndexOf(Включить, имяКарты + "/" + a.name) >= 0;
                if (нужно && !можноВключать) continue;
                if (нужно && !a.enabled) a.Enable();
                else if (!нужно && a.enabled) a.Disable();
            }
        }
    }

    void ЗапомнитьКресло()
    {
        if (_риг == null || _риг.Camera == null || MainMenu.Открыто) return;
        var г = _риг.Camera.transform;
        _кресло = г.position;
        _креслоВперёд = Vector3.ProjectOnPlane(г.forward, Vector3.up);
        _естьКресло = _креслоВперёд.sqrMagnitude > 1e-4f;
    }

    // Как возврат из меню (MainMenu.ВернутьВКабину): поворот вокруг головы к прежнему
    // взгляду и сдвиг по полу на прежнее место; высота головы своя.
    void ВКресло()
    {
        var г = _риг.Camera.transform;
        Vector3 сейчас = Vector3.ProjectOnPlane(г.forward, Vector3.up);
        if (сейчас.sqrMagnitude > 1e-4f)
            _риг.transform.RotateAround(г.position, Vector3.up, Vector3.SignedAngle(сейчас, _креслоВперёд, Vector3.up));
        Vector3 сдвиг = _кресло - г.position;
        сдвиг.y = 0f;
        var тело = _риг.GetComponentInChildren<CharacterController>();
        bool былоВкл = тело != null && тело.enabled;
        if (былоВкл) тело.enabled = false;   // иначе CharacterController вернёт риг на старое место
        _риг.transform.position += сдвиг;
        if (былоВкл) тело.enabled = true;
        UiSound.Клик();
    }
}
