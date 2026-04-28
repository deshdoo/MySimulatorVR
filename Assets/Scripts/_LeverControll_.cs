using UnityEngine;
using UnityEngine.InputSystem;

// Простая версия: Raycast от контроллеров + emission подсветка на указанном Renderer
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

    private float _angle;
    private bool _hoveredLeft;
    private bool _hoveredRight;

    private readonly System.Collections.Generic.List<Material> _mats = new System.Collections.Generic.List<Material>();
    private readonly System.Collections.Generic.List<Color> _origEmissions = new System.Collections.Generic.List<Color>();
    private readonly System.Collections.Generic.List<bool> _hadEmissions = new System.Collections.Generic.List<bool>();

    private InputAction _leftStick;
    private InputAction _rightStick;

    void Awake()
    {
        Debug.Log($"[LeverController v4-SIMPLE] Awake on {gameObject.name}");

        _leftStick = new InputAction(binding: "<XRController>{LeftHand}/thumbstick");
        _rightStick = new InputAction(binding: "<XRController>{RightHand}/thumbstick");
        _leftStick.Enable();
        _rightStick.Enable();

        CacheMaterial(leverRenderer);
        CacheMaterial(leverRenderer2);

        if (_mats.Count == 0)
            Debug.LogWarning($"[LeverController] ни один Renderer не назначен на {gameObject.name}");
    }

    void CacheMaterial(Renderer r)
    {
        if (r == null) return;
        var m = r.material; // инстанс
        _mats.Add(m);
        _hadEmissions.Add(m.IsKeywordEnabled("_EMISSION"));
        _origEmissions.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black);
    }

    void OnDestroy()
    {
        _leftStick?.Dispose();
        _rightStick?.Dispose();
    }

    void Update()
    {
        _hoveredLeft  = CheckRay(leftControllerTransform);
        _hoveredRight = CheckRay(rightControllerTransform);

        bool hovered = _hoveredLeft || _hoveredRight;
        ApplyHighlight(hovered);

        float input = 0f;

        // Любой ввод работает только при наведении (когда рычаг подсвечен)
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

        float dir = invert ? -1f : 1f;
        _angle += input * 60f * Time.deltaTime * dir;
        _angle = Mathf.Clamp(_angle, minAngle, maxAngle);

        transform.localEulerAngles = axis == LeverAxis.Yaw
            ? new Vector3(-_angle, 0f, 0f)
            : new Vector3(0f, 0f, -_angle);

        float normalized = Mathf.InverseLerp(minAngle, maxAngle, _angle) * 2f - 1f;
        if (invert) normalized *= -1f;
        WriteToInput(normalized);
    }

    bool CheckRay(Transform controller)
    {
        if (controller == null) return false;
        Ray ray = new Ray(controller.position, controller.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance))
        {
            // считаем попадание если коллайдер принадлежит этому рычагу или его детям
            return hit.collider != null && hit.collider.transform.IsChildOf(transform);
        }
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
