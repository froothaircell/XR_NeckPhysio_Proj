using CoreResources.Managers;
using CoreResources.Singleton;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GameResources.Gameplay
{
    public class PhysiologicalDataHandler : DestroyableMonoSingleton<PhysiologicalDataHandler>
    {
        private Transform _camHMD;

        private bool _userMeasurementStarted = false;
        private int _score;
        private const int MAX_SCORE = 15;

        // To scale our readings when accounting for different AR game set placements and scales
        private float _scaleFactor = 1f;

        public int MaxScore => MAX_SCORE;


        [Header("Data Collection Settings")]
        [SerializeField] private int _bufferSizeHMDPosRot = 60;
        [SerializeField] private int _bufferSizeAppP2Response = 60;
        [SerializeField] private int _bufferSizeAppP3Response = 60;
        
        #region App Phase 2
        private string _responsePathP2;
        private string _responseDataP2;
        private bool _userResponseMeasurementStartedP2 = false;
        private Plane _cachedTargetPlaneP2;
        private Vector3 _cachedCenter, _cachedTarget, _cachedTargetPlaneUp;
        private List<(Vector2 point, DateTime timestamp)> _responseBuffer;
        private DateTime _responseStartTime;
        private int _responseIndexP2 = 0;
        private const string RESPONSE_PATH_HEADER = "Timestamp,PosX,PosY\n";
        private const string RESPONSE_DATA_HEADER = "ResponseID,StartTime,DurationSeconds,CenterX,CenterY,TargetX,TargetY\n";
        private const string BUFFER_DATA_KEY_P2RESPONSE = "respBuffer";

        public static Action<int> OnScoreUpdated;
        #endregion

        #region App Phase 3
        private string _distanceCsvPathP3;
        private bool _userResponseMeasurementStartedP3 = false;
        private Transform _targetReferenceP3 = null;
        private Plane _cachedTargetPlaneP3;
        private List<(float distance, DateTime timestamp)> _distanceBufferP3;
        private const string DISTANCE_HEADER_P3 = "Timestamp,Distance\n";
        private const string BUFFER_DATA_KEY_P3DISTANCE = "p3distanceBuffer";
        private int _distanceSampleIndexP3 = 0;
        #endregion

        #region App Phase 4
        private string _distanceCsvPathP4 = "";
        private const string DISTANCE_HEADER_P4 = "Timestamp,DistanceX,DistanceY,DistanceZ,Displacement\n";
        #endregion

        #region App Wide Measurements
        private List<string> _dataBuffer = new List<string>();
        private string _rootFolderPath;
        private string _hmdPosRotCsvPath;
        private string _fileTime;
        private string _currFolderPath; // Change path for each user
        private const string HMDPOSROT_HEADER = "Timestamp,PosX,PosY,PosZ,RotX,RotY,RotZ,RotW\n";
        private const string BUFFER_DATA_KEY_HMDPOSROT = "bufferDataHMD";
        #endregion

        #region Overrides
        public override void OnInit()
        {
            ResetMetrics();

            GameplayHandler.OnPhase2Hit += OnTargetHit_AppP2;
            GameplayHandler.OnPlayEvent += SetCurrentMeasurementMode;

            _rootFolderPath = Path.Combine(Application.persistentDataPath, "PhysioLogs");
            if (!Directory.Exists(_rootFolderPath))
                Directory.CreateDirectory(_rootFolderPath);

            _fileTime = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        }

        public override void OnDeInit()
        {
            ResetMetrics();

            OnScoreUpdated = null;

            TaskUtilitiesManager.CancelTask(WriteDataAsync_HMDPosRot);

            GameplayHandler.OnPhase2Hit -= OnTargetHit_AppP2;
            GameplayHandler.OnPlayEvent -= SetCurrentMeasurementMode;
        }

        private void FixedUpdate()
        {
            ProcessMetrics();
        }
        #endregion

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

        public void ResetMetrics()
        {
            _userMeasurementStarted = false;
            _userResponseMeasurementStartedP2 = false;
            _userResponseMeasurementStartedP3 = false;

            _cachedCenter = _cachedTarget = default;
            _cachedTargetPlaneP2 = default;

            _targetReferenceP3 = null;

            _score = 0;
            _responseIndexP2 = 0;
            _distanceSampleIndexP3 = 0;
            _dataBuffer.Clear();
            _currFolderPath = string.Empty;

            if (GameplayHandler.Instance.Phase >= (AppPhase) 1)
                OnScoreUpdated?.Invoke(_score);
        }

        public void SetScale(float scale)
        {
            _scaleFactor = scale;
        }

        public void RecordViewingAngleBounds_Sorted(Vector3 center, List<Vector3> viewLimits)
        {
            if (_currFolderPath == String.Empty)
            {
                throw new Exception("Folder Path is not set!");
            }

            if (_camHMD == null)
            {
                Debug.LogError("CamHMD is not set.");
                return;
            }

            if (viewLimits == null || viewLimits.Count != 8)
            {
                Debug.LogError("Expected 8 view limit edge points.");
                return;
            }

            // Direction labels in fixed order
            string[] directionLabels = new[]
            {
                "Top", "TopRight", "Right", "BottomRight",
                "Bottom", "BottomLeft", "Left", "TopLeft"
            };

            Vector3 camPosition = _camHMD.position;
            Vector3 forward = _camHMD.forward;

            List<float> angles = new List<float>();

            for (int i = 0; i < viewLimits.Count; i++)
            {
                Vector3 toEdge = (viewLimits[i] - camPosition).normalized;
                float angle = Vector3.Angle(forward, toEdge);
                angles.Add(angle);
            }

            // Create CSV contents
            string timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            StringBuilder sb = new StringBuilder();

            // Header
            sb.Append("Timestamp");
            foreach (string label in directionLabels)
                sb.Append($",{label}");
            sb.AppendLine();

            // Data
            sb.Append(timestamp);
            foreach (float angle in angles)
                sb.Append($",{angle:F2}");
            sb.AppendLine();

            string filename = $"P1_ViewLimits.csv";
            string viewLimitsPath = Path.Combine(_currFolderPath, filename);

            File.WriteAllText(viewLimitsPath, sb.ToString());
        }

        public void MeasureUserResponse_AppP2(Vector3 center, Vector3 target, Vector3 up, Plane targetPlane)
        {
            if (_currFolderPath == String.Empty)
            {
                throw new Exception("Folder Path is not set!");
            }

            if (!_userResponseMeasurementStartedP2)
            {
                _userResponseMeasurementStartedP2 = true;

                _cachedTargetPlaneP2 = targetPlane;
                _cachedCenter = center;
                _cachedTarget = target;
                _cachedTargetPlaneUp = up;

                _responseBuffer = new List<(Vector2, DateTime)>();
                _responseStartTime = DateTime.UtcNow;

                // Build file paths for this response:
                string idx = _responseIndexP2.ToString();
                _responsePathP2 = Path.Combine(_currFolderPath,
                    $"P2_ResponsePath_{idx}.csv");
                _responseDataP2 = Path.Combine(_currFolderPath,
                    $"P2_ResponseData_{idx}.csv");

                File.WriteAllText(_responsePathP2, RESPONSE_PATH_HEADER);
                File.WriteAllText(_responseDataP2, RESPONSE_DATA_HEADER);
            }
        }
        
        public void BeginUserPathTracking_AppP3(Transform reference, Vector3 referenceForward)
        {
            if (_currFolderPath == String.Empty)
            {
                throw new Exception("Folder Path is not set!");
            }

            if (!_userResponseMeasurementStartedP3)
            {
                _targetReferenceP3 = reference;
                _cachedTargetPlaneP3 = new Plane(referenceForward, _targetReferenceP3.position);
                _userResponseMeasurementStartedP3 = true;

                _distanceBufferP3 = new List<(float, DateTime)>();

                string filename = $"P3_Distance_{_distanceSampleIndexP3}.csv";
                _distanceCsvPathP3 = Path.Combine(_currFolderPath, filename);

                File.WriteAllText(_distanceCsvPathP3, DISTANCE_HEADER_P3); // Header
            }
        }

        public void EndUserPathTracking_AppP3()
        {
            if (_userResponseMeasurementStartedP3)
            {
                FlushP3DistanceBuffer();

                _targetReferenceP3 = null;
                _cachedTargetPlaneP3 = default;
                _userResponseMeasurementStartedP3 = false;
                
                _distanceSampleIndexP3++;
            }
        }

        public void RecordDistanceFromCenter_AppP4(Vector3 distance, float displacement)
        {
            if (_currFolderPath == String.Empty)
            {
                throw new Exception("Folder Path is not set!");
            }

            if (string.IsNullOrEmpty(_distanceCsvPathP4))
            {
                string filename = $"P4_ProprioceptionDistance.csv";
                _distanceCsvPathP4 = Path.Combine(_currFolderPath, filename);

                // Write header if file does not exist
                if (!File.Exists(_distanceCsvPathP4))
                    File.WriteAllText(_distanceCsvPathP4, DISTANCE_HEADER_P4);
            }

            string timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string line = $"{timestamp},{distance.x:F4},{distance.y:F4},{distance.z:F4},{displacement:F4}\n";

            File.AppendAllText(_distanceCsvPathP4, line);
        }
        #endregion

        #region Private Methods
        private void ProcessMetrics()
        {
            if (!_userMeasurementStarted)
                return;

            // Capture current state
            Vector3 pos = _camHMD.position;
            Quaternion rot = _camHMD.rotation;
            string timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); // ISO 8601

            string entry = $"{timestamp},{pos.x:F4},{pos.y:F4},{pos.z:F4},{rot.x:F4},{rot.y:F4},{rot.z:F4},{rot.w:F4}";
            _dataBuffer.Add(entry);

            if (_dataBuffer.Count >= _bufferSizeHMDPosRot)
            {
                // Copy and clear buffer
                List<string> toWrite = new List<string>(_dataBuffer);
                _dataBuffer.Clear();

                UniTaskContext context = new UniTaskContext();

                context.Set(BUFFER_DATA_KEY_HMDPOSROT, toWrite);

                TaskUtilitiesManager.RunTask(WriteDataAsync_HMDPosRot, context);
            }

            if (_userResponseMeasurementStartedP2)
            {
                // Store Response Path Here
                Ray ray = new Ray(_camHMD.position, _camHMD.forward);
                if (_cachedTargetPlaneP2.Raycast(ray, out float enter))
                {
                    Vector3 hit = ray.GetPoint(enter);

                    Vector3 normal = _cachedTargetPlaneP2.normal;
                    Vector3 right = Vector3.Cross(normal, _cachedTargetPlaneUp).normalized;
                    Vector3 up = Vector3.Cross(right, normal).normalized;

                    Vector3 localOffset = hit - _cachedCenter;
                    Vector2 p2 = new Vector2(Vector3.Dot(localOffset, right), Vector3.Dot(localOffset, up));
                    p2 /= _scaleFactor;

                    DateTime ts = DateTime.UtcNow;
                    _responseBuffer.Add((p2, ts));

                    if (_responseBuffer.Count >= _bufferSizeAppP2Response)
                    {
                        FlushP2ResponseBuffer();
                    }
                }
            }

            if (_userResponseMeasurementStartedP3 && _targetReferenceP3 != null)
            {
                Ray ray = new Ray(_camHMD.position, _camHMD.forward);

                if (_cachedTargetPlaneP3.Raycast(ray, out float enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    float distance = Vector3.Distance(hit, _targetReferenceP3.position);
                    distance /= _scaleFactor;
                    DateTime ts = DateTime.UtcNow;

                    _distanceBufferP3.Add((distance, ts));

                    if (_distanceBufferP3.Count >= _bufferSizeAppP3Response)
                    {
                        FlushP3DistanceBuffer();
                    }
                }
            }
        }

        private async UniTask WriteDataAsync_HMDPosRot(CancellationToken token, UniTaskContext context)
        {
            try
            {
                if (context.TryGet<List<string>>(BUFFER_DATA_KEY_HMDPOSROT, out var entries))
                {
                    var sb = new StringBuilder();
                    foreach (var line in entries)
                        sb.AppendLine(line);

                    using (StreamWriter writer = new StreamWriter(_hmdPosRotCsvPath, append: true))
                    {
                        await writer.WriteAsync(sb.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhysioData] Error writing to file: {ex.Message}");
            }
        }

        private void FlushP2ResponseBuffer()
        {
            var copy = new List<(Vector2, DateTime)>(_responseBuffer);
            _responseBuffer.Clear();

            UniTaskContext ctx = new UniTaskContext();
            ctx.Set(BUFFER_DATA_KEY_P2RESPONSE, copy);

            TaskUtilitiesManager.RunTask(FlushP2ResponseBuffer_Async, ctx);
        }
        
        private async UniTask FlushP2ResponseBuffer_Async(CancellationToken token, UniTaskContext context)
        {
            if (context.TryGet<List<(Vector2, DateTime)>>(BUFFER_DATA_KEY_P2RESPONSE, out var list))
            {
                var sb = new StringBuilder();
                foreach (var (pt, ts) in list)
                    sb.AppendLine($"{ts:o},{pt.x:F4},{pt.y:F4}");

                using (var writer = new StreamWriter(_responsePathP2, append: true))
                {
                    await writer.WriteAsync(sb.ToString());
                }
            }
        }

        private void FlushP3DistanceBuffer()
        {
            if (_distanceBufferP3 == null || _distanceBufferP3.Count == 0)
                return;

            var copy = new List<(float, DateTime)>(_distanceBufferP3);
            _distanceBufferP3.Clear();

            UniTaskContext ctx = new UniTaskContext();
            ctx.Set(BUFFER_DATA_KEY_P3DISTANCE, copy);

            TaskUtilitiesManager.RunTask(FlushP3DistanceBuffer_Async, ctx);
        }

        private async UniTask FlushP3DistanceBuffer_Async(CancellationToken token, UniTaskContext context)
        {
            if (context.TryGet<List<(float, DateTime)>>(BUFFER_DATA_KEY_P3DISTANCE, out var list))
            {
                var sb = new StringBuilder();
                foreach (var (dist, ts) in list)
                {
                    sb.AppendLine($"{ts:o},{dist:F4}");
                }

                using (var writer = new StreamWriter(_distanceCsvPathP3, append: true))
                {
                    await writer.WriteAsync(sb.ToString());
                }
            }
        }
        #endregion

        #region Event Listeners
        private void OnTargetHit_AppP2()
        {
            if (!_userResponseMeasurementStartedP2)
                return;

            _score += 1;

            FlushP2ResponseBuffer();

            DateTime end = DateTime.UtcNow;
            double duration = (end - _responseStartTime).TotalSeconds;

            // 2D center and target projection
            Vector3 normal = _cachedTargetPlaneP2.normal;
            Vector3 right = Vector3.Cross(normal, _cachedTargetPlaneUp).normalized;
            Vector3 up = Vector3.Cross(right, normal).normalized;

            Vector2 center2D = Vector2.zero;
            Vector3 offT = _cachedTarget - _cachedCenter;
            Vector2 target2D = new Vector2(Vector3.Dot(offT, right), Vector3.Dot(offT, up));

            string startTime = _responseStartTime.ToString("o", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.AppendLine($"{_responseIndexP2},{startTime},{duration:F4},{center2D.x:F4},{center2D.y:F4},{target2D.x:F4},{target2D.y:F4}");

            File.AppendAllText(_responseDataP2, sb.ToString());

            _userResponseMeasurementStartedP2 = false;
            _responseIndexP2++;

            if (GameplayHandler.Instance.Phase >= (AppPhase) 1)
                OnScoreUpdated?.Invoke(_score);
        }

        private void SetCurrentMeasurementMode(int currMode)
        {
            switch (currMode)
            {
                case 0:
                    if (_userMeasurementStarted)
                    {
                        _userMeasurementStarted = false;
                        _currFolderPath = string.Empty;
                    }
                    break;
                case 1:
                    if (!_userMeasurementStarted)
                    {
                        _fileTime = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"; // Get the file time again everytime the application starts over

                        string fileName = $"General_HeadPosRot.csv";
                        string folderName = $"Participant_{_fileTime}";
                        _currFolderPath = Path.Combine(_rootFolderPath, folderName);

                        if (!Directory.Exists(_currFolderPath))
                            Directory.CreateDirectory(_currFolderPath);

                        _hmdPosRotCsvPath = Path.Combine(_currFolderPath, fileName);
                        File.WriteAllText(_hmdPosRotCsvPath, HMDPOSROT_HEADER); // Header

                        _userMeasurementStarted = true;
                    }
                    break;
                case 2:
                    break;
                case 3:
                    break;
                case 4:
                    break;
                case 5:
                    break;
                default:
                    _userMeasurementStarted = false;
                    _currFolderPath = string.Empty;
                    break;
            }
        }
        #endregion
    }
}