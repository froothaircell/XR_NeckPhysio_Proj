using CoreResources.Managers.InputManagement;
using CoreResources.Singleton;
using GameResources.Gameplay;
using GameResources.Gameplay.VRController;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace CoreResources.Utils
{
    [RequireComponent(typeof(MeshFilter))]
    public class LookAreaGenerator : DestroyableMonoSingleton<LookAreaGenerator>
    {
        [SerializeField] private Transform _centerPos;
        [SerializeField] private GameObject _interactionPanel, _appP4TextBox;
        [SerializeField] private TextMeshProUGUI _appP4Text;
        [SerializeField] private List<Transform> _points = new List<Transform>(8);
        [SerializeField] private float _cornerRadius = 0.1f;
        [SerializeField] private int _cornerResolution = 4;
        [SerializeField] private Transform _spawnPos;
        [SerializeField] private float _borderThickness = 0.1f, _maxRaycastDistance = 50f, _spherecastRadius = 1f;
        [SerializeField] private LayerMask _collisionLayerMask;

        [Header("Materials")]
        [SerializeField] private Material _fillMaterial;
        [SerializeField] private Material _borderMaterial, _lineMaterial;

        [Header("Meshes")]
        [SerializeField] private MeshFilter _fillMeshFilter;
        [SerializeField] private MeshRenderer _fillMeshRenderer;
        [SerializeField] private MeshFilter _borderMeshFilter;
        [SerializeField] private MeshRenderer _borderMeshRenderer;

        [Header("Gizmo Settings")]
        [SerializeField] private float _lineThickness = 0.01f;
        [SerializeField] private float _gizmoCircleRadius = 0.05f;

        private Mesh _fillMesh;
        private Mesh _borderMesh;

        private Transform _camHMD;
        private GameObject _originMarker;
        private GameObject _targetMarker;
        private GameObject _connectionLine;

        // Handling user input for handling movement
        private bool _lookAreaModificationAllowed = false;
        private bool _triggerPressed = false;
        private List<LookAreaInteractable> _interactables = new List<LookAreaInteractable>();
        private LookAreaInteractable _currentInteractable = null;
        private InteractionPanel _interactionPanelScript;
        private Vector3 _appP4LastHitPosition = Vector3.zero;

        public Vector3 AppP4LastHitPosition { get { return _appP4LastHitPosition; } }

        #region Overrides
        public override void OnInit()
        {
            if (_interactables.Count == 0)
            {
                for (int i = 0; i < _points.Count; i++)
                {
                    var currItem = _points[i].GetComponent<LookAreaInteractable>();
                    _interactables.Add(currItem);
                    currItem.InitializeInteractable(_centerPos);
                }
            }

            if (_interactionPanelScript == null)
                _interactionPanelScript = _interactionPanel.GetComponentInChildren<InteractionPanel>();
            
            _interactionPanelScript.InitializePanel();
            _appP4TextBox.SetActive(false);
            _interactionPanel.SetActive(false);
            _appP4LastHitPosition = Vector3.zero;
        }

        public override void OnDeInit()
        {
        }
        #endregion

        private void Update()
        {
            GenerateMeshes();

            if (_lookAreaModificationAllowed && _triggerPressed && _currentInteractable != null)
            {
                ProcessMovementInput();
            }
        }

        #region Public Methods
        public void InitSingleton(Transform camTransform)
        {
            if (camTransform == null)
            {
                Debug.LogError("Injected Camera transform is null!");
                return;
            }

            _camHMD = camTransform;
            InitSingleton();
        }

        public void RecalibrateInteractables(out Vector3 normal, out Vector3 up)
        {
            foreach (var interactable in _interactables)
                interactable.CalibrateInteractable();

            var topIter = _interactables.FirstOrDefault((x) => x.ViewingDirection == ViewAngleDirection.Top).transform;
            normal = -1 * topIter.forward;
            up = topIter.up;
        }

        public void ResetInteractables()
        {
            foreach (var interactable in _interactables)
                interactable.ResetInteractable();
        }

        public void GenerateMeshes()
        {
            if (_points.Count < 3)
            {
                UnityEngine.Debug.LogError("Need at least 3 points.");
                return;
            }
            
            _fillMesh = new Mesh();
            _borderMesh = new Mesh();

            List<Vector3> innerArc = GenerateArcPoints(_points, _cornerRadius, _cornerResolution);

            // Generate inner fan mesh
            _fillMesh = CreateFanMesh(Vector3.zero, innerArc);
            _fillMeshFilter.mesh = _fillMesh;
            _fillMeshRenderer.material = _fillMaterial;

            // Generate outer ring mesh
            _borderMesh = CreateRingMesh(innerArc, _borderThickness);
            _borderMeshFilter.mesh = _borderMesh;
            _borderMeshRenderer.material = _borderMaterial;
        }

        public void AllowLookAreaModification()
        {
            for (int i = 0; i < _interactables.Count; i++)
            {
                _interactables[i].EnableInteraction();
            }

            _lookAreaModificationAllowed = true;

            if (CursorHandler.IsInstantiated)
            {
                CursorHandler.Instance.OnValidSelectionPerformed += OnValidSelectionPerformed;
                CursorHandler.Instance.OnValidSelectionCancelled += OnValidSelectionCancelled;
            }
        }

        public void RestrictLookAreaModification()
        {
            for (int i = 0; i < _interactables.Count; i++)
            {
                _interactables[i].DisableInteraction();
            }

            if (CursorHandler.IsInstantiated)
            {
                CursorHandler.Instance.OnValidSelectionPerformed -= OnValidSelectionPerformed;
                CursorHandler.Instance.OnValidSelectionCancelled -= OnValidSelectionCancelled;
            }
            
            _lookAreaModificationAllowed = false;
        }

        public void SetMeshInteraction(bool status)
        {
            if (status)
            {
                _interactionPanel.SetActive(true);
                _interactionPanelScript.InitializePanel();

                if (CursorHandler.IsInstantiated)
                    CursorHandler.Instance.OnValidInteractionPerformed += OnValidInteractionPerformed;
            }
            else
            {
                _interactionPanelScript.DeInitializePanel();
                _interactionPanel.SetActive(false);

                if (CursorHandler.IsInstantiated)
                    CursorHandler.Instance.OnValidInteractionPerformed -= OnValidInteractionPerformed;

                _appP4LastHitPosition = Vector3.zero;
            }
        }

        public void SetMeshDisplayStatus(bool status)
        {
            _fillMeshRenderer.enabled = status;
            _borderMeshRenderer.enabled = status;

            foreach (var point in _points)
            {
                point.GetComponent<MeshRenderer>().enabled = status;
            }
        }

        public void DisplayTargetDistanceFromOrigin_AppP4(float gizmosScale = 1f)
        {
            ClearDisplay_AppP4();

            Vector3 position = _appP4LastHitPosition;
            Vector3 origin = _centerPos.position;
            Vector3 displacement = position - origin;
            displacement /= gizmosScale;

            if (!_originMarker) _originMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _originMarker.transform.position = origin;
            _originMarker.transform.localScale = Vector3.one * _gizmoCircleRadius * gizmosScale;
            _originMarker.GetComponent<Renderer>().material = _lineMaterial;

            if (!_targetMarker) _targetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _targetMarker.transform.position = position;
            _targetMarker.transform.localScale = Vector3.one * _gizmoCircleRadius * gizmosScale;
            _targetMarker.GetComponent<Renderer>().material = _lineMaterial;

            if (!_connectionLine) _connectionLine = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Vector3 midPoint = (origin + position) / 2f;
            _connectionLine.transform.position = midPoint;
            _connectionLine.transform.up = (position - origin).normalized;
            float distance = displacement.magnitude;
            _connectionLine.transform.localScale = new Vector3(_lineThickness, distance / 2f, _lineThickness);
            _connectionLine.transform.localScale *= gizmosScale;
            _connectionLine.GetComponent<Renderer>().material = _lineMaterial;

            _appP4Text.text = $"Displacement: {displacement}\nDistance: {distance}";

            PhysiologicalDataHandler.Instance.RecordDistanceFromCenter_AppP4(displacement, distance);

            _appP4TextBox.SetActive(true);
            _originMarker.SetActive(true);
            _targetMarker.SetActive(true);
            _connectionLine.SetActive(true);
        }

        public void ClearDisplay_AppP4()
        {
            if (_originMarker) _originMarker.SetActive(false);
            if (_targetMarker) _targetMarker.SetActive(false);
            if (_connectionLine) _connectionLine.SetActive(false);

            _appP4Text.text = string.Empty;
            _appP4TextBox.SetActive(false);
        }
        
        public void RecordViewingAngleBounds()
        {
            List<Vector3> currInteractablePositions = new List<Vector3>();

            for (int i = 0; i < 8; i++)
            {
                var curInteractable = _interactables.FirstOrDefault((x) => x.ViewingDirection == (ViewAngleDirection)i);
                var currPos = curInteractable.transform.position;
                currInteractablePositions.Add(currPos);
            }

            PhysiologicalDataHandler.Instance.RecordViewingAngleBounds_Sorted(_centerPos.position, currInteractablePositions);
        }
        #endregion

        private void ProcessMovementInput()
        {
            if (_triggerPressed)
            {
                var pos = _camHMD.position;
                var rot = _camHMD.forward;

                _currentInteractable.UpdatePositionByReferenceLine(pos, rot);
            }
        }

        private List<Vector3> GenerateArcPoints(List<Transform> points, float radius, int resolution)
        {
            List<Vector3> arcPoints = new List<Vector3>();

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 prev = points[(i - 1 + points.Count) % points.Count].localPosition;
                Vector3 current = points[i].localPosition;
                Vector3 next = points[(i + 1) % points.Count].localPosition;

                Vector3 dirA = (prev - current).normalized;
                Vector3 dirB = (next - current).normalized;

                Vector3 start = current + dirA * radius;
                Vector3 end = current + dirB * radius;

                for (int j = 0; j < resolution; j++)
                {
                    float t = j / (float)(resolution - 1);
                    Vector3 arcPoint = Bezier(start, current, end, t);
                    arcPoints.Add(arcPoint);
                }
            }

            return arcPoints;
        }

        private Mesh CreateFanMesh(Vector3 center, List<Vector3> arcPoints)
        {
            Mesh mesh = new Mesh();

            List<Vector3> vertices = new List<Vector3> { center };
            vertices.AddRange(arcPoints);

            List<int> triangles = new List<int>();
            int count = arcPoints.Count;
            for (int i = 0; i < count; i++)
            {
                triangles.Add(0);
                triangles.Add(1 + i);
                triangles.Add(1 + (i + 1) % count);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private Mesh CreateRingMesh(List<Vector3> innerArc, float thickness)
        {
            Mesh mesh = new Mesh();

            List<Vector3> outerArc = new List<Vector3>();
            foreach (var pt in innerArc)
            {
                Vector3 dir = (pt - Vector3.zero).normalized;
                outerArc.Add(pt + dir * thickness);
            }

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            int count = innerArc.Count;

            for (int i = 0; i < count; i++)
            {
                Vector3 innerA = innerArc[i];
                Vector3 innerB = innerArc[(i + 1) % count];
                Vector3 outerA = outerArc[i];
                Vector3 outerB = outerArc[(i + 1) % count];

                int index = vertices.Count;

                vertices.Add(innerA); // 0
                vertices.Add(innerB); // 1
                vertices.Add(outerA); // 2
                vertices.Add(outerB); // 3

                // Tri 1
                triangles.Add(index + 0);
                triangles.Add(index + 2);
                triangles.Add(index + 3);

                // Tri 2
                triangles.Add(index + 0);
                triangles.Add(index + 3);
                triangles.Add(index + 1);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public Vector3 GetRandomPointOnMesh()
        {
            if (_fillMesh == null)
            {
                UnityEngine.Debug.LogError("Mesh not assigned.");
                return Vector3.zero;
            }

            Vector3[] vertices = _fillMesh.vertices;
            int[] triangles = _fillMesh.triangles;

            // Pick a random triangle
            int triIndex = Random.Range(0, triangles.Length / 3) * 3;

            Vector3 a = transform.TransformPoint(vertices[triangles[triIndex]]);
            Vector3 b = transform.TransformPoint(vertices[triangles[triIndex + 1]]);
            Vector3 c = transform.TransformPoint(vertices[triangles[triIndex + 2]]);

            var pos = GetRandomPointInTriangle(a, b, c);

            return pos;
        }

        /// <summary>
        /// Get a point on the edge of the look area
        /// </summary>
        /// <param name="index">
        /// Index of the edge
        /// </param>
        /// <returns></returns>
        public Vector3 GetEdgePoint(int index)
        {
            if (index < 0 || index >= _interactables.Count)
            {
                UnityEngine.Debug.LogError("Index out of bounds");
                return Vector3.zero;
            }

            return _interactables[index].transform.position;
        }

        private Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            // Quadratic Bezier curve
            return (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
        }

        private Vector3 GetRandomPointInTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            // Generate random barycentric coordinates
            float r1 = Mathf.Sqrt(Random.value);
            float r2 = Random.value;

            Vector3 point = (1 - r1) * a + r1 * (1 - r2) * b + r1 * r2 * c;
            return point;
        }

        #region Event Listeners
        private void OnValidSelectionPerformed(Transform objTransform, Collider objCollider)
        {
            var interactable = objCollider.GetComponent<LookAreaInteractable>();
            if (interactable != null)
            {
                _currentInteractable = interactable;
                _currentInteractable.SetHighlight(true);
                _triggerPressed = true;
            }
        }

        private void OnValidSelectionCancelled()
        {
            if (_currentInteractable != null)
                _currentInteractable.SetHighlight(false);

            _triggerPressed = false;
            _currentInteractable = null;
        }

        private void OnValidInteractionPerformed(Transform objTransform, Collider objCollider, Vector3 hitPosition)
        {
            if (GameplayHandler.Instance.Phase == AppPhase.Phase4)
            {
                _appP4LastHitPosition = hitPosition;
            }
        }
        #endregion
    }
}