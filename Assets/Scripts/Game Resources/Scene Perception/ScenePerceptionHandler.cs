using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Wave.Native;
using Wave.Essence;
using Wave.Essence.ScenePerception;
using CoreResources.Singleton;
using GameResources.PerceptionManagement.MeshManagement;
using GameResources.PerceptionManagement.AnchorManagement;

namespace GameResources.PerceptionManagement
{
    public enum SceneTarget
    {
        TwoDimensionPlane = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane,
        ThreeDimensionObject = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_3dObject,
        SceneMesh = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_SceneMesh,
    }

    public enum ScenePerceptionState
    {
        Empty = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty,  // if No plane, no object, or no scene mesh, return this.
        Observing = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Observing,
        Paused = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Paused,
        Completed = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Completed,  // if has plane, has object, or hasscene mesh, return this.
    }

    public class ScenePerceptionHandler : DestroyableMonoSingleton<ScenePerceptionHandler>
    {
        public static bool PermissionGranted { get; private set; } = false;

        #region Serialized Fields
        [SerializeField] private ScenePerceptionManager _scenePerceptionManager;
        [SerializeField] private SpatialAnchorHelper _spatialAnchorHelper;
        [SerializeField] private bool target2DPlane = true, 
            target3DObject = true, 
            targetSceneMesh = true;
        [SerializeField] private Material _generatedMeshMaterialTranslucent, 
            _generatedMeshMaterialWireframe, 
            _generatedMeshMaterialTexture;
        [SerializeField] private GameObject _anchorPrefab, 
            _anchorDisplayPrefab;
        #endregion

        #region Private Properties
        private ScenePerceptionMeshFacade _meshFacade = null;
        private Transform trackingOrigin;
        private bool _scenePerceptionStarted = false;
        float timeAccForAnchorUpdate = 0;
        private const string _scenePerceptionPermissionString = "wave.permission.GET_SCENE_MESH";
        private readonly List<bool> perceptionStartedDictionary = new List<bool>(3) { false, false, false };
        private readonly List<WVR_ScenePerceptionState> perceptionStateDictionary =
            new List<WVR_ScenePerceptionState>(3) {
                WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty,
                WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty,
                WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty, };
        #endregion

        #region Public Properties
        public ScenePerceptionManager PerceptionManager => _scenePerceptionManager;
        public bool Target2DPlane => target2DPlane;
        public bool Target3DObject => target3DObject;
        public bool TargetSceneMesh => targetSceneMesh;
        #endregion
        #region Overrides
        public override void OnInit()
        {
            //Check whether feature is supported on device or not
            if (IsScenePerceptionSupported())
            {
                RequestSceneMeshPermission();

                WVR_Result result = _scenePerceptionManager.StartScene();
                if (result == WVR_Result.WVR_Success)
                {
                    result = _scenePerceptionManager.StartScenePerception(WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane); //Start perceiving 2D planes

                    if (result == WVR_Result.WVR_Success)
                    {
                        _scenePerceptionStarted = true;
                        _spatialAnchorHelper.Init(this, _scenePerceptionManager, _anchorPrefab);
                        _spatialAnchorHelper.OnEnable();
                        _spatialAnchorHelper.SetAnchorsShouldBeUpdated();

                        _meshFacade = new ScenePerceptionMeshFacade(this, _anchorDisplayPrefab, _generatedMeshMaterialTranslucent, _generatedMeshMaterialWireframe, _generatedMeshMaterialTexture);
                        //Examples of things to do here:
                        //Scene Planes
                        //- Call scenePerceptionManager.GetScenePerceptionState() to see the current WVR_ScenePerceptionState of a specific WVR_ScenePerceptionTarget
                        //- Call scenePerceptionManager.StopScenePerception() to stop perceiving a specific WVR_ScenePerceptionTarget
                        //
                        //Spatial Anchors
                        //- Call scenePerceptionManager.GetSpatialAnchors() to retrieve all handles of existing anchors
                        //- Using the retrieved handles, call scenePerceptionManager.GetSpatialAnchorState() to get information of the Spatial Anchors
                    }
                }
            }
            else
            {
                Debug.LogError("Scene Perception is not available on the current device.");
            }
        }

        public override void OnDeInit()
        {
            if (_scenePerceptionStarted)
            {
                _scenePerceptionManager.StopScene();
                _spatialAnchorHelper.OnDisable();
            }
        }

        private void Update()
        {
            if (ScenePerceptionManager.GetTrackingOriginModeFlags() != TrackingOriginModeFlags.Floor)
                return;

            if (!_scenePerceptionStarted)
                return;

            HandleScenePerceptionUpdates();
            HandleAnchorUpdates();
        }
        #endregion

        #region Public Methods
        public Transform GetTrackingOrigin()
        {
            if (trackingOrigin == null)
            {
                if (_scenePerceptionManager != null && _scenePerceptionManager.TrackingOrigin != null)
                    trackingOrigin = _scenePerceptionManager.TrackingOrigin;
                else if (Camera.main != null)
                    trackingOrigin = Camera.main.transform.parent;
                else
                    trackingOrigin = transform.root;
            }

            return trackingOrigin;
        }

        public bool IsStarted(SceneTarget target)
        {
            return perceptionStartedDictionary[(int)target];
        }

        public ScenePerceptionState GetState(SceneTarget target)
        {
            return (ScenePerceptionState)perceptionStateDictionary[(int)target];
        }
        #endregion

        #region Private Methods
        public void StartScenePerception(SceneTarget target)
        {
            if (_scenePerceptionStarted && !perceptionStartedDictionary[(int)target])
            {
                if (target == SceneTarget.SceneMesh && !SceneMeshPermissionHelper.permissionGranted)
                {
                    Debug.Log("Scene Mesh Permission not granted, cannot not start scene perception with scene mesh as perception target.");
                }

                WVR_Result result = _scenePerceptionManager.StartScenePerception((WVR_ScenePerceptionTarget)target);

                if (result == WVR_Result.WVR_Success)
                {
                    perceptionStartedDictionary[(int)target] = true;
                    ScenePerceptionGetState(target);
                }
            }
        }

        private bool IsScenePerceptionSupported()
        {
#if UNITY_EDITOR
            if (Application.isEditor)
                return true;
#endif
            return (Interop.WVR_GetSupportedFeatures() &
                    (ulong)WVR_SupportedFeature.WVR_SupportedFeature_ScenePerception) != 0;
        }

        private void RequestSceneMeshPermission()
        {
            Debug.Log("Request Scene Mesh Permission");
            string[] permArray = {
               _scenePerceptionPermissionString
            };

            if (PermissionManager.instance == null) return;

            PermissionGranted = PermissionManager.instance.isPermissionGranted(_scenePerceptionPermissionString);
            if (!PermissionGranted)
                PermissionManager.instance.requestPermissions(permArray, requestDoneCallback);
        }

        private void HandleScenePerceptionUpdates()
        {
            bool needUpdateMeshes = false;
            List<SceneTarget> targets = new List<SceneTarget>();
            if (target2DPlane)
                targets.Add(SceneTarget.TwoDimensionPlane);
            if (target3DObject)
                targets.Add(SceneTarget.ThreeDimensionObject);
            if (targetSceneMesh)
                targets.Add(SceneTarget.SceneMesh);

            foreach (var target in targets)
            {
                //Handle Scene Perception
                if (!IsStarted(target))
                {
                    StartScenePerception(target);
                }
                else
                {
                    ScenePerceptionGetState(target); //Update state of scene perception every frame
                    needUpdateMeshes = true;
                }
            }

            if (needUpdateMeshes)
                _meshFacade.UpdateScenePerceptionMesh();
        }

        private void HandleAnchorUpdates()
        {
            // Update Spatial Anchor's pose / state every 0.35 second.
            timeAccForAnchorUpdate += Time.deltaTime;
            if (timeAccForAnchorUpdate > 0.35f)
            {
                timeAccForAnchorUpdate = 0;
                _spatialAnchorHelper.UpdateAnchorDictionary();
            }
        }

        private void ScenePerceptionGetState()
        {
            WVR_ScenePerceptionState latestPerceptionState = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty;
            WVR_Result result = _scenePerceptionManager.GetScenePerceptionState(WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane, ref latestPerceptionState);
            if (result == WVR_Result.WVR_Success)
            {
                //Check perception state here
                if (latestPerceptionState == WVR_ScenePerceptionState.WVR_ScenePerceptionState_Completed)
                {
                    //When perception for 2d planes is completed, you can retrieve the data of the perceived Scene Planes
                    ScenePlane[] latestScenePlanes;
                    result = _scenePerceptionManager.GetScenePlanes(ScenePerceptionManager.GetTrackingOriginModeFlags(), out latestScenePlanes);

                    if (result == WVR_Result.WVR_Success)
                    {
                        //Handle the retrieved data of the Scene Planes here
                        //For example:
                        //- Cache Scene Plane data for future reference
                        //- Compare extent and pose of cached Scene Planes with the freshly retrieved ones to see if there are any updates
                    }
                }
            }
        }

        private void ScenePerceptionGetState(SceneTarget target)
        {
            WVR_ScenePerceptionState latestPerceptionState = WVR_ScenePerceptionState.WVR_ScenePerceptionState_Empty;
            WVR_Result result = _scenePerceptionManager.GetScenePerceptionState((WVR_ScenePerceptionTarget)target, ref latestPerceptionState);
            if (result == WVR_Result.WVR_Success)
            {
                perceptionStateDictionary[(int)target] = latestPerceptionState; //Update perception state for the perception target
            }
        }

        private void StopScenePerception(SceneTarget target)
        {
            if (_scenePerceptionStarted && perceptionStartedDictionary[(int)target])
            {
                WVR_Result result = _scenePerceptionManager.StopScenePerception((WVR_ScenePerceptionTarget)target);

                if (result == WVR_Result.WVR_Success)
                {
                    perceptionStartedDictionary[(int)target] = false;
                }
            }
        }

        private void StopScenePerception()
        {
            StopScenePerception(SceneTarget.TwoDimensionPlane);
            StopScenePerception(SceneTarget.SceneMesh);
            StopScenePerception(SceneTarget.ThreeDimensionObject);
        }
        #endregion

        #region Events and Callbacks
        private static void requestDoneCallback(List<PermissionManager.RequestResult> results)
        {
            foreach (PermissionManager.RequestResult permissionRequestResult in results)
            {
                if (permissionRequestResult.PermissionName.Equals(_scenePerceptionPermissionString))
                {
                    PermissionGranted = permissionRequestResult.Granted;

                    Debug.Log("Scene Mesh permission granted = " + PermissionGranted);
                }
            }
        }
        #endregion
    }
}