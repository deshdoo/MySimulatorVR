using UnityEngine;

namespace RovSim.Rov
{
	// Прямое управление челюстями клешни по общему флагу RovSystems.grabberClosed —
	// без Animator/клипов (та схема с состояниями Idle/Close/Open была ненадёжной и
	// залипала). Стартовая поза каждой челюсти считается «открытой»; для закрытого
	// состояния задаётся локальный доворот в градусах. Между ними — плавная
	// интерполяция. Флаг переключают клавиша (InputDetector) и кнопка пульта
	// (RovPokeButton, action = ToggleGrabber).
	//
	// Настройка на объекте rov_gripper:
	//   Jaws               = подвижные челюсти (например arm.001 и arm.002).
	//   Closed Local Euler = поворот КАЖДОЙ челюсти в сжатом положении (по одному на челюсть).
	//                        Обычно у двух челюстей знак угла противоположный (напр. (0,0,30) и (0,0,-30)).
	//   Ось (X/Y/Z) и величину подбери в Play: меняй угол, пока створки не сойдутся.
	// Если на объекте остался старый Animator — его можно отключить/удалить: этот
	// скрипт работает в LateUpdate и всё равно перекрывает его поворотами.
	public class Grabber : MonoBehaviour
	{
		[Tooltip("Подвижные челюсти клешни (например arm.001, arm.002).")]
		public Transform[] jaws;

		[Tooltip("Локальный доворот КАЖДОЙ челюсти в СЖАТОМ состоянии, градусы (Euler), " +
		         "относительно её стартовой (открытой) позы. По одному элементу на челюсть.")]
		public Vector3[] closedLocalEuler;

		[Tooltip("Скорость сжатия/разжатия (доля полного хода в секунду).")]
		public float speed = 6f;

		private Quaternion[] _openRot;
		private float _t; // 0 = открыто, 1 = сжато

		private void Awake()
		{
			if (jaws == null) return;
			_openRot = new Quaternion[jaws.Length];
			for (int i = 0; i < jaws.Length; i++)
				if (jaws[i] != null) _openRot[i] = jaws[i].localRotation;
			_t = RovSystems.grabberClosed ? 1f : 0f;
		}

		// LateUpdate — после аниматора, чтобы перекрыть его, если он остался на объекте.
		private void LateUpdate()
		{
			if (_openRot == null) return;

			float target = RovSystems.grabberClosed ? 1f : 0f;
			_t = Mathf.MoveTowards(_t, target, speed * Time.deltaTime);

			for (int i = 0; i < jaws.Length; i++)
			{
				if (jaws[i] == null) continue;
				Vector3 e = (closedLocalEuler != null && i < closedLocalEuler.Length)
					? closedLocalEuler[i] : Vector3.zero;
				jaws[i].localRotation = _openRot[i] * Quaternion.Euler(e * _t);
			}
		}
	}
}
