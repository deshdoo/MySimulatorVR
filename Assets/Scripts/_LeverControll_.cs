using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// Не зависит от XRI — использует собственный Raycast от контроллеров
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

    [Header("VR Outline Highlight")]
    public Color outlineColor = new Color(0.3f, 0.7f, 1f);
    [Range(1.01f, 1.3f)] public float outlineScale = 1.08f;

    [Header("VR Controllers (assign in Inspector)")]
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;
    [Tooltip("Дальность луча от контроллера")]
    public float rayDistance = 5f;

    private float _angle;
    private bool _hoveredLeft;
    private bool _hoveredRight;
    private readonly List<GameObject> _outlineObjects = new List<GameObject>();

    private InputAction _leftStick;
    private InputAction _rightStick;

    void Awake()
    {
        _leftStick = new InputAction(binding: "<XRController>{LeftHand}/thumbstick");
        _rightStick = new InputAction(binding: "<XRController>{RightHand}/thumbstick");
        _leftStick.Enable();
        _rightStick.Enable();

        BuildOutlines();
    }

    void OnDestroy()
    {
        _leftStick?.Dispose();
        _rightStick?.Dispose();
    }

    // Создаём outline-клон для каждого MeshFilter в дочерних объектах
    void BuildOutlines()
    {
        // Берём все MeshFilter-ы внутри LeverPivot, кроме самого себя
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);

        Material outlineMat = CreateOutlineMaterial();

        foreach (var mf in filters)
        {
            if (mf.sharedMesh == null) continue;

            var outlineGO = new GameObject("_Outline_" + mf.gameObject.name);
            outlineGO.transform.SetParent(mf.transform, false);
            // Чуть больше оригинала чтобы торчал контур снаружи
            outlineGO.transform.localScale = Vector3.one * outlineScale;

            var outlineMF = outlineGO.AddComponent<MeshFilter>();
            outlineMF.sharedMesh = mf.sharedMesh;

            var outlineMR = outlineGO.AddComponent<MeshRenderer>();
            outlineMR.material = outlineMat;
            outlineMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineMR.receiveShadows = false;

            outlineGO.SetActive(false);
            _outlineObjects.Add(outlineGO);
        }
    }

    Material CreateOutlineMaterial()
    {
        // URP Unlit с Cull Front — рисует только внешние грани (и получается контур)
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.name = "OutlineMat";
        // Cull Front = 1: отбрасываем передние грани, видны только задние — эффект outline
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
        mat.SetColor("_BaseColor", outlineColor);
        // Рисуем поверх всего чтобы не резало Z-буфер
        mat.renderQueue = 3001;
        return mat;
    }

    void Update()
    {
        _hoveredLeft  = CheckRay(leftControllerTransform);
        _hoveredRight = CheckRay(rightControllerTransform);

        bool hovered = _hoveredLeft || _hoveredRight;
        SetOutline(hovered);

        float input = 0f;

        if (Keyboard.current != null)
        {
            if (Keyboard.current[keyPositive].isPressed) input += 1f;
            if (Keyboard.current[keyNegative].isPressed) input -= 1f;
        }

        if (_hoveredLeft)  input += _leftStick.ReadValue<Vector2>().y;
        if (_hoveredRight) input += _rightStick.ReadValue<Vector2>().y;
        input = Mathf.Clamp(input, -1f, 1f);

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
            return hit.collider != null && hit.collider.transform.IsChildOf(transform.parent ?? transform);
        return false;
    }

    void SetOutline(bool on)
    {
        foreach (var go in _outlineObjects)
            if (go != null) go.SetActive(on);
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
