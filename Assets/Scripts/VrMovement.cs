using UnityEngine;
using UnityEngine.InputSystem;

namespace RovSim.Rov
{
    [RequireComponent(typeof(Rigidbody))]
    public class VrMovement : MonoBehaviour
    {
        [Header("Настройки тяги (из Оригинала)")]
        public float verticalThrust = 10.0f;
        public float horizontalThrust = 10.0f;
        public float rotationSensitivity = 1.0f;

        [Header("VR Стики (Input Actions)")]
        // Левый стик: Вперед/Назад (Y) и Стрейф влево/вправо (X)
        public InputActionProperty moveAction; 
        // Правый стик: Вверх/Вниз (Y) и Поворот влево/вправо (X)
        public InputActionProperty turnAction;

        [Header("Кнопки Манипулятора (F и G)")]
        public InputActionProperty grabAction;   // Например, кнопка А (или клавиша F)
        public InputActionProperty releaseAction; // Например, кнопка B (или клавиша G)

        private Rigidbody _body;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            // Читаем значения со стиков (от -1 до 1)
            Vector2 leftStick = moveAction.action.ReadValue<Vector2>();
            Vector2 rightStick = turnAction.action.ReadValue<Vector2>();

            // 1. Движение Вперед/Назад (Твои "Стрелочки") -> Левый стик Y
            ApplyForce(Vector3.forward * leftStick.y, horizontalThrust);

            // 2. Движение Вправо/Влево (Твои "A" и "D") -> Левый стик X
            ApplyForce(Vector3.right * leftStick.x, horizontalThrust);

            // 3. Движение Вверх/Вниз (Твои "W" и "S") -> Правый стик Y
            ApplyForce(Vector3.up * rightStick.y, verticalThrust);

            // 4. Повороты (Твои "Стрелочки влево/вправо") -> Правый стик X
            ApplyRotation(rightStick.x * rotationSensitivity);
            
            // 5. Манипулятор (проверка кнопок)
            if(grabAction.action.WasPressedThisFrame()) Debug.Log("VR: Сжать клешню (Кнопка F)");
            if(releaseAction.action.WasPressedThisFrame()) Debug.Log("VR: Разжать клешню (Кнопка G)");
        }

        private void ApplyForce(Vector3 directionVector, float thrust)
        {
            if (directionVector.magnitude < 0.1f) return; // Мертвая зона стика

            var relativeVector = _body.transform.TransformDirection(directionVector);
            _body.AddForce(relativeVector * thrust, ForceMode.Force);
        }

        private void ApplyRotation(float rotationValue)
        {
            if (Mathf.Abs(rotationValue) < 0.1f) return;

            var currentTransform = transform;
            var previousAngles = currentTransform.localEulerAngles;

            currentTransform.localEulerAngles = new Vector3(
                previousAngles.x,
                previousAngles.y + rotationValue,
                previousAngles.z
            );
        }
    }
}