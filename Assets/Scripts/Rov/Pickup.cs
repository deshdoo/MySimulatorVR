using UnityEngine;

namespace RovSim.Rov
{
    public class Pickup : MonoBehaviour
    {
        public Transform holdParent;
        [SerializeField] private bool canGrab;

        private const float MoveForce = 250f;
        private GameObject _heldObj;
        private GameObject _body;
        private Rigidbody _temp;

        // Захват завязан на то же состояние клешни, что и анимация с кнопкой:
        //   клешня сжата (RovSystems.grabberClosed) + касание Grabbable -> берём объект,
        //   клешня разжата -> отпускаем. Отдельные клавиши close/open больше не нужны.
        private void Update()
        {
            if (RovSystems.grabberClosed)
            {
                if (_heldObj is null && canGrab && _body != null)
                {
                    PickupObject(_body);
                    _temp = _body.GetComponent<Rigidbody>();
                    if (_temp != null) _temp.constraints = RigidbodyConstraints.FreezeAll;
                    Debug.Log("picked up");
                }
            }
            else if (!(_heldObj is null))
            {
                DropObject();
                if (_temp != null) _temp.constraints = RigidbodyConstraints.None;
            }

            if (!(_heldObj is null))
            {
                MoveObject();
            }
        }

        private void MoveObject()
        {
            if (Vector3.Distance(_heldObj.transform.position, holdParent.position) > 0.1f)
            {
                var moveDirection = (holdParent.position - _heldObj.transform.position);
                _heldObj.GetComponent<Rigidbody>().AddForce(moveDirection * MoveForce);
            }
        }

        private void PickupObject(GameObject pickObj)
        {
            if (pickObj.GetComponent<Rigidbody>())
            {
                var objBody = pickObj.GetComponent<Rigidbody>();
                objBody.linearDamping = 10;

                objBody.transform.parent = holdParent;
                _heldObj = pickObj;
            }
        }

        private void DropObject()
        {
            var heldRig = _heldObj.GetComponent<Rigidbody>();
            heldRig.linearDamping = 10;

            _heldObj.transform.parent = null;
            _heldObj = null;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("Grabbable") || collision.gameObject.CompareTag("GrabbableFalse"))
            {
                canGrab = true;
                _body = collision.gameObject;
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            if (collision.gameObject.CompareTag("Grabbable") || collision.gameObject.CompareTag("GrabbableFalse"))
            {
                canGrab = false;
                _body = null;
            }
        }
    }
}
