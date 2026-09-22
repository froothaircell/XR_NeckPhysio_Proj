using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Wave.Native;
using Wave.XR.Sample.Input;

namespace Wave.Essence.ScenePerception.Sample
{
	public class ScenePerceptionDemo : MonoBehaviour
	{
		private const string TAG = "ScenePerceptionDemo";

		[SerializeField] private LogPanel logPanel;
		[SerializeField] private ScenePerceptionHelper _scenePerceptionHelper;
		[SerializeField] private PassThroughHelper passThroughHelper;
		[SerializeField] private GameObject anchorPrefab, anchorDisplayPrefab;

		[SerializeField] private SpatialAnchorHelper _spatialAnchorHelper;
		private ScenePerceptionMeshFacade _scenePerceptionMeshFacade;
		private bool hideMeshAndAnchors = false;

		private RaycastHit leftControllerRaycastHitInfo = new RaycastHit();
		private RaycastHit rightControllerRaycastHitInfo = new RaycastHit();
		private GameObject AnchorDisplayRight = null;
		private AnchorGenerateMode anchorGenerateMode = AnchorGenerateMode.Raycast;

		[SerializeField] private Material GeneratedMeshMaterialTranslucent, GeneratedMeshMaterialWireframe, GeneratedMeshMaterialTexture;
		[SerializeField] private GameObject leftController = null, rightController = null;

		[SerializeField] private Text modeText;
		[SerializeField] private Text statusText;

		[SerializeField] private SwipeFunction verticalSwipe = SwipeFunction.AnchorGeneration;
		[SerializeField] private SwipeFunction horizontalSwipe = SwipeFunction.AnchorMode;

		[SerializeField] private Transform trackingOrigin;

		private enum AnchorGenerateMode
		{
			Raycast,
			Touch,
		}

		public enum SwipeFunction
		{
			SceneTarget,
			AnchorMode,
			AnchorGeneration,
		}

		private const InputUsage ctrlUsages = 
			InputUsage.Primary2DAxis | 
			InputUsage.Primary2DButton | 
			InputUsage.MenuButton | 
			InputUsage.PrimaryButton | 
			InputUsage.SecondaryButton;
		Wave.XR.Sample.Input.Controller ctrlL = new Wave.XR.Sample.Input.Controller(true, ctrlUsages);
		Wave.XR.Sample.Input.Controller ctrlR = new Wave.XR.Sample.Input.Controller(false, ctrlUsages);

		private void OnEnable()
		{
			_scenePerceptionHelper.Init(this, logPanel);
			_spatialAnchorHelper.Init(this, logPanel, _scenePerceptionHelper.scenePerceptionManager, anchorPrefab);

			_scenePerceptionHelper.OnEnable();
			_spatialAnchorHelper.OnEnable();

			if (_scenePerceptionHelper.IsSceneStarted)
			{
				_spatialAnchorHelper.SetAnchorsShouldBeUpdated();
			}
			_scenePerceptionMeshFacade = new ScenePerceptionMeshFacade(this, _scenePerceptionHelper, anchorDisplayPrefab, GeneratedMeshMaterialTranslucent, GeneratedMeshMaterialWireframe, GeneratedMeshMaterialTexture);
		}

		private void OnDisable()
		{
			_scenePerceptionHelper.OnDisable();
			_spatialAnchorHelper.OnDisable();
		}

		private void OnApplicationPause(bool pause)
		{
			if (!pause)
			{
				_spatialAnchorHelper.SetAnchorsShouldBeUpdated(); //Anchors will have moved since the program was previously running - re-update during On Resume in case of a tracking map change
			}
		}

		private void Start()
		{
			UpdateSceneAnchorModeText();
			if (logPanel)
			{
				logPanel.Clear();
				logPanel.AddLog("ScenePerceptionDemo started.");
			}
		}

		private void UpdateSceneAnchorModeText()
		{
			if (modeText == null) return;
			StringBuilder sb = new StringBuilder("Scene");
			if (_scenePerceptionHelper.target2DPlane)
				sb.Append(" 2DPlane");
			if (_scenePerceptionHelper.target3DObject)
				sb.Append(" 3DObject");
			if (_scenePerceptionHelper.targetSceneMesh)
				sb.Append(" Mesh");
			//string anchorMode;
			switch (_spatialAnchorHelper.anchorMode)
			{
				case SpatialAnchorHelper.AnchorMode.Spatial:
					sb.Append(" + Spatial Anchor");
					break;
				case SpatialAnchorHelper.AnchorMode.Persisted:
					sb.Append("Persisted Anchor");
					anchorGenerateMode = AnchorGenerateMode.Touch;  // Force use touch mode
					break;
				case SpatialAnchorHelper.AnchorMode.Cached:
					sb.Append("Cached Anchor");
					break;
				default:
					sb.Append("Unknown");
					break;
			}
			modeText.text = sb.ToString();
		}

		List<InputDevice> inputDevices = new List<InputDevice>();

		void UpdateInput()
		{
			if (!ctrlL.dev.isValid) InputDeviceTools.GetController(ctrlL, inputDevices);
			if (ctrlL.dev.isValid) InputDeviceTools.UpdateController(ctrlL, ctrlUsages);

			if (!ctrlR.dev.isValid) InputDeviceTools.GetController(ctrlR, inputDevices);
			if (ctrlR.dev.isValid) InputDeviceTools.UpdateController(ctrlR, ctrlUsages);
		}

		float menuDoubleClickTime = 0;
		float timeAccForAnchorUpdate = 0;

		private void Update()
		{
			// Make sure the floor is set.  Otherwise, the scene objects's pose will get wrong.
			if (ScenePerceptionManager.GetTrackingOriginModeFlags() != TrackingOriginModeFlags.Floor)
				return;

			UpdateInput();

			// All operation need wait Scene started.
			if (!_scenePerceptionHelper.IsSceneStarted)
				return;

			if (ctrlR.btnSec.IsDown)  // B
			{
				statusText.text = "Togggle objects visiblility";
				hideMeshAndAnchors = !hideMeshAndAnchors;
				Log.d(TAG, Log.CSB.Append("hideMeshAndAnchors: ").Append(hideMeshAndAnchors).Append(hideMeshAndAnchors ? ", All ScenePerception update is stopped." : ""));
				if (hideMeshAndAnchors)
				{
					_scenePerceptionMeshFacade.DestroyGeneratedMeshes();
					_spatialAnchorHelper.ClearAnchorObjects();
					modeText.text = "All Scene Perception paused";
				}
				else
				{
					UpdateSceneAnchorModeText();
				}
			}

			if (ctrlL.btnSec.IsDown)  // Y
			{
				statusText.text = "Togggle Passthrough Underlay";
				passThroughHelper.ShowPassthroughUnderlay(!Interop.WVR_IsPassthroughOverlayVisible());
			}

			if (hideMeshAndAnchors)
				return;

			bool needUpdateMeshes = false;
			List <ScenePerceptionHelper.SceneTarget> targets = new List<ScenePerceptionHelper.SceneTarget>();
			if (_scenePerceptionHelper.target2DPlane)
				targets.Add(ScenePerceptionHelper.SceneTarget.TwoDimensionPlane);
			if (_scenePerceptionHelper.target3DObject)
				targets.Add(ScenePerceptionHelper.SceneTarget.ThreeDimensionObject);
			if (_scenePerceptionHelper.targetSceneMesh)
				targets.Add(ScenePerceptionHelper.SceneTarget.SceneMesh);

			foreach (var target in targets)
			{
				//Handle Scene Perception
				if (!_scenePerceptionHelper.IsStarted(target))
				{
					_scenePerceptionHelper.StartScenePerception(target);
				}
				else
				{
					_scenePerceptionHelper.ScenePerceptionGetState(target); //Update state of scene perception every frame
					needUpdateMeshes = true;
				}
			}

			if (needUpdateMeshes)
				_scenePerceptionMeshFacade.UpdateScenePerceptionMesh();

			// Update Spatial Anchor's pose / state every 0.35 second.
			timeAccForAnchorUpdate += Time.deltaTime;
			if (timeAccForAnchorUpdate > 0.35f)
			{
				timeAccForAnchorUpdate = 0;
				_spatialAnchorHelper.UpdateAnchorDictionary();
			}

			if (ctrlL.btnPri.IsDown)  // X
			{
				statusText.text = "Destroy hitted anchor object";
				_spatialAnchorHelper.HandleAnchorUpdateDestroy(leftControllerRaycastHitInfo);
			}
			if (ctrlR.btnPri.IsDown)  // A
			{
				if (anchorGenerateMode == AnchorGenerateMode.Raycast)
				{
					statusText.text = "Create Anchor at hitted place";
					_spatialAnchorHelper.HandleAnchorUpdateCreate(rightControllerRaycastHitInfo, rightController.transform.rotation);
				}
				else
				{
					statusText.text = "Create Anchor at controller position";
					_spatialAnchorHelper.HandleAnchorUpdateCreate(rightController.transform.position, rightController.transform.rotation);
				}
			}

			if (ctrlL.btnMenu.IsDown)
			{
				if (Time.unscaledTime - menuDoubleClickTime >= 1.5f)
				{
					Log.d(TAG, "Destroy all anchors");
					statusText.text = "Destroy all anchors";
					_spatialAnchorHelper.ClearAnchors();
				}
				else
				{
					Log.d(TAG, "Destroy all kind of anchors");
					statusText.text = "Destroy all kind of anchors";
					_spatialAnchorHelper.ClearAnchors(true);
				}
				menuDoubleClickTime = Time.unscaledTime;
			}

			if (ctrlR.btnJoy.IsDown)
			{
				Log.d(TAG, "Export all persist anchor");
				statusText.text = "Export all persist anchor";
				_spatialAnchorHelper.ExportPersistAnchors();
			}

			if (ctrlL.btnJoy.IsDown)
			{
				Log.d(TAG, "Import all persist anchor");
				statusText.text = "Import all persist anchor";
				_spatialAnchorHelper.ImportPersistAnchors();
			}

			SwipeActionDetect();
		}

		private void SwipeActionDetect()
		{
			bool isSwipeDown =
				(ctrlR.axisJoy.Down.IsDown && ctrlL.axisJoy.Down.IsPressed) ||
				(ctrlL.axisJoy.Down.IsDown && ctrlR.axisJoy.Down.IsPressed);
			bool isSwipeUp = 
				(ctrlR.axisJoy.Up.IsDown && ctrlL.axisJoy.Up.IsPressed) ||
				(ctrlL.axisJoy.Up.IsDown && ctrlR.axisJoy.Up.IsPressed);
			bool isSwipeLeft =
				(ctrlR.axisJoy.Left.IsDown && ctrlL.axisJoy.Left.IsPressed) ||
				(ctrlL.axisJoy.Left.IsDown && ctrlR.axisJoy.Left.IsPressed);
			bool isSwipeRight =
				(ctrlR.axisJoy.Right.IsDown && ctrlL.axisJoy.Right.IsPressed) ||
				(ctrlL.axisJoy.Right.IsDown && ctrlR.axisJoy.Right.IsPressed);
			{
				if ( isSwipeUp)
				{
					if (verticalSwipe == SwipeFunction.SceneTarget)
					{
						Log.d(TAG, "Scene Mode ++");
						statusText.text = "Scene Mode ++";
						// TODO
						_scenePerceptionHelper.target2DPlane = false;
						_scenePerceptionHelper.target3DObject = false;
						_scenePerceptionHelper.targetSceneMesh = false;
						UpdateSceneAnchorModeText();
					}
					else if (verticalSwipe == SwipeFunction.AnchorGeneration)
					{
						if (_spatialAnchorHelper.anchorMode == SpatialAnchorHelper.AnchorMode.Persisted)
							anchorGenerateMode = AnchorGenerateMode.Touch;
						else
							anchorGenerateMode = (AnchorGenerateMode)(((int)anchorGenerateMode + 1) % 2);
						Log.d(TAG, "Generate Anchor By " + anchorGenerateMode);
						statusText.text = "Anchor By " + anchorGenerateMode;
					}
				}
				if (isSwipeDown)
				{
					if (verticalSwipe == SwipeFunction.SceneTarget)
					{
						Log.d(TAG, "Scene Mode --");
						statusText.text = "Scene Mode --";
						// TODO
						_scenePerceptionHelper.target2DPlane = false;
						_scenePerceptionHelper.target3DObject = false;
						_scenePerceptionHelper.targetSceneMesh = false;
						UpdateSceneAnchorModeText();
					}
					else if (verticalSwipe == SwipeFunction.AnchorGeneration)
					{
						if (_spatialAnchorHelper.anchorMode == SpatialAnchorHelper.AnchorMode.Persisted)
							anchorGenerateMode = AnchorGenerateMode.Touch;
						else
							anchorGenerateMode = (AnchorGenerateMode)(((int)anchorGenerateMode + 1) % 2);
						Log.d(TAG, "Generate Anchor By " + anchorGenerateMode);
						statusText.text = "Anchor By " + anchorGenerateMode;
					}
				}
			}

			{
				if (isSwipeLeft)
				{
					if (horizontalSwipe == SwipeFunction.AnchorMode)
					{
						Log.d(TAG, "Anchor Mode --");
						statusText.text = "Anchor Mode --";
						_spatialAnchorHelper.anchorMode = (SpatialAnchorHelper.AnchorMode)(((int)_spatialAnchorHelper.anchorMode + 2) % 3);
						_spatialAnchorHelper.SetAnchorsShouldBeUpdated();
						UpdateSceneAnchorModeText();
					}
				}
				if (isSwipeRight)
				{
					if (horizontalSwipe == SwipeFunction.AnchorMode)
					{
						Log.d(TAG, "Anchor Mode ++");
						statusText.text = "Anchor Mode ++";
						_spatialAnchorHelper.anchorMode = (SpatialAnchorHelper.AnchorMode)(((int)_spatialAnchorHelper.anchorMode + 1) % 3);
						_spatialAnchorHelper.SetAnchorsShouldBeUpdated();
						UpdateSceneAnchorModeText();
					}
				}
			}
		}

		private void FixedUpdate()
		{
			if (AnchorDisplayRight == null)
			{
				AnchorDisplayRight = Instantiate(anchorDisplayPrefab);
			}

			// Only right hand have touch mode.
			Physics.Raycast(leftController.transform.position, leftController.transform.forward, out leftControllerRaycastHitInfo);

			if (anchorGenerateMode == AnchorGenerateMode.Raycast)
			{
				Physics.Raycast(rightController.transform.position, rightController.transform.forward, out rightControllerRaycastHitInfo);
				if (rightControllerRaycastHitInfo.collider != null &&
					rightControllerRaycastHitInfo.collider.transform.GetComponent<AnchorPrefab>() == null) //Not hitting an anchor
				{
					AnchorDisplayRight.SetActive(true);
					AnchorDisplayRight.transform.SetPositionAndRotation(rightControllerRaycastHitInfo.point, rightController.transform.rotation);
				}
				else
				{
					AnchorDisplayRight.SetActive(false);
				}
			}
			else if (anchorGenerateMode == AnchorGenerateMode.Touch)
			{
				AnchorDisplayRight.SetActive(true);
				AnchorDisplayRight.transform.SetPositionAndRotation(rightController.transform.position, rightController.transform.rotation);
			}
		}

		public void ChangeSceneMeshTypeToVisual()
		{
			_scenePerceptionMeshFacade.ChangeSceneMeshType(WVR_SceneMeshType.WVR_SceneMeshType_VisualMesh);
		}

		public void ChangeSceneMeshTypeToCollider()
		{
			_scenePerceptionMeshFacade.ChangeSceneMeshType(WVR_SceneMeshType.WVR_SceneMeshType_ColliderMesh);
		}

		public Transform GetTrackingOrigin()
		{
			if (trackingOrigin == null)
			{
				if (WaveRig.Instance != null)
					trackingOrigin = WaveRig.Instance.transform;
				else if (Camera.main != null)
					trackingOrigin = Camera.main.transform.parent;
				else
					trackingOrigin = transform.root;
			}

			return trackingOrigin;
		}
	}
}
