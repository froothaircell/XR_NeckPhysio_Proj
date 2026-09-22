using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Wave.Native;
using Wave.Essence.Events;
using Wave.Essence.ScenePerception;

namespace GameResources.PerceptionManagement.AnchorManagement
{
    [Serializable]
    public class SpatialAnchorHelper
    {
        private object _enumerationRequestlock = new object();
        private bool _needAnchorEnumeration = false;
        private bool _needCachedAnchorEnumeration = false;
        private bool _needPersistedAnchorEnumeration = false;

        private ulong[] _anchorHandles = null;
        private readonly Dictionary<ulong, GameObject> _dictAnchorObject = new Dictionary<ulong, GameObject>();
        private object _anchorListLock = new object();  // lock for anchorHandles, dictAnchorObject

        private string[] _cachedAnchorNames = null;
        private string[] _persistedAnchorNames = null;

        private MonoBehaviour _context;
        private ScenePerceptionManager _scenePerceptionManager;
        private GameObject _anchorPrefab;

        private object asyncUpdateAnchorLock = new object();

        public enum AnchorMode
        {
            Spatial,
            Cached,
            Persisted
        }

        public AnchorMode anchorMode = AnchorMode.Spatial;

        private WVR_Result wvrResult;

        public void Init(MonoBehaviour p, ScenePerceptionManager scenePerceptionManager, GameObject anchorPrefab)
        {
            _context = p;
            this._scenePerceptionManager = scenePerceptionManager;
            this._anchorPrefab = anchorPrefab;
        }

        public void SetAnchorsShouldBeUpdated()
        {
            Debug.Log($"SetAnchorsShouldBeUpdated m={anchorMode}");
            lock (_enumerationRequestlock)
            {
                _needAnchorEnumeration = true;
                _needCachedAnchorEnumeration = anchorMode == AnchorMode.Cached;
                _needPersistedAnchorEnumeration = anchorMode == AnchorMode.Persisted;
            }
        }

        public void UpdateAnchorLists(bool ns, bool nc, bool np)
        {
            if (!ns && !nc && !np)
                return;
            LogD($"UpdateAnchorLists sp={ns} c={nc} p={np}");
            ulong[] saHandles;
            string[] caNames;
            string[] paNames;

            lock (_anchorListLock)
            {
                saHandles = _anchorHandles;
                caNames = _cachedAnchorNames;
                paNames = _persistedAnchorNames;
            }

            if (_anchorHandles == null || ns)
            {
                wvrResult = _scenePerceptionManager.GetSpatialAnchors(out saHandles);
                if (!ResultProcess("GetSpatialAnchors", false, true)) return;
                LogD("Got " + saHandles.Length + " spatial anchors");
            }

            if (nc)
            {
                wvrResult = _scenePerceptionManager.GetCachedSpatialAnchorNames(out caNames);
                if (!ResultProcess("GetCachedSpatialAnchorNames", false, true)) return;
                LogD("Got " + caNames.Length + " cached anchor names");
            }

            if (np)
            {
                wvrResult = _scenePerceptionManager.GetPersistedSpatialAnchorNames(out paNames);
                if (!ResultProcess("GetPersistedSpatialAnchorNames", false, true)) return;
                LogD("Got " + paNames.Length + " persisted anchor names");
            }

            lock (_anchorListLock)
            {
                _anchorHandles = saHandles;
                _cachedAnchorNames = caNames;
                _persistedAnchorNames = paNames;
            }
        }

        // Not to let UpdateAnchorDictionaryCoroutine run more than once at the same time.
        private bool isUpdateCoroutineRunning = false;

        // Update Anchor's dictionary and tracking state.
        // Use Coroutine to run in main thread to avoid blocking.
        // In the coroutine, use Task.Run to run in another thread.
        // When the task is done, run in main thread to process engine side codes.
        internal void UpdateAnchorDictionary()
        {
            if (!isUpdateCoroutineRunning)
                _context.StartCoroutine(UpdateAnchorDictionaryCoroutine());
        }

        internal void UpdateAnchorsTask(bool ns, bool nc, bool np, WVR_PoseOriginModel wvrOriginModel, Pose trackingOriginPose, out List<Tuple<ulong, SpatialAnchorTrackingState, Pose, string>> stateList)
        {
            Debug.Log("UpdateAnchorsTask");  // Use to check thread id.

            if (ns || nc || np)
                UpdateAnchorLists(ns, nc, np);

            if (_anchorHandles == null)
            {
                stateList = null;
                return;
            }

            stateList = new List<Tuple<ulong, SpatialAnchorTrackingState, Pose, string>>();
            foreach (ulong anchor in _anchorHandles)
            {
                wvrResult = _scenePerceptionManager.GetSpatialAnchorState(anchor, wvrOriginModel, out SpatialAnchorTrackingState trackingState, out Pose pose, out string name, trackingOriginPose);
                if (ResultProcess("GetSpatialAnchorState", false, true))
                    stateList.Add(new Tuple<ulong, SpatialAnchorTrackingState, Pose, string>(anchor, trackingState, pose, name));
            }

            if (anchorMode == AnchorMode.Cached && _cachedAnchorNames != null)
            {
                foreach (var cachedAnchorName in _cachedAnchorNames)
                {
                    if (string.IsNullOrEmpty(cachedAnchorName))
                        continue;
                    // If name in cached not in statList, it means the anchor is not in the scene
                    bool found = false;
                    foreach (var state in stateList)
                    {
                        if (cachedAnchorName.Contains(state.Item4))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        // Remove anchorName's start string "Cached"
                        string spatialAnchorName = cachedAnchorName.Substring(6);
                        wvrResult = _scenePerceptionManager.CreateSpatialAnchorFromCacheName(cachedAnchorName, spatialAnchorName, out ulong _);
                        // Process it in next run.  Let bullet fly.
                        lock (_enumerationRequestlock) _needAnchorEnumeration = ResultProcess("CreateSpatialAnchorFromCacheName", false, true);
                    }
                }
            }

            if (anchorMode == AnchorMode.Persisted && _persistedAnchorNames != null)
            {
                foreach (var persistedAnchorName in _persistedAnchorNames)
                {
                    // If name in persisted not in statList, it means the anchor is not in the scene
                    bool found = false;
                    foreach (var state in stateList)
                    {
                        if (persistedAnchorName.Contains(state.Item4))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        // Remove anchorName's start string "Persisted"
                        string spatialAnchorName = persistedAnchorName.Substring(9);
                        wvrResult = _scenePerceptionManager.CreateSpatialAnchorFromPersistenceName(persistedAnchorName, spatialAnchorName, out ulong _);
                        // Process it in next run.  Let bullet fly.
                        lock (_enumerationRequestlock) _needAnchorEnumeration = wvrResult == WVR_Result.WVR_Success;
                    }
                }
            }

        }

        internal IEnumerator UpdateAnchorDictionaryCoroutine()
        {
            isUpdateCoroutineRunning = true;
            bool ns, nc, np;
            lock (_enumerationRequestlock)
            {
                ns = _needAnchorEnumeration;
                nc = _needCachedAnchorEnumeration;
                np = _needPersistedAnchorEnumeration;

                _needAnchorEnumeration = false;
                _needCachedAnchorEnumeration = false;
                _needPersistedAnchorEnumeration = false;
            }

            // if new task is comming, use lock to wait for the previous task to finish
            lock (asyncUpdateAnchorLock)
            {
                List<Tuple<ulong, SpatialAnchorTrackingState, Pose, string>> stateList = null;
                var wvrOriginModel = ScenePerceptionManager.GetCurrentPoseOriginModel();
                // Get Origin Pose is engine side codes, so it need run in main thread.
                var trackingOriginPose = _scenePerceptionManager.GetTrackingOriginPose();

                // Async run UpdateAnchorLists.  Should not touch an Unity Engine's object in the Task. No error will show.
                var t = Task.Run(() => UpdateAnchorsTask(ns, nc, np, wvrOriginModel, trackingOriginPose, out stateList));

                yield return new WaitUntil(() => t.IsCompleted);

                // code below need run on Unity main thread.

                lock (_anchorListLock)
                {
                    if (stateList != null)
                    {
                        foreach (var state in stateList)
                        {
                            if (Log.gpl.Print)
                                Debug.Log("Anchor " + state.Item1 + " Tracking State: " + state.Item4);
                            if (!_dictAnchorObject.ContainsKey(state.Item1))
                            {
                                // Create Anchor Object
                                GameObject newAnchorGameObject = CreateNewAnchor(state.Item1, state.Item2, state.Item3, state.Item4);
                                if (newAnchorGameObject)
                                    _dictAnchorObject.Add(state.Item1, newAnchorGameObject);
                            }

                            CheckAnchorPose(state.Item1, state.Item2, state.Item3);
                        }
                    }

                    {
                        // Remove objects which are not exist in anchorHandles
                        var anchors = _dictAnchorObject.Keys.ToArray();
                        foreach (var anchor in anchors)
                        {
                            if (Array.IndexOf(_anchorHandles, anchor) < 0)
                            {
                                UnityEngine.Object.Destroy(_dictAnchorObject[anchor]);
                                _dictAnchorObject.Remove(anchor);
                            }
                        }
                    }
                }
            }
            isUpdateCoroutineRunning = false;
        }

        public void HandleAnchorUpdateDestroy(RaycastHit raycastHit)
        {
            Debug.Log("HandleAnchorUpdateDestroy");
            if (raycastHit.collider == null) return;
            var anchorPrefab = raycastHit.collider.transform.GetComponent<AnchorPrefab>();
            if (anchorPrefab == null) return;

            ulong anchor = anchorPrefab.GetAnchorHandle();

            wvrResult = _scenePerceptionManager.GetSpatialAnchorState(
                anchor, ScenePerceptionManager.GetCurrentPoseOriginModel(), out var _, out Pose _, out var anchorName);
            bool hasAnchorState = ResultProcess("GetAnchorState");

            wvrResult = _scenePerceptionManager.DestroySpatialAnchor(anchor);
            if (ResultProcess("DestroySpatialAnchor"))
            {
                lock (_anchorListLock)
                {
                    UnityEngine.Object.Destroy(_dictAnchorObject[anchor]);
                    _dictAnchorObject.Remove(anchor);
                }
                lock (_enumerationRequestlock) _needAnchorEnumeration = true;
            }

            if (anchorMode == AnchorMode.Cached && hasAnchorState)
            {
                var cachedName = "Cached" + anchorName;
                if (IsNameExistInList(_cachedAnchorNames, cachedName))
                {
                    wvrResult = _scenePerceptionManager.UncacheSpatialAnchor(cachedName);
                    lock (_enumerationRequestlock)
                        _needCachedAnchorEnumeration = ResultProcess("UncacheSpatialAnchor");
                }
            }

            if (anchorMode == AnchorMode.Persisted && hasAnchorState)
            {
                var persistedName = "Persisted" + anchorName;
                if (IsNameExistInList(_persistedAnchorNames, persistedName))
                {
                    wvrResult = _scenePerceptionManager.UnpersistSpatialAnchor(persistedName);
                    lock (_enumerationRequestlock)
                        _needPersistedAnchorEnumeration = ResultProcess("UnpersistSpatialAnchor");
                }
            }

            if (_needAnchorEnumeration || _needCachedAnchorEnumeration || _needPersistedAnchorEnumeration)
                UpdateAnchorDictionary();
        }

        private bool IsNameExistInList(string[] anchorNames, string name)
        {
            foreach (var anchorName in anchorNames)
            {
                if (anchorName.Contains(name))
                    return true;
            }
            return false;
        }

        public void HandleAnchorUpdateCreate(RaycastHit raycastHit, Quaternion anchorRotationUnity)
        {
            Debug.Log("HandleAnchorUpdateCreate(raycast)");
            if (raycastHit.collider == null) return;
            if (raycastHit.collider.transform.GetComponent<AnchorPrefab>() != null) return; //Collider hit is not an anchor (Create)
            HandleAnchorUpdateCreate(raycastHit.point, anchorRotationUnity);
        }

        public void HandleAnchorUpdateCreate(Vector3 anchorWorldPositionUnity, Quaternion anchorRotationUnity)
        {
            Debug.Log("HandleAnchorUpdateCreate");
            if (anchorWorldPositionUnity == null) return;
            // Use time to create a unique anchor name.
            // Name must be saveable to file.  Avoid special characters.
            string anchorNameString = "SpatialAnchor_" + DateTime.UtcNow.ToString("HHmmss.fff");
            char[] anchorNameArray = anchorNameString.ToCharArray();

            wvrResult = _scenePerceptionManager.CreateSpatialAnchor(anchorNameArray, anchorWorldPositionUnity, anchorRotationUnity, ScenePerceptionManager.GetCurrentPoseOriginModel(), out ulong newAnchorHandle, true);

            if (ResultProcess("CreateSpatialAnchor"))
            {
                lock (_enumerationRequestlock) _needAnchorEnumeration = true;
                if (anchorMode == AnchorMode.Cached)
                {
                    string cachedAnchorNameString = "Cached" + anchorNameString;
                    wvrResult = _scenePerceptionManager.CacheSpatialAnchor(cachedAnchorNameString, newAnchorHandle);
                    lock (_enumerationRequestlock)
                        _needCachedAnchorEnumeration = ResultProcess("CacheSpatialAnchor") ?
                        true : _needCachedAnchorEnumeration;
                }

                if (anchorMode == AnchorMode.Persisted)
                {
                    wvrResult = _scenePerceptionManager.GetPersistedSpatialAnchorCount(out var getInfo);
                    if (ResultProcess("GetPersistedSpatialAnchorCount"))
                        LogD("PersistAnchor maxTrkCount=" + getInfo.maximumTrackingCount + " curTrkCount=" + getInfo.currentTrackingCount);

                    if (wvrResult == WVR_Result.WVR_Success && getInfo.currentTrackingCount >= getInfo.maximumTrackingCount)
                        LogW("Persisted anchors are too many.");

                    string persistedAnchorNameString = "Persisted" + anchorNameString;
                    wvrResult = _scenePerceptionManager.PersistSpatialAnchor(persistedAnchorNameString, newAnchorHandle);
                    lock (_enumerationRequestlock)
                        _needPersistedAnchorEnumeration = ResultProcess("PersistSpatialAnchor") ?
                            true : _needPersistedAnchorEnumeration;
                }
            }

            if (_needAnchorEnumeration || _needCachedAnchorEnumeration || _needPersistedAnchorEnumeration)
                UpdateAnchorDictionary();
        }

        private GameObject CreateNewAnchor(ulong anchorHandle, SpatialAnchorTrackingState trackingState, Pose pose, string anchorName)
        {
            if (_anchorPrefab == null) return null;
            GameObject newAnchorGameObject = UnityEngine.Object.Instantiate(_anchorPrefab);
            AnchorPrefab newAnchorPrefabInstance = newAnchorGameObject.GetComponent<AnchorPrefab>();

            newAnchorPrefabInstance.SetAnchorHandle(anchorHandle);
            newAnchorPrefabInstance.SetAnchorName(anchorName);
            newAnchorPrefabInstance.SetAnchorState(trackingState, pose);

            SetAnchorPoseInScene(newAnchorPrefabInstance, pose);
            return newAnchorGameObject;
        }

        private void CheckAnchorPose(ulong anchorHandle, SpatialAnchorTrackingState trackingState, Pose pose)
        {
            //Check anchor pose
            GameObject anchorObj;
            lock (_anchorListLock) anchorObj = _dictAnchorObject[anchorHandle];
            if (anchorObj == null) return;

            AnchorPrefab prefab = anchorObj.GetComponent<AnchorPrefab>();
            if (prefab == null)
            {
                LogE("Anchor prefab gameobject deleted but the internals are still hanging out");
                lock (_enumerationRequestlock)
                    _needAnchorEnumeration = true;
                return;
            }

            bool needSetAnchorPose = prefab.GetPose() != pose;
            if (needSetAnchorPose)
                SetAnchorPoseInScene(prefab, pose);
            if (needSetAnchorPose || prefab.GetTrackingState() != trackingState)
                prefab.SetAnchorState(trackingState, pose);
        }

        private void SetAnchorPoseInScene(AnchorPrefab anchorPrefab, Pose pose)
        {
            //scenePerceptionManager.ApplyTrackingOriginCorrectionToAnchorPose(anchorState, out Vector3 currentAnchorPosition, out Quaternion currentAnchorRotation);
            anchorPrefab.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        public void ClearAnchorObjects()
        {
            lock (_anchorListLock)
            {
                foreach (KeyValuePair<ulong, GameObject> anchorPair in _dictAnchorObject)
                {
                    var anchor = anchorPair.Value;
                    if (anchor == null)
                    {
                        LogW("Anchor deleted without being cleaned up in anchor manager");
                        continue;
                    }
                    UnityEngine.Object.Destroy(anchor);
                }

                _dictAnchorObject.Clear();
            }
        }

        bool isClearCoroutineRunning = false;
        public IEnumerator ClearAnchorsCoroutine(bool all)
        {
            isClearCoroutineRunning = true;
            lock (asyncUpdateAnchorLock)
            {
                List<GameObject> objToBeRemoved = new List<GameObject>();
                var t = Task.Run(() =>
                {
                    UpdateAnchorLists(true, false, false);

                    // No, don't do if.  Always clear all spatial anchors.  Because we don't known which one is belong to persist or cached.
                    // if (anchorMode == AnchorMode.Spatial || all)
                    foreach (var anchorHandle in _anchorHandles)
                    {
                        wvrResult = _scenePerceptionManager.DestroySpatialAnchor(anchorHandle);
                        ResultProcess("DestroySpatialAnchor");
                    }
                    lock (_enumerationRequestlock) _needAnchorEnumeration = true;

                    if (anchorMode == AnchorMode.Cached || all)
                    {
                        wvrResult = _scenePerceptionManager.ClearCachedSpatialAnchors();
                        ResultProcess("ClearCachedSpatialAnchors");
                        lock (_enumerationRequestlock) _needCachedAnchorEnumeration = true;
                    }

                    if (anchorMode == AnchorMode.Persisted || all)
                    {
                        wvrResult = _scenePerceptionManager.ClearPersistedSpatialAnchors();
                        ResultProcess("ClearPersistedSpatialAnchors");
                        lock (_enumerationRequestlock) _needPersistedAnchorEnumeration = true;
                    }
                });

                yield return new WaitUntil(() => t.IsCompleted);

                lock (_anchorListLock)
                {
                    foreach (var anchorHandle in _anchorHandles)
                    {
                        if (_dictAnchorObject.ContainsKey(anchorHandle))
                        {
                            UnityEngine.Object.Destroy(_dictAnchorObject[anchorHandle]);
                            _dictAnchorObject.Remove(anchorHandle);
                        }
                    }
                }
            }
            isClearCoroutineRunning = false;
        }

        public void ClearAnchors(bool all = false)
        {
            if (!isClearCoroutineRunning)
                _context.StartCoroutine(ClearAnchorsCoroutine(all));
        }

        public bool IsExporting { get { return isExporting; } }
        bool isExporting = false;

        void ExportPersistAnchorsTask(string persistentDataPath, List<string> anchorNames)
        {
            foreach (var anchorName in anchorNames)
            {
                try
                {
                    WVR_Result wvrResult;
                    byte[] data = null;
                    wvrResult = _scenePerceptionManager.ExportPersistedSpatialAnchor(anchorName, out data);
                    if (wvrResult == WVR_Result.WVR_Success)
                    {
                        LogD("ExportPersistedSpatialAnchor " + anchorName + " successed");
                    }
                    else
                    {
                        LogE("ExportPersistedSpatialAnchor " + anchorName + " failed");
                        continue;
                    }
                    var outputPath = Path.Combine(persistentDataPath, anchorName + ".pa");
                    File.WriteAllBytes(outputPath, data);
                }
                catch (Exception e)
                {
                    LogE("Exception on export: " + anchorName);
                    Debug.LogError(anchorName + " export failed. " + e.Message); continue;
                }
            }
        }

        private IEnumerator ExportPersistAnchorsCoroutine()
        {
            isExporting = true;
            var persistentDataPath = Application.persistentDataPath;  // Cannot call this in Task.
            LogD("Remove old exported anchors...");
            try
            {
                var files = Directory.GetFiles(persistentDataPath, "*.pa");
                foreach (var file in files)
                    File.Delete(file);
            }
            catch (Exception e)
            {
                LogE("Failed to remove old exported anchors.");
                Debug.LogError(e.Message);
            }

            UpdateAnchorLists(false, false, true);

            if (_persistedAnchorNames == null || _persistedAnchorNames.Length == 0)
            {
                LogD("No persisted anchors.  Stop export.");
                isExporting = false;
                yield break;
            }
            LogD(_persistedAnchorNames.Length + " persisted anchors will be Exportd at " + persistentDataPath);

            var t = Task.Run(() => ExportPersistAnchorsTask(persistentDataPath, new List<string>(_persistedAnchorNames)));
            yield return new WaitUntil(() => t.IsCompleted);
            isExporting = false;
        }

        public void ExportPersistAnchors()
        {
            if (isExporting || isImporting)
            {
                LogW("Previous Export is still running, wait for it to finish.");
                return;
            }
            _context.StartCoroutine(ExportPersistAnchorsCoroutine());
        }


        public bool IsImporting { get { return isImporting; } }
        bool isImporting = false;

        void ImportPersistAnchorsTask(List<string> pathNames)
        {
            foreach (var pathName in pathNames)
            {
                string fileName = Path.GetFileName(pathName);
                Debug.Log("ImportPersistedSpatialAnchor from " + fileName);

                try
                {
                    WVR_Result wvrResult;
                    var data = File.ReadAllBytes(pathName);

                    // This might cost a lot of time, so we run it in another thread.
                    wvrResult = _scenePerceptionManager.ImportPersistedSpatialAnchor(data);
                    if (wvrResult == WVR_Result.WVR_Success)
                    {
                        LogD("ImportPersistedSpatialAnchor " + fileName + " successed");
                    }
                    else
                    {
                        LogE("ImportPersistedSpatialAnchor " + fileName + " failed");
                        continue;
                    }
                }
                catch (Exception e)
                {
                    LogE("Exception on import");
                    Debug.LogError(fileName + " import failed. " + e.Message); continue;
                }
                lock (_enumerationRequestlock) _needPersistedAnchorEnumeration = true;
            }
        }

        public IEnumerator ImportPersistAnchorsCoroutine()
        {
            isImporting = true;
            var persistentDataPath = Application.persistentDataPath;  // Cannot call this in Task.

            wvrResult = _scenePerceptionManager.GetPersistedSpatialAnchorCount(out var getInfo);
            if (ResultProcess("GetPersistedSpatialAnchorCount"))
                LogD("PersistAnchor maxTrkCount=" + getInfo.maximumTrackingCount + " curTrkCount=" + getInfo.currentTrackingCount);
            int currentCount = (int)getInfo.currentTrackingCount;

            var files = Directory.GetFiles(persistentDataPath, "*.pa");
            List<string> pathNames = new List<string>();
            foreach (var file in files)
            {
                if (currentCount + 1 >= getInfo.maximumTrackingCount)
                {
                    LogW("PersistAnchor import count exceed maxTrkCount=" + getInfo.maximumTrackingCount + ".  Not add " + Path.GetFileName(file) + " into import lists");
                }
                else
                {
                    LogD("Add " + Path.GetFileName(file) + " into import lists");
                    pathNames.Add(file);
                }
                currentCount++;
            }

            if (pathNames.Count == 0)
            {
                LogD("No exported persisted anchor found in the older.  Before Import, put files here:");
                LogD(persistentDataPath);
                isImporting = false;
                yield break;
            }

            var t = Task.Run(() => ImportPersistAnchorsTask(pathNames));
            yield return new WaitUntil(() => t.IsCompleted);
            isImporting = false;
        }

        public void ImportPersistAnchors()
        {
            if (isImporting || isExporting)
            {
                LogW("Previous Importing / Exporting is still running, wait for it to finish.");
                return;
            }
            _context.StartCoroutine(ImportPersistAnchorsCoroutine());
        }

        bool ResultProcess(string msg, bool successedLog = true, bool failedLog = true)
        {
            if (wvrResult == WVR_Result.WVR_Success)
            {
                if (successedLog)
                    LogD(msg + " successed");
                return true;
            }
            else
            {
                if (failedLog)
                    LogE(msg + " failed");
                return false;
            }
        }

        // LogE is thread safe
        void LogE(string log)
        {
            Debug.LogError(log);
        }

        // LogD is thread safe
        void LogD(string log)
        {
            Debug.Log(log);
        }

        // LogW is thread safe
        void LogW(string log)
        {
            Debug.LogWarning(log);
        }

        internal void OnEnable()
        {
            SystemEvent.Listen(WVR_EventType.WVR_EventType_SpatialAnchor_Changed, OnSpatialAnchorEvent, true);
            SystemEvent.Listen(WVR_EventType.WVR_EventType_CachedSpatialAnchor_Changed, OnSpatialAnchorEvent, true);
            SystemEvent.Listen(WVR_EventType.WVR_EventType_PersistedSpatialAnchor_Changed, OnSpatialAnchorEvent, true);
        }

        internal void OnDisable()
        {
            SystemEvent.Remove(WVR_EventType.WVR_EventType_SpatialAnchor_Changed, OnSpatialAnchorEvent);
            SystemEvent.Remove(WVR_EventType.WVR_EventType_CachedSpatialAnchor_Changed, OnSpatialAnchorEvent);
            SystemEvent.Remove(WVR_EventType.WVR_EventType_PersistedSpatialAnchor_Changed, OnSpatialAnchorEvent);
        }

        void OnSpatialAnchorEvent(WVR_Event_t wvrEvent)
        {
            if (wvrEvent.common.type == WVR_EventType.WVR_EventType_PersistedSpatialAnchor_Changed)
            {
                LogD("Receive Persisted Spatial Anchor Changed event");
                lock (_enumerationRequestlock) _needPersistedAnchorEnumeration = anchorMode == AnchorMode.Persisted;
            }
            else if (wvrEvent.common.type == WVR_EventType.WVR_EventType_CachedSpatialAnchor_Changed)
            {
                LogD("Receive Cached Spatial Anchor Changed event");
                lock (_enumerationRequestlock) _needCachedAnchorEnumeration = anchorMode == AnchorMode.Cached;
            }
            else if (wvrEvent.common.type == WVR_EventType.WVR_EventType_SpatialAnchor_Changed)
            {
                LogD("Receive Spatial Anchor Changed event");
                lock (_enumerationRequestlock) _needAnchorEnumeration = true;
            }
        }
    }
}