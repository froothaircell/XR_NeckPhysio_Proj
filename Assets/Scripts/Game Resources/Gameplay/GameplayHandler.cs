using BezierSolution;
using CoreResources.Managers.InputManagement;
using CoreResources.Singleton;
using CoreResources.Utils;
using GameResources.Gameplay.VRController;
using GameResources.Pooling;
using GameResources.StateMachine;
using GameResources.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Wave.Native;

namespace GameResources.Gameplay
{
    public enum AppPhase
    {
        MainMenu = 0,
        Warmup = 1,
        Phase1 = 2,
        Phase2 = 3,
        Phase3 = 4,
        Phase4 = 5,
    }

    public enum Phase4Mode
    {
        FourTargetMode = 0,
        EightTargetMode = 1
    }

    public class GameplayHandler : DestroyableMonoSingleton<GameplayHandler>
    {
        #region Serialized Fields
        [SerializeField]
        private float _appCalibrationDistance = 15f;
        [SerializeField]
        private float _appCalibrationDistanceFactor = 0.90f;
        [SerializeField]
        private float _appCalibrationDistanceFactor_UI = 0.95f;
        [SerializeField]
        private float _appCalibrationDistanceFactor_BlackScreen = 0.6f;
        [SerializeField]
        private float _appCalibrationScaling = 0.8f;
        [SerializeField]
        private ObjectPool _pool;
        [SerializeField]
        private PhysiologicalDataHandler _dataHandler;

        [Space(5)]
        
        [Header("Game Set - Application Warmup")]
        [SerializeField]
        private GameObject _gameSetWarmup;
        [SerializeField]
        private Transform[] _defaultTransforms;

        [Space(5)]

        [Header("Game Set - Application Phase 1")]
        [SerializeField]
        private LookAreaGenerator _lookAreaGenerator;

        [Space(5)]
        
        [Header("Game Set - Application Phase 2")]
        [SerializeField]
        private GameObject _gameSetPhase2;
        [SerializeField]
        private Transform _spawnCenter;
        [SerializeField]
        private float _minSpawnDelay = 0.8f, 
        _maxSpawnDelay = 5f;
        [SerializeField]
        private LayerMask _collisionLayerMask;
        [SerializeField]
        private int _phase2SpawnCount = 15;
        [SerializeField]
        private Transform _camHMD;

        [Space(5)]

        [Header("Game Set - Application Phase 3")]
        [SerializeField]
        private GameObject _gameSetPhase3;
        [SerializeField]
        private BezierSpline[] _bezierSplines;
        [SerializeField]
        private InteractionPanel _interactionPanel_AppP3;

        [Space(5)]

        [Header("Game Set - Application Phase 4")]
        /*[SerializeField, Range(0, 8)]
        private int _phase4TargetSpawnCount = 2; */
        [SerializeField]
        private Phase4Mode _phase4Mode;
        #endregion

        #region Private Fields
        private Vector3 _defaultSpawnPosition;
        private Quaternion _defaultSpawnRotation;
        private Vector3 _defaultLookAreaNormalVector;
        private Vector3 _defaultLookAreaUpVector;
        private Color? recOriginalColor = null;
        private Coroutine _spawnCoroutine;
        private int _spawnCount, _warmupSelectedCount;
        private float _cachedProjectileScaleFactor = 1f;
        private bool _triggerPressed = false,
            _inputsAssigned = false,
            _centeringReticleDespawned = false,
            _phase2ProjectileDespawned = false,
            _phase3NextProjectileRequested = false,
            _phase4TargetRequested = false,
            _phase4TargetDespawned = false,
            _phase4HeadRecentered = false;
        private AppPhase _phase;

        private ProjectileController _cachedProjectile_Interaction = null;
        private ProjectileController _cachedProjectile_Selection = null;

        private const string P4_BLACKOUT_TEXT = "Please recenter your head position and then press the right grip button";
        #endregion

        public AppPhase Phase => _phase;
        public float CachedProjectileScaleFactor => _cachedProjectileScaleFactor;

        #region Events
        /// <summary>
        /// Event to invoke when launching application 
        /// variant, the parameter should be the 
        /// corresponding value for the application phase
        /// </summary>
        public static Action<int> OnPlayEvent;
        public static Action OnExitEvent;
        public static Action OnWarmupComplete;
        public static Action OnPhase2Hit;
        public static Action OnPhase2Complete;
        public static Action OnEnablePhase3NextButton;
        public static Action OnPhase3NextItem;
        public static Action OnPhase3Complete;
        public static Action OnEnablePhase4NextButton;
        public static Action OnPhase4NextItem;
        public static Action OnPhase4Complete;
        #endregion

        #region Overrides
        public override void OnInit()
        {
            ShowPassthroughUnderlay(true);

            _defaultSpawnPosition = _spawnCenter.position;
            _defaultSpawnRotation = _spawnCenter.rotation;

            _dataHandler.gameObject.SetActive(true);
            _dataHandler.InitSingleton(_camHMD);
            _lookAreaGenerator.InitSingleton(_camHMD);

            OnPlayEvent += OnPlay;
            OnExitEvent += OnExit;
            OnPhase2Hit += OnHitPerformed_AppP2;
            OnPhase3NextItem += OnNextProjectileRequested_AppP3;
            OnPhase4NextItem += OnNextProjectileRequested_AppP4;

            _inputsAssigned = false;

            StartCoroutine(WaitForInputSystem());
        }

        public override void OnDeInit()
        {
            OnPlayEvent = null;
            OnExitEvent = null;
            OnWarmupComplete = null;
            OnPhase2Hit = null;
            OnPhase2Complete = null;
            OnEnablePhase3NextButton = null;
            OnPhase3NextItem = null;
            OnPhase3Complete = null;
            OnPhase4NextItem = null;
            OnPhase4Complete = null;

            _dataHandler.CleanSingleton();

            ResetGame();
        }
        #endregion

        #region Public Methods
        public void CalibrateSceneToCameraOrientation()
        {
            // Set Positions of relevant 
            Vector3 forward = _camHMD.forward;
            _camHMD.GetPositionAndRotation(out Vector3 origin, out Quaternion finalRotation);
            Vector3 finalPosition = origin + forward * _appCalibrationDistance;
            Vector3 finalUIPosition = finalPosition;
            float scaleFactor = 1f;

            // Try to see if a raycast hits a wall mesh
            if (Physics.Raycast(origin, forward, out var hitInfo))
            {
                var cachedCollider = hitInfo.collider;
                finalPosition = hitInfo.point;
                var distance = Vector3.Distance(origin, finalPosition);
                finalPosition = origin + forward * distance * _appCalibrationDistanceFactor;
                finalUIPosition = origin + forward * distance * _appCalibrationDistanceFactor_UI;
                finalRotation = cachedCollider.transform.rotation;
                Quaternion flipRotation = Quaternion.Euler(0, 180f, 0);
                finalRotation *= flipRotation;
                var minRectDim = UIMediator.Instance.GetMinimumRectDimensions();
                
                _cachedProjectileScaleFactor = scaleFactor = _appCalibrationDistanceFactor * 
                    Mathf.Abs(
                        Mathf.Max(
                            cachedCollider.bounds.size.x, 
                            cachedCollider.bounds.size.y, 
                            cachedCollider.bounds.size.z) / minRectDim);

                BlackoutScreenHandler.Instance.SetScale(_appCalibrationDistanceFactor_BlackScreen);
                CursorHandler.Instance.SetCursorScales(distance);
                ObjectPool.Instance.ScaleProjectiles(_cachedProjectileScaleFactor);
                PhysiologicalDataHandler.Instance.SetScale(scaleFactor);
            }

            _gameSetWarmup.transform.position = finalPosition;
            _gameSetWarmup.transform.rotation = finalRotation;
            _gameSetWarmup.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            
            _lookAreaGenerator.transform.position = finalPosition;
            _lookAreaGenerator.transform.rotation = finalRotation;
            _lookAreaGenerator.RecalibrateInteractables(
                out _defaultLookAreaNormalVector, 
                out _defaultLookAreaUpVector);
            _lookAreaGenerator.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

            _gameSetPhase2.transform.position = finalPosition;
            _gameSetPhase2.transform.rotation = finalRotation;
            _defaultSpawnPosition = _spawnCenter.position;
            _defaultSpawnRotation = _spawnCenter.rotation;
            _gameSetPhase2.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

            _gameSetPhase3.transform.position = finalPosition;
            _gameSetPhase3.transform.rotation = finalRotation;
            _gameSetPhase3.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);

            // UIMediator.Instance.SetMenuPositions(origin, forward, rotation);
            UIMediator.Instance.SetMenuPositions(finalUIPosition, finalRotation, scaleFactor);
        }

        /// <summary>
        /// Returns a pair of values, namely the distance 
        /// of the camera from a  subject, and the distance 
        /// of the camera from the center spawn position 
        /// respectively
        /// </summary>
        /// <param name="subject">
        /// The object to get the first distance value for
        /// </param>
        /// <returns></returns>
        public Tuple<float, float> GetDistanceTuple(Transform subject)
        {
            var tuple = new Tuple<float, float>(
                Vector3.Distance(subject.position, _camHMD.position),
                Vector3.Distance(_spawnCenter.position, _camHMD.position));
            return tuple;
        }
        #endregion

        #region Event Listeners
        private void OnPlay(int appPhase)
        {
            if (CursorHandler.IsInstantiated)
            {
                CursorHandler.Instance.OnValidSelectionPerformed += OnValidSelection;
                CursorHandler.Instance.OnValidSelectionCancelled += OnValidSelectionCancelled;
                CursorHandler.Instance.OnValidInteractionStarted += OnValidInteractionStarted;
                CursorHandler.Instance.OnValidInteractionPerformed += OnValidInteractionPerformed;
                CursorHandler.Instance.OnValidInteractionCancelled += OnValidInteractionCancelled;
            }

            switch (appPhase)
            {
                case 0:
                    _phase = (AppPhase)appPhase;
                    break;
                case 1:
                    // Initialization for warmup
                    _gameSetWarmup.SetActive(true);
                    _lookAreaGenerator.gameObject.SetActive(false);

                    if (_spawnCoroutine != null)
                    {
                        StopCoroutine(_spawnCoroutine);
                        _spawnCoroutine = null;
                    }

                    _pool.UnlockPool();
                    _spawnCoroutine = StartCoroutine(SpawnCoroutine_Warmup());

                    _phase = (AppPhase)appPhase;
                    break;
                case 2:
                    ResetGame(false);

                    _gameSetWarmup.SetActive(false);
                    _gameSetPhase2.SetActive(true);
                    _lookAreaGenerator.gameObject.SetActive(true);
                    _lookAreaGenerator.ResetInteractables();
                    _lookAreaGenerator.AllowLookAreaModification();

                    if (_spawnCoroutine != null)
                    {
                        StopCoroutine(_spawnCoroutine);
                        _spawnCoroutine = null;
                    }

                    _phase = (AppPhase)appPhase;
                    break;
                case 3:
                    ResetGame(false);

                    _lookAreaGenerator.RecordViewingAngleBounds();
                    _lookAreaGenerator.RestrictLookAreaModification();

                    _phase2ProjectileDespawned = false;
                    _centeringReticleDespawned = false;

                    _pool.UnlockPool();
                    _spawnCoroutine = StartCoroutine(SpawnCoroutine_AppP2());

                    _phase = (AppPhase)appPhase;
                    break;
                case 4:
                    ResetGame(false);

                    _gameSetPhase3.SetActive(true);
                    _lookAreaGenerator.RestrictLookAreaModification();
                    _lookAreaGenerator.gameObject.SetActive(false);
                    _gameSetPhase2.SetActive(false);

                    _interactionPanel_AppP3.InitializePanel();

                    _pool.UnlockPool();

                    if (_spawnCoroutine != null)
                    {
                        StopCoroutine(_spawnCoroutine);
                        _spawnCoroutine = null;
                    }

                    _spawnCoroutine = StartCoroutine(SpawnCoroutine_AppP3());

                    _phase = (AppPhase)appPhase;
                    break;
                case 5:
                    // Initialization for proprioception test
                    _gameSetPhase3.SetActive(false);
                    _lookAreaGenerator.RestrictLookAreaModification();
                    _lookAreaGenerator.gameObject.SetActive(true);
                    // _gameSetPhase2and4.SetActive(true);

                    foreach (var item in _bezierSplines)
                    {
                        item.gameObject.SetActive(false);
                    }

                    _spawnCoroutine = StartCoroutine(SpawnCoroutine_AppP4());

                    _phase = (AppPhase)appPhase;
                    break;
                default:
                    break;
            }
        }

        private void OnExit()
        {
            if (CursorHandler.IsInstantiated)
            {
                CursorHandler.Instance.OnValidSelectionPerformed -= OnValidSelection;
                CursorHandler.Instance.OnValidSelectionCancelled -= OnValidSelectionCancelled;
                CursorHandler.Instance.OnValidInteractionStarted -= OnValidInteractionStarted;
                CursorHandler.Instance.OnValidInteractionPerformed -= OnValidInteractionPerformed;
                CursorHandler.Instance.OnValidInteractionCancelled -= OnValidInteractionCancelled;
            }

            ResetGame();
        }

        private void RightGripPressed(InputAction.CallbackContext obj)
        {
            switch (_phase)
            {
                case AppPhase.Phase4:
                    if (_phase4TargetDespawned && !_phase4HeadRecentered)
                        _phase4HeadRecentered = true;
                    break;
                default:
                    break;
            }
        }

        private void OnWarmupTargetSelected()
        {
            ++_warmupSelectedCount;
            // Debug.LogError($"Warmup Count incremented. Current Count: {_warmupSelectedCount}");
        }

        private void OnHitPerformed_AppP2()
        {
            _phase2ProjectileDespawned = true;
        }

        private void OnNextProjectileRequested_AppP3()
        {
            _phase3NextProjectileRequested = true;
        }

        private void OnNextProjectileRequested_AppP4()
        {
            _phase4TargetRequested = true;
        }

        private void OnValidSelection(Transform objTransform, Collider objCollider)
        {
            if (objCollider == null || objTransform == null) return;

            if (!_triggerPressed && (_collisionLayerMask.value & (1 << objCollider.gameObject.layer)) > 0)
            {
                var currSelection = objCollider.GetComponent<ProjectileController>();
                currSelection.HandleSelectionEnter();

                // objCollider.GetComponent<ProjectileController>().ReturnToPool();
                // OnHit?.Invoke();
                _triggerPressed = true;
            }
        }

        private void OnValidSelectionCancelled()
        {
            _triggerPressed = false;
        }

        private void OnValidInteractionStarted(Transform transform, Collider collider, Vector3 _)
        {
            var currProj = transform.GetComponent<ProjectileController>();

            if (_cachedProjectile_Interaction != null && _cachedProjectile_Interaction != currProj)
            {
                _cachedProjectile_Interaction.HandleInteractionExit();
            }

            _cachedProjectile_Interaction = currProj;

            if (_cachedProjectile_Interaction == null)
                return;

            if ((Phase == AppPhase.Phase2 || Phase == AppPhase.Phase3) && !_centeringReticleDespawned)
            {
                _cachedProjectile_Interaction.HandleInteractionEnter();
            }
            else if (Phase == AppPhase.Warmup || Phase == AppPhase.Phase4)
            {
                _cachedProjectile_Interaction.HandleInteractionEnter();
            }
        }

        private void OnValidInteractionPerformed(Transform transform, Collider collider, Vector3 _)
        {
            
        }

        private void OnValidInteractionCancelled()
        {
            // var currProjectile = transform.GetComponent<ProjectileController>();

            if (_cachedProjectile_Interaction != null && !_centeringReticleDespawned)
            {
                _cachedProjectile_Interaction.CenteringReticleContracting = false;
            }
        }
        #endregion

        #region Spawn Coroutines
        private IEnumerator SpawnCoroutine_Warmup()
        {
            _spawnCount = 0;
            _warmupSelectedCount = 0;

            int maxCount = _defaultTransforms.Length;

            while (_spawnCount < maxCount)
            {
                var currTrnsfrm = _defaultTransforms[_spawnCount];

                ProjectileController item = (ProjectileController)_pool.SpawnItem(currTrnsfrm.position, currTrnsfrm.rotation, InitializeWarmupTarget);

                item.OnWarmupTargetSelected += OnWarmupTargetSelected;

                ++_spawnCount;
            }

            yield return new WaitUntil(() => _warmupSelectedCount >= maxCount);

            OnWarmupComplete?.Invoke();
        }

        private IEnumerator SpawnCoroutine_AppP2()
        {
            _spawnCount = 0;

            while (_spawnCount < _phase2SpawnCount)
            {
                // var angleRad = UnityEngine.Random.Range(0f, 360f).ToRadians();
                // var radius = UnityEngine.Random.Range(_minSpawnRadius2, _maxSpawnRadius2);
                var pos = _lookAreaGenerator.GetRandomPointOnMesh();
                var dir = (pos - _camHMD.position).normalized;
                // pos += (dir * UnityEngine.Random.Range(0f, _maxSpawnDistance));

                // Access the look area handler and use the spawn function
                // var delay = UnityEngine.Random.Range(_minSpawnDelay, _maxSpawnDelay);

                // yield return new WaitForSecondsRealtime(delay);
                _spawnCenter.localPosition = Vector3.zero;
                _spawnCenter.localRotation = Quaternion.identity;
                _pool.SpawnItem(_spawnCenter.position, _spawnCenter.rotation, InitializeCenteringReticle);

                yield return new WaitUntil(() => _centeringReticleDespawned);

                // spawn on a random location within a radius range and angle range
                _spawnCenter.position = pos;
                _spawnCenter.LookAt(_camHMD);
                _pool.SpawnItem(_spawnCenter.position, _spawnCenter.rotation, InitalizePhase2Projectile);
                ++_spawnCount;

                Plane currPlane = new Plane(_defaultLookAreaNormalVector, _defaultSpawnPosition);
                _dataHandler.MeasureUserResponse_AppP2(_defaultSpawnPosition, _spawnCenter.position, _defaultLookAreaUpVector, currPlane);
                _centeringReticleDespawned = false;

                yield return new WaitUntil(() => _phase2ProjectileDespawned);

                _phase2ProjectileDespawned = false;
            }

            ResetGame(false);

            OnPhase2Complete?.Invoke();
        }

        private IEnumerator SpawnCoroutine_AppP3()
        {
            _spawnCount = 0;

            while (_spawnCount < _bezierSplines.Length)
            {
                var currSpline = _bezierSplines[_spawnCount];
                currSpline.gameObject.SetActive(true);

                _spawnCenter.position = currSpline.GetPoint(0);
                _pool.SpawnItem(_spawnCenter.position, _spawnCenter.rotation, (item) => { InitializePhase3Projectile(item, currSpline); });
                ++_spawnCount;

                yield return new WaitUntil(() => _phase3NextProjectileRequested);

                currSpline.gameObject.SetActive(false);
                _phase3NextProjectileRequested = false;
            }

            ResetGame(false);

            OnPhase3Complete?.Invoke();
            OnEnablePhase3NextButton?.Invoke();
        }

        private IEnumerator SpawnCoroutine_AppP4()
        {
            int phase4SpawnCount = 8;
            int phase4Offset = _phase4Mode == 0 ? 2 : 1; 

            for (int i = 0; i < phase4SpawnCount; i += phase4Offset)
            {
                _spawnCenter.localPosition = Vector3.zero;
                _spawnCenter.localRotation = Quaternion.identity;
                _pool.SpawnItem(_spawnCenter.position, _spawnCenter.rotation, InitializeCenteringReticle);

                yield return new WaitUntil(() => _centeringReticleDespawned);

                var pos = _lookAreaGenerator.GetEdgePoint(i);
                _pool.SpawnItem(pos, Quaternion.identity, InitializePhase4Target);

                yield return new WaitUntil(() => _phase4TargetDespawned);

                
                BlackoutScreenHandler.Instance.SetBlackoutScreen(true, P4_BLACKOUT_TEXT);
                _lookAreaGenerator.SetMeshDisplayStatus(false);
                _lookAreaGenerator.SetMeshInteraction(true);

                yield return new WaitUntil(() => _phase4HeadRecentered);

                _lookAreaGenerator.DisplayTargetDistanceFromOrigin_AppP4(_cachedProjectileScaleFactor);
                _lookAreaGenerator.SetMeshDisplayStatus(true);
                _lookAreaGenerator.SetMeshInteraction(false);

                BlackoutScreenHandler.Instance.SetBlackoutScreen(false);
                OnEnablePhase4NextButton?.Invoke();

                // Display the current position of the head pointer on the look area with respect to the "true center"

                yield return new WaitUntil(() => _phase4TargetRequested);

                _lookAreaGenerator.ClearDisplay_AppP4();
                _centeringReticleDespawned = false;
                _phase4TargetRequested = false;
                _phase4TargetDespawned = false;
                _phase4HeadRecentered = false;
            }


            ResetGame(false);
            
            OnPhase4Complete?.Invoke();
            OnEnablePhase4NextButton?.Invoke();
        }
        #endregion

