using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RunAcross.CameraRig
{
    /// <summary>
    /// Switches between the fixed look-dev shot (matches the Blender render) and the third-person
    /// player camera. Tab toggles. In Edit mode the look-dev shot wins, so screenshots frame the hero view.
    /// </summary>
    public class CameraModeToggle : MonoBehaviour
    {
        public CinemachineCamera lookDevCamera;
        public CinemachineCamera playerCamera;
        public bool startInPlayerMode = true;

        bool _playerMode;

        void Start() => SetPlayerMode(startInPlayerMode);

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                SetPlayerMode(!_playerMode);
        }

        void SetPlayerMode(bool on)
        {
            _playerMode = on;
            playerCamera.Priority = on ? 20 : 0;
            lookDevCamera.Priority = on ? 0 : 20;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }
    }
}
