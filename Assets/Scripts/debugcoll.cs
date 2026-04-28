using UnityEngine;

public class DebugCollision : MonoBehaviour
{
    CharacterController cc;
    void Start() {
        cc = GetComponent<CharacterController>();
        Debug.Log("CC enabled: " + cc.enabled);
        Debug.Log("CC radius: " + cc.radius);
        Debug.Log("CC height: " + cc.height);
        Debug.Log("CC isGrounded: " + cc.isGrounded);
    }
}