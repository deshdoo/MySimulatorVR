using UnityEngine;
using UnityEngine.InputSystem;

// Raycast от контроллеров + emission подсветка + VR Grab (grip + движение руки)
public class LeverController : MonoBehaviour
{
    public enum LeverAxis { Forward, Vertical, Yaw }

    public LeverAxis axis = LeverAxis.Forward;

    [Header("Angles")]
    public float minAngle = -45f;
    public float maxAngle = 45f;

    [Header("Keyboard (editor test)")]
    public Key keyPositive = Key.I;
    public Key keyNegative = Key.K;

    [Header("Settings")]
    public bool invert = false;

    [Header("VR Highlight")]
    [Tooltip("Первый меш рычага (например Cylinder)")]
    public Renderer leverRenderer;
    [Tooltip("Второй меш рычага (например tripo)")]
    public Renderer leverRenderer2;
    public Color highlightColor = new Color(0.3f, 0.7f, 1f);
    [Range(0f, 3f)] public float highlightIntensity = 1f;

    [Header("VR Controllers")]
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;
    [Tooltip("Дальность луча от контроллера")]
    public float rayDistance = 5f;

    [Header("VR Grab")]
    [Tooltip("Сколько метров движения руки = полный ход рычага (от minAngle до maxAngle)")]
    public float grabRange = 0.25f;
    [Tooltip("По какой оси считать движение руки: Camera Forward — относительно взгляда")]
    public bool useCameraForward = true;

    private float _angle;
    private bool _hoveredLeft;
    private bool _hoveredRight;

    private readonly System.Collections.Generic.List<Material> _mats = new System.Collections.Generic.List<Material>();
    private readonly System.Collections.Generic.List<Color> _origEmissions = new System.Collections.Generic.List<Color>();
    private readonly System.Collections.Generic.List<bool> _hadEmissions = new System.Collections.Generic.List<bool>();

    private InputAction _leftStick;
    private InputAction _rightStick;
    private InputAction _leftGrip;
    private InputAction _rightGrip;

    // Grab state
    private Transform _grabbingHand;       // null если никто не схватил
    private Vector3 _grabStartHandPos;     // позиция руки в момент захвата
    private float _grabStartAngle;         // угол рычага в момент захвата
    private Camera _mainCam;

    // Глобальный реестр: какой контроллер сейчас держит какой рычаг.
    // 1 контроллер = максимум 1 рычаг. Два контроллера = максимум 2 разных рычага.
    private static readonly System.Collections.Generic.Dictionary<Transform, LeverController> _activeGrabs
        = new System.Collections.Generic.Dictionary<Transform, LeverController>();

    void Awake()
    {
        Debug.Log($"[LeverController v5-GRAB] Awake on {gameObject.name}");

        _leftStick = new InputAction(binding: "<XRController>{LeftHand}/thumbstick");
        _rightStick = new InputAction(binding: "<XRController>{RightHand}/thumbstick");
        _leftGrip = new InputAction(binding: "<XRController>{LeftHand}/grip");
        _rightGrip = new InputAction(binding: "<XRController>{RightHand}/grip");
        _leftStick.Enable();
        _rightStick.Enable();
        _leftGrip.Enable();
        _rightGrip.Enable();

        _mainCam = Camera.main;

        CacheMaterial(leverRenderer);
        CacheMaterial(leverRenderer2);

        if (_mats.Count == 0)
            Debug.LogWarning($"[LeverController] ни один Renderer не назначен на {gameObject.name}");
    }

    void CacheMaterial(Renderer r)
    {
        if (r == null) return;
        var m = r.material;
        _mats.Add(m);
        _hadEmissions.Add(m.IsKeywordEnabled("_EMISSION"));
        _origEmissions.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);
    }

    void OnDestroy()
    {
        _leftStick?.Dispose();
        _rightStick?.Dispose();
        _leftGrip?.Dispose();
        _rightGrip?.Dispose();
    }

    void Update()
    {
        _hoveredLeft  = CheckRay(leftControllerTransform);
        _hoveredRight = CheckRay(rightControllerTransform);

        bool hovered = _hoveredLeft || _hoveredRight;
        bool isGrabbing = _grabbingHand != null;
        // Подсветка: когда наводишь ИЛИ держишь
        ApplyHighlight(hovered || isGrabbing);

        // === VR Grab logic ===
        bool leftGrip  = _leftGrip.ReadValue<float>()  > 0.5f;
        bool rightGrip = _rightGrip.ReadValue<float>() > 0.5f;

        if (_grabbingHand == null)
        {
            // Начинаем захват только если наводимся И зажат grip И этот контроллер ещё свободен
            if (_hoveredLeft && leftGrip && leftControllerTransform != null
                && !_activeGrabs.ContainsKey(leftControllerTransform))
                StartGrab(leftControllerTransform);
            else if (_hoveredRight && rightGrip && rightControllerTransform != null
                && !_activeGrabs.ContainsKey(rightControllerTransform))
                StartGrab(rightControllerTransform);
        }
        else
        {
            // Отпустили grip — релиз
            bool stillGripping =
                (_grabbingHand == leftControllerTransform  && leftGrip)
             || (_grabbingHand == rightControllerTransform && rightGrip);
            if (!stillGripping) ReleaseGrab();
        }

        // === Применение углов ===
        if (_grabbingHand != null)
        {
            // Прямое VR-управление: угол = функция смещения руки от точки захвата
            Vector3 delta = _grabbingHand.position - _grabStartHandPos;

            // Для Yaw (3 рычаг) используем горизонтальную ось (лево/право),
            // для Forward/Vertical — глубинную (вперёд/назад)
            Vector3 dirAxis;
            if (axis == LeverAxis.Yaw)
            {
                dirAxis = (useCameraForward && _mainCam != null)
                    ? _mainCam.transform.right
                    : Vector3.right;
            }
            else
            {
                dirAxis = (useCameraForward && _mainCam != null)
                    ? _mainCam.transform.forward
                    : Vector3.forward;
            }
            dirAxis.y = 0f;
            dirAxis = dirAxis.sqrMagnitude > 0.001f ? dirAxis.normalized : Vector3.forward;

            float along = Vector3.Dot(delta, dirAxis);
            // Нормализуем: grabRange метров = полный ход (-1..1)
            float t = Mathf.Clamp(along / grabRange, -1f, 1f);
            float dirSign = invert ? -1f : 1f;
            _angle = _grabStartAngle + t * (maxAngle - minAngle) * 0.5f * dirSign;
            _angle = Mathf.Clamp(_angle, minAngle, maxAngle);
        }
        else
        {
            // Обычный режим — клавиатура и стик при наведении
            float input = 0f;
            if (hovered)
            {
                if (Keyboard.current != null)
                {
                    if (Keyboard.current[keyPositive].isPressed) input += 1f;
                    if (Keyboard.current[keyNegative].isPressed) input -= 1f;
                }
                if (_hoveredLeft)  input += _leftStick.ReadValue<Vector2>().y;
                if (_hoveredRight) input += _rightStick.ReadValue<Vector2>().y;
                input = Mathf.Clamp(input, -1f, 1f);
            }
            float dirSign = invert ? -1f : 1f;
            _angle += input * 60f * Time.deltaTime * dirSign;
            _angle = Mathf.Clamp(_angle, minAngle, maxAngle);
        }

        transform.localEulerAngles = axis == LeverAxis.Yaw
            ? new Vector3(-_angle, 0f, 0f)
            : new Vector3(0f, 0f, -_angle);

        float normalized = Mathf.InverseLerp(minAngle, maxAngle, _angle) * 2f - 1f;
        if (invert) normalized *= -1f;
        WriteToInput(normalized);
    }

    void StartGrab(Transform hand)
    {
        _grabbingHand = hand;
        _grabStartHandPos = hand.position;
        _grabStartAngle = _angle;
        _activeGrabs[hand] = this;
    }

    void ReleaseGrab()
    {
        if (_grabbingHand != null && _activeGrabs.TryGetValue(_grabbingHand, out var owner) && owner == this)
            _activeGrabs.Remove(_grabbingHand);
        _grabbingHand = null;
    }

    void OnDisable()
    {
        ReleaseGrab();
    }

    bool CheckRay(Transform controller)
    {
        if (controller == null) return false;
        Ray ray = new Ray(controller.position, controller.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance))
            return hit.collider != null && hit.collider.transform.IsChildOf(transform);
        return false;
    }

    void ApplyHighlight(bool on)
    {
        for (int i = 0; i < _mats.Count; i++)
        {
            var m = _mats[i];
            if (m == null) continue;
            if (on)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", highlightColor * highlightIntensity);
            }
            else
            {
                if (!_hadEmissions[i]) m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", _origEmissions[i]);
            }
        }
    }

    void WriteToInput(float value)
    {
        switch (axis)
        {
            case LeverAxis.Forward:  DroneInput.forward  = value; break;
            case LeverAxis.Vertical: DroneInput.vertical = value; break;
            case LeverAxis.Yaw:      DroneInput.yaw      = value; break;
        }
    }
}