        #region Private Methods
        private IEnumerator WaitForInputSystem()
        {
            yield return new WaitUntil(() => InputManager.IsInstantiated);

            InputManager.InputActions.XRIRightHandInteraction.Select.performed += RightGripPressed;

            _inputsAssigned = true;
        }

        private void ShowPassthroughUnderlay(bool status)
        {
            var _hmdCam = _camHMD.GetComponent<Camera>();

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

        private void InitializeWarmupTarget(PooledItem item)
        {
            var res = (ProjectileController)item;

            res.InitializeItem(ProjectileMode.WarmupTarget);
        }

        private void InitializeCenteringReticle(PooledItem item)
        {
            var res = (ProjectileController) item;

            res.InitializeItem(ProjectileMode.CenteringReticle);
            res.OnCenteringReticleDespawned += () => { _centeringReticleDespawned = true; };
        }

        private void InitalizePhase2Projectile(PooledItem item)
        {
            var res = (ProjectileController) item;

            res.InitializeItem(ProjectileMode.Phase2Projectile);
        }

        private void InitializePhase3Projectile(PooledItem item, BezierSpline spline)
        {
            var res = (ProjectileController) item;

            res.OnPhase3ProjectileMovementStarted += (reference) => { _dataHandler.BeginUserPathTracking_AppP3(reference, -1 * _interactionPanel_AppP3.transform.forward); };
            res.OnPhase3ProjectileMovementComplete += () => { _dataHandler.EndUserPathTracking_AppP3(); };
            res.InitializeItem(ProjectileMode.Phase3Projectile);
            res.InjectSpline(spline);
        }

        private void InitializePhase4Target(PooledItem item)
        {
            var res = (ProjectileController)item;

            res.InitializeItem(ProjectileMode.Phase4Target);

            res.OnPhase4TargetDespawned += () =>
            {
                _phase4TargetDespawned = true;
            };
        }

        private void ResetGame(bool hardRest = true)
        {
            if (_spawnCoroutine != null)
            {
                StopCoroutine(_spawnCoroutine);
                _spawnCoroutine = null;
            }

            if (_cachedProjectile_Interaction != null)
            {
                _cachedProjectile_Interaction.ReturnToPool();
                _cachedProjectile_Interaction = null;
            }

            _triggerPressed = false;
            _inputsAssigned = false;
            _centeringReticleDespawned = false;
            _phase2ProjectileDespawned = false;
            _phase4TargetRequested = false;
            _phase3NextProjectileRequested = false;
            _phase4TargetDespawned = false;
            _phase4HeadRecentered = false;

            _spawnCenter.localPosition = _defaultSpawnPosition;
            _spawnCenter.rotation = _defaultSpawnRotation;

            _interactionPanel_AppP3.DeInitializePanel();

            if (hardRest)
            {
                _phase = 0;
                _cachedProjectileScaleFactor = 1f;

                _gameSetWarmup.SetActive(false);
                _gameSetPhase2.SetActive(false);
                _gameSetPhase3.SetActive(false);

                foreach (var item in _bezierSplines)
                {
                    item.gameObject.SetActive(false);
                }

                _lookAreaGenerator.RestrictLookAreaModification();
                _lookAreaGenerator.gameObject.SetActive(false);

                _dataHandler.ResetMetrics();
            }

            _pool.CleanPool();
            _spawnCount = 0;
            _warmupSelectedCount = 0;
        }
        #endregion
    }
}