using UnityEngine;
using UnityEngine.InputSystem;

public class LeverController : MonoBehaviour
{
    public enum LeverAxis { Forward, Vertical, Yaw }

    public LeverAxis axis = LeverAxis.Forward;

    [Header("Angles")]
    public float minAngle = -45f;
    public float maxAngle = 45f;

    [Header("Input")]
    public Key keyPositive = Key.I;
    public Key keyNegative = Key.K;

    [Header("Settings")]
    public bool invert = false; // инверсия рычага

    private float currentAngle = 0f;

    void Update()
    {
        float speed = 60f;
        float direction = invert ? -1f : 1f;

        // Управление
        if (Keyboard.current[keyPositive].isPressed)
            currentAngle += speed * Time.deltaTime * direction;

        if (Keyboard.current[keyNegative].isPressed)
            currentAngle -= speed * Time.deltaTime * direction;

        // Ограничение угла
        currentAngle = Mathf.Clamp(currentAngle, minAngle, maxAngle);

        // Финальный угол (инверсия визуала)
        float finalAngle = currentAngle;

        // Вращение
        if (axis == LeverAxis.Yaw)
            transform.localEulerAngles = new Vector3(-finalAngle, 0f, 0f);
        else
            transform.localEulerAngles = new Vector3(0f, 0f, -finalAngle);

        // Передача значения (тоже учитываем инверсию)
        float normalized = Mathf.InverseLerp(minAngle, maxAngle, currentAngle) * 2f - 1f;

        if (invert)
            normalized *= -1f;

        WriteToInput(normalized);
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