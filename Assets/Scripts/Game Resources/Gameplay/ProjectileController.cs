using BezierSolution;
using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using GameResources.Pooling;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace GameResources.Gameplay
{
    public enum ProjectileMode
    {
        CenteringReticle = 0,
        Phase2Projectile = 1,   // Projectiles that get shot towards user in phase 2
        Phase3Projectile = 2,   // Projectile that will move on a fixed route in phase 3
        Phase4Target = 3,       // Target that, once looked at, will trigger the black screen for recentering
        WarmupTarget = 4,
    }

    public class ProjectileController : PooledItem, ICursorInteractable
    {
        [Header("Spline Follow")]
        [SerializeField]
        private BezierWalkerWithSpeed _splineFollower;

        [Space(5)]

        [Header("Misc Properties")]
        [SerializeField]
        private float _targetFollowVelocity = 1.9f;
        [SerializeField, Range(0f, 10f)]
        private float _centeringReticleResizingDuration;
        [SerializeField, Range(0f, 2f)]
        private float _centeringReticleResettingDuration;
        [SerializeField, Range(0f, 2f)]
        private float _distanceBasedProjectileResizeFactor;
        [SerializeField]
        private Rigidbody _rb;
        [SerializeField]
        private LayerMask _validCollisionLayers;
        [SerializeField, ColorUsage(true, true)]
        private Color _centeringReticleColor,
            _phase2ProjectileColor,
            _phase3ProjectileColor,
            _phase4TargetColor,
            _warmupTargetColor1, _warmupTargetColor2;
        [SerializeField]
        TrailRenderer _trailRenderer;

        #region Private Properties
        private Material _projectileMaterial;
        private ProjectileMode _currentProjectileMode = ProjectileMode.Phase2Projectile;
        private Vector3 _currentVelocity = Vector3.zero;
        private bool _isInteractable = false,
            _centeringReticleContracting = false,
            _warmupItemSelected = false,
            _phase3startPath = false;
        private float _originalScale = 1.2f, _finalScale = 0.35f;

        private TweenerCore<Vector3, Vector3, VectorOptions> _centeringReticleResizingTween = null;
        private Coroutine _phase3AwaitCoroutine = null;
        #endregion

        #region Public Properties
        public Action OnWarmupTargetSelected = null;
        public Action OnCenteringReticleDespawned = null;
        public Action<Transform> OnPhase3ProjectileMovementStarted = null;
        public Action OnPhase3ProjectileMovementComplete = null;
        public Action OnPhase4TargetDespawned = null;

        public bool IsInteractable
        {
            get => _isInteractable;
            private set => _isInteractable = value;
        }

        public ProjectileMode CurrentProjectileMode
        {
            get { return _currentProjectileMode; }
            private set
            {
                // Only allow property to be set while the object is pooled
                if (_isPooled)
                    _currentProjectileMode = value;
            }
        }

        public bool CenteringReticleContracting
        {
            get { return _centeringReticleContracting; }
            set
            {
                if (!_isPooled && CurrentProjectileMode == ProjectileMode.CenteringReticle)
                {
                    _centeringReticleContracting = value;
                }
            }
        }
        #endregion

        #region Overrides
        protected override void OnSpawn()
        {
            _currentVelocity = Vector3.zero;
            _originalScale = transform.localScale.x; // Only taking one dimension since the dimensions will be equal

            if (_projectileMaterial == null)
                _projectileMaterial = gameObject.GetComponent<MeshRenderer>().material;

            switch (CurrentProjectileMode)
            {
                case ProjectileMode.CenteringReticle:
                    _projectileMaterial.SetColor("_EmissionColor", _centeringReticleColor);
                    break;
                case ProjectileMode.Phase2Projectile:
                default:
                    _projectileMaterial.SetColor("_EmissionColor", _phase2ProjectileColor);
                    SetProjectileSizeFromDistance();
                    break;
                case ProjectileMode.Phase3Projectile:
                    _projectileMaterial.SetColor("_EmissionColor", _phase3ProjectileColor);
                    if (_splineFollower == null)
                        _splineFollower = transform.GetComponent<BezierWalkerWithSpeed>();

                    _phase3startPath = false;
                    _splineFollower.enabled = true;
                    _splineFollower.executionStatus = false;
                    _splineFollower.speed = _targetFollowVelocity;
                    _splineFollower.travelMode = TravelMode.Once;
                    _splineFollower.onPathCompleted.AddListener(OnSplinePathComplete);

                    _trailRenderer.enabled = true;

                    if (_phase3AwaitCoroutine != null)
                    {
                        StopCoroutine(_phase3AwaitCoroutine);
                        _phase3AwaitCoroutine = null;
                    }

                    break;
                case ProjectileMode.Phase4Target:
                    _projectileMaterial.SetColor("_EmissionColor", _phase4TargetColor);
                    SetProjectileSizeFromDistance();
                    break;
                case ProjectileMode.WarmupTarget:
                    _projectileMaterial.SetColor("_EmissionColor", _warmupTargetColor1);
                    break;
            }
        }

        protected override void OnDespawn()
        {
            _currentVelocity = Vector3.zero;

            transform.localScale = Vector3.one;
            transform.localScale = Vector3.zero;
            transform.rotation = Quaternion.identity;
            _trailRenderer.enabled = false;

            _isInteractable = false;
            _centeringReticleContracting = false;
            _warmupItemSelected = false;

            OnWarmupTargetSelected = null;
            OnCenteringReticleDespawned = null;
            OnPhase3ProjectileMovementStarted = null;
            OnPhase3ProjectileMovementComplete = null;

            if (_currentProjectileMode == ProjectileMode.Phase3Projectile)
            {
                _splineFollower.onPathCompleted.RemoveAllListeners();
            }

            if (_phase3AwaitCoroutine != null)
            {
                StopCoroutine(_phase3AwaitCoroutine);
                _phase3AwaitCoroutine = null;
            }
        }

        private void Update()
        {
            switch (_currentProjectileMode)
            {
                case ProjectileMode.CenteringReticle:
                    SimulateCenteringReticle();
                    break;
                case ProjectileMode.Phase2Projectile:
                    SimulatePhase2Projectile();
                    break;
                case ProjectileMode.Phase3Projectile:
                    SimulatePhase3Projectile();
                    break;
                case ProjectileMode.Phase4Target:
                    SimulatePhase4Projectile();
                    break;
                case ProjectileMode.WarmupTarget:
                    SimulateWarmupTargets();
                    break;
                default:
                    // SimulatePhase2Projectile();
                    break;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != null && (_validCollisionLayers.value & (1 << other.gameObject.layer)) > 0)
            {
                ReturnToPool();
            }
        }
        #endregion

        #region Private Methods
        private void SetProjectileSizeFromDistance()
        {
            var distTuple = GameplayHandler.Instance.GetDistanceTuple(transform);
            float scaleFactor = (distTuple.Item1 / distTuple.Item2) * _distanceBasedProjectileResizeFactor;
            Vector3 newLocalScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
            transform.localScale = newLocalScale;
        }

        private void SimulateCenteringReticle()
        {
            if (!IsPooled)
            {
                if (_centeringReticleContracting && _centeringReticleResizingTween == null)
                {
                    _centeringReticleResizingTween = transform.DOScale(_finalScale, _centeringReticleResizingDuration).OnComplete(() => DespawnCenteringReticle());
                }
                else if (!_centeringReticleContracting && _centeringReticleResizingTween != null)
                {
                    _centeringReticleResizingTween.Kill();
                    _centeringReticleResizingTween = null;

                    transform.DOScale(_originalScale, _centeringReticleResettingDuration);
                }
            }
        }

        private void SimulatePhase2Projectile()
        {
            if (!IsPooled)
            {
                // Additional functionality that can optionally be added. Makes the target move forward

                //var targetVelocity = transform.forward * _targetVelocity;
                //var currentVelocity = _rb.velocity;
                
                //_rb.AddForce((targetVelocity - currentVelocity) * _velocityBlendStrength);
            }
        }

        private void SimulatePhase3Projectile()
        {
            if (!IsPooled)
            {
                _phase3AwaitCoroutine = StartCoroutine(AwaitPhase3PathStart());
            }
        }

        private void SimulatePhase4Projectile()
        {

        }

        private void SimulateWarmupTargets()
        {

        }

        private void DespawnCenteringReticle()
        {
            OnCenteringReticleDespawned?.Invoke();
            OnCenteringReticleDespawned = null;

            ReturnToPool();
        }

        private void DespawnPhase4Target()
        {
            OnPhase4TargetDespawned?.Invoke();
            OnPhase4TargetDespawned = null;

            ReturnToPool();
        }

        private IEnumerator AwaitPhase3PathStart()
        {
            yield return new WaitUntil(() => _phase3startPath);

            // _splineFollower.Execute(Time.deltaTime);
            OnPhase3ProjectileMovementStarted?.Invoke(transform);
            _splineFollower.executionStatus = true;
        }

        private void OnSplinePathComplete()
        {
            GameplayHandler.OnEnablePhase3NextButton?.Invoke();
            _splineFollower.executionStatus = false;
            OnPhase3ProjectileMovementComplete?.Invoke();
            ReturnToPool();
        }
        #endregion

        #region Public Methods
        public void InitializeItem(ProjectileMode mode)
        {
            CurrentProjectileMode = mode;
            IsInteractable = true;
        }

        public void HandleSelectionEnter()
        {
            switch (CurrentProjectileMode)
            {
                case ProjectileMode.Phase2Projectile:
                    GameplayHandler.OnPhase2Hit?.Invoke();
                    ReturnToPool();
                    break;
            }
        }

        public void HandleSelectionExit()
        {

        }

        public void HandleInteractionEnter()
        {
            switch (CurrentProjectileMode)
            {
                case ProjectileMode.CenteringReticle:
                    CenteringReticleContracting = true;
                    break;
                case ProjectileMode.WarmupTarget:
                    if (_warmupItemSelected)
                        return;

                    _projectileMaterial.SetColor("_EmissionColor", _warmupTargetColor2);
                    _warmupItemSelected = true;
                    OnWarmupTargetSelected?.Invoke();
                    break;
                case ProjectileMode.Phase3Projectile:
                    // Debug.LogError("Phase3Projectile | Starting");
                    _phase3startPath = true;
                    break;
                case ProjectileMode.Phase4Target:
                    DespawnPhase4Target();
                    break;
                default:
                    break;
            }
        }

        public void HandleInteractionExit()
        {
            switch (CurrentProjectileMode)
            {
                case ProjectileMode.CenteringReticle:
                    CenteringReticleContracting = false;
                    break;
            }
        }

        public void InjectSpline(BezierSpline spline)
        {
            if (_splineFollower != null)
                _splineFollower.spline = spline;

            _splineFollower.NormalizedT = 0;
            _splineFollower.executionStatus = false;
            // transform.position = spline[0].position;
        }

        public void UpdateScale(float scale)
        {
            _originalScale = transform.localScale.x * scale;
            _finalScale *= scale;
            transform.localScale *= scale;

            if (_splineFollower == null)
                _splineFollower = transform.GetComponent<BezierWalkerWithSpeed>();

            _targetFollowVelocity *= scale;
            _trailRenderer.minVertexDistance *= scale;
            _trailRenderer.startWidth *= scale;
        }
        #endregion
    }
}