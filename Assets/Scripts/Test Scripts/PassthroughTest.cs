using CoreResources.Managers.InputManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wave.Native;

public class PassthroughTest : MonoBehaviour
{
    [SerializeField]
    private Camera _hmdCam;

    private bool _inputActionsLinked = false;
    private Color? recOriginalColor = null;

    private void Awake()
    {
        if (_hmdCam == null)
            _hmdCam = transform.GetComponent<Camera>();

        _inputActionsLinked = false;

        StartCoroutine(WaitForInputSystem());
    }

    private void OnDestroy()
    {
        if (_inputActionsLinked)
        {
            InputManager.InputActions.XRILeftHandInteraction.DebugButton1.performed -= DebugButton1_performed;
        }
    }

    private void DebugButton1_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        ShowPassthroughUnderlay(!Interop.WVR_IsPassthroughOverlayVisible());
    }

    private void ShowPassthroughUnderlay(bool status)
    {
        if (status)
        {
            _hmdCam.clearFlags = CameraClearFlags.SolidColor;

            if (recOriginalColor == null)
                recOriginalColor = _hmdCam.backgroundColor;

            _hmdCam.backgroundColor = Color.white * 0;
            Interop.WVR_SetPassthroughOverlayAlpha(0);
        }
        else
        {
            Interop.WVR_SetPassthroughOverlayAlpha(1);
            _hmdCam.clearFlags = CameraClearFlags.Skybox;

            if (recOriginalColor.HasValue)
                _hmdCam.backgroundColor = recOriginalColor.Value;
            else
                _hmdCam.backgroundColor = new Color(49f / 255f, 77f / 255f, 121f / 255f, 5f / 255f);
        }

        // Interop.WVR_ShowPassthroughOverlay(!status);
        Interop.WVR_ShowPassthroughUnderlay(status);
    }

    private IEnumerator WaitForInputSystem()
    {
        yield return new WaitUntil(() => InputManager.IsInstantiated);

        InputManager.InputActions.XRILeftHandInteraction.DebugButton1.performed += DebugButton1_performed;
        _inputActionsLinked = true;
    }
}
