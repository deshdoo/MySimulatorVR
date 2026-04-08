using UnityEngine;
using UnityEngine.InputSystem;

public class ManipulatorOpenClose : MonoBehaviour
{
    public InputActionReference toggleAction;

    private bool isClosed = false;

    private void OnEnable()
    {
        toggleAction.action.performed += OnToggle;
        toggleAction.action.Enable();
    }

    private void OnDisable()
    {
        toggleAction.action.performed -= OnToggle;
        toggleAction.action.Disable();
    }

    private void OnToggle(InputAction.CallbackContext ctx)
    {
        isClosed = !isClosed;

        if (isClosed)
            Debug.Log("CLOSE");
        else
            Debug.Log("OPEN");
    }
}