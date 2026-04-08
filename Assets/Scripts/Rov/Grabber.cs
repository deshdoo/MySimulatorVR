using UnityEngine;

namespace RovSim.Rov
{
	public class Grabber : MonoBehaviour
	{
		private Animator _animator;
		private InputDetector _inputDetector;
		private bool _isClosed;
		private bool _lastButtonState;

		private void Awake()
		{
			_inputDetector = GetComponent<InputDetector>();
		}


		private void Start()
		{
			_animator = GetComponent<Animator>();
		}


		private void Update()
		{
			Grab();
		}

		private void Grab()
		{
			_animator.SetBool("Open Grabber", ! _inputDetector.GrabberClosed);
		}
	}
}
