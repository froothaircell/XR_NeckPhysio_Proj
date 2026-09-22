using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wave.Essence.ScenePerception;
using Wave.Native;

namespace GameResources.PerceptionManagement.MeshManagement
{
    public class GeneratedMeshContainer : IDisposable
    {
        public virtual void Dispose()
        {
            throw new NotImplementedException();
        }

        public virtual void UpdateAssumingThePerceptionTargetIsCompleted()
        {
            throw new NotImplementedException();
        }
    }

    public class GeneratedSceneMeshContainer : GeneratedMeshContainer
    {
        private readonly List<GeneratedSceneMesh> generatedSceneMeshes = new List<GeneratedSceneMesh>();

        private readonly ScenePerceptionManager scenePerceptionManager;
        private readonly Material generatedMeshMaterialWireframe;

        public WVR_SceneMeshType currentSceneMeshType = WVR_SceneMeshType.WVR_SceneMeshType_VisualMesh;

        private ScenePerceptionHandler context;

        public GeneratedSceneMeshContainer(ScenePerceptionHandler context, ScenePerceptionManager scenePerceptionManager, Material generatedMeshMaterialWireframe)
        {
            this.context = context;
            this.scenePerceptionManager = scenePerceptionManager ?? throw new ArgumentNullException(nameof(scenePerceptionManager));
            this.generatedMeshMaterialWireframe = generatedMeshMaterialWireframe ?? throw new ArgumentNullException(nameof(generatedMeshMaterialWireframe));
        }

        public override void Dispose()
        {
            foreach (var sceneMesh in generatedSceneMeshes)
            {
                sceneMesh.Dispose();
            }
            generatedSceneMeshes.Clear();
        }

        private GeneratedSceneMesh FindGeneratedSceneMesh(ulong meshBufferId)
        {
            foreach (var generatedSceneMesh in generatedSceneMeshes)
            {
                if (generatedSceneMesh.sceneMesh.meshBufferId == meshBufferId)
                {
                    return generatedSceneMesh;
                }
            }
            return null;
        }

        enum SceneMeshAction { NONE, ADD, REMOVE }
        IEnumerable<Tuple<SceneMeshAction, SceneMesh, GeneratedSceneMesh>> SceneMeshActionEnumerator()
        {
            WVR_Result result = scenePerceptionManager.GetSceneMeshes(currentSceneMeshType, out SceneMesh[] currentSceneMeshes);
            if (result != WVR_Result.WVR_Success)
            {
                Debug.LogError("Failed to get scene meshes");
                yield break;
            }

            //Check if generated scene mesh still exsits
            List<int> sceneMeshIndexToRemove = new List<int>();
            for (int i = 0; i < generatedSceneMeshes.Count; i++)
            {
                bool sceneMeshExists = false;
                foreach (SceneMesh sceneMesh in currentSceneMeshes)
                {
                    if (sceneMesh.meshBufferId == generatedSceneMeshes[i].sceneMesh.meshBufferId) //scene mesh still exists
                    {
                        sceneMeshExists = true;
                        break;
                    }
                }

                if (!sceneMeshExists)
                {
                    sceneMeshIndexToRemove.Add(i);
                }
            }

            foreach (int index in sceneMeshIndexToRemove) //Remove all scene meshes that no longer exists
            {
                yield return new Tuple<SceneMeshAction, SceneMesh, GeneratedSceneMesh>(SceneMeshAction.REMOVE, default, generatedSceneMeshes[index]);
            }

            for (var index = 0; index < currentSceneMeshes.Length; index++)
            {
                SceneMesh currentSceneMesh = currentSceneMeshes[index];
                GeneratedSceneMesh generatedSceneMesh = FindGeneratedSceneMesh(currentSceneMesh.meshBufferId);
                if (generatedSceneMesh == null && currentSceneMesh.meshBufferId != 0)
                {
                    yield return new Tuple<SceneMeshAction, SceneMesh, GeneratedSceneMesh>(SceneMeshAction.ADD, currentSceneMesh, null);
                }
                else
                {
                    yield return new Tuple<SceneMeshAction, SceneMesh, GeneratedSceneMesh>(SceneMeshAction.NONE, currentSceneMesh, generatedSceneMesh);
                }
            }
        }

        //only call if scenePerceptionHelper.CurrentPerceptionTargetIsCompleted -- which was perceptionStateDictionary[currentPerceptionTarget] == WVR_ScenePerceptionState.WVR_ScenePerceptionState_Completed
        public override void UpdateAssumingThePerceptionTargetIsCompleted()
        {
            foreach (Tuple<SceneMeshAction, SceneMesh, GeneratedSceneMesh> sceneMeshAction in SceneMeshActionEnumerator())
            {
                SceneMeshAction action = sceneMeshAction.Item1;
                SceneMesh currentSceneMesh = sceneMeshAction.Item2;
                GeneratedSceneMesh generateSceneMesh = sceneMeshAction.Item3;

                switch (action)
                {
                    case SceneMeshAction.ADD:
                        //Log.d(LOG_TAG, "SceneMeshAction.ADD");
                        GeneratedSceneMesh newGeneratedSceneMesh = NewGeneratedSceneMesh(currentSceneMesh);
                        if (newGeneratedSceneMesh != null) generatedSceneMeshes.Add(newGeneratedSceneMesh);
                        break;
                    case SceneMeshAction.REMOVE:
                        //Log.d(LOG_TAG, "SceneMeshAction.REMOVE");
                        generatedSceneMeshes.Remove(generateSceneMesh);
                        generateSceneMesh.Dispose();
                        break;
                    case SceneMeshAction.NONE:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        private GeneratedSceneMesh NewGeneratedSceneMesh(SceneMesh sceneMesh)
        {
            //Log.d(LOG_TAG, "Add new scene mesh");
            GameObject newGeneratedSceneMeshGO = GenerateNewGameObject(sceneMesh);
            if (newGeneratedSceneMeshGO != null)
            {
                GeneratedSceneMesh newGeneratedSceneMesh = new GeneratedSceneMesh() { sceneMesh = sceneMesh, go = newGeneratedSceneMeshGO };
                return newGeneratedSceneMesh;
            }

            return null;

        }
        private GameObject GenerateNewGameObject(SceneMesh sceneMesh)
        {
            //Log.d(LOG_TAG, "Add new scene mesh");
            GameObject newSceneMeshGO = ScenePerceptionObjectTools.GenerateSceneMesh(scenePerceptionManager, sceneMesh, generatedMeshMaterialWireframe, false, context.GetTrackingOrigin());

            if (newSceneMeshGO == null)
                return null;

            //Process Mesh For Wireframe rendering
            MeshFilter generatedSceneMeshFilter = newSceneMeshGO.GetComponent<MeshFilter>();
            if (generatedSceneMeshFilter && generatedSceneMeshFilter.mesh)
            {
                Mesh generatedSceneMeshInstance = generatedSceneMeshFilter.mesh;
                generatedSceneMeshFilter.mesh = ProcessSceneMeshForWireframe(generatedSceneMeshInstance);
            }

            return newSceneMeshGO;
        }

        private Mesh ProcessSceneMeshForWireframe(Mesh generatedSceneMesh)
        {
            int[] originalTriangles = generatedSceneMesh.triangles;
            Vector3[] originalVertices = generatedSceneMesh.vertices;
            Vector3[] originalNormals = generatedSceneMesh.normals;

            Mesh processedMesh = new Mesh();

            if (originalTriangles.Length >= 65535) //Check if processed vertex count is higher than 16 bit limit
            {
                processedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            int[] processedTriangles = new int[originalTriangles.Length];
            Vector3[] processedVertices = new Vector3[originalTriangles.Length];
            Vector3[] processedNormals = new Vector3[originalTriangles.Length];
            Vector2[] processedUVs = new Vector2[originalTriangles.Length];

            for (var i = 0; i < originalTriangles.Length; i += 3)
            {
                processedVertices[i] = originalVertices[originalTriangles[i]];
                processedVertices[i + 1] = originalVertices[originalTriangles[i + 1]];
                processedVertices[i + 2] = originalVertices[originalTriangles[i + 2]];

                processedUVs[i] = new Vector2(0f, 0f);
                processedUVs[i + 1] = new Vector2(1f, 0f);
                processedUVs[i + 2] = new Vector2(0f, 1f);

                processedTriangles[i] = i;
                processedTriangles[i + 1] = i + 1;
                processedTriangles[i + 2] = i + 2;

                processedNormals[i] = originalNormals[originalTriangles[i]];
                processedNormals[i + 1] = originalNormals[originalTriangles[i + 1]];
                processedNormals[i + 2] = originalNormals[originalTriangles[i + 2]];
            }

            processedMesh.vertices = processedVertices;
            processedMesh.triangles = processedTriangles;
            processedMesh.normals = processedNormals;
            processedMesh.uv = processedUVs;

            return processedMesh;
        }
    }

    public class Generated3DObjectContainer : GeneratedMeshContainer
    {
        private readonly List<Generated3DObject> generated3DObjects = new List<Generated3DObject>();

        private readonly ScenePerceptionManager scenePerceptionManager;
        private readonly Material matTranslucent;
        private readonly Material matWireframe;
        private readonly Material matTexture;
        private readonly GameObject anchorDisplayPrefab;

        private ScenePerceptionHandler context;

        public Generated3DObjectContainer(ScenePerceptionHandler context, ScenePerceptionManager scenePerceptionManager, Material matTranslucent, Material matWireframe, Material matTexture, GameObject anchorDisplayPrefab)
        {
            this.context = context;
            this.scenePerceptionManager = scenePerceptionManager ?? throw new ArgumentNullException(nameof(scenePerceptionManager));
            this.anchorDisplayPrefab = anchorDisplayPrefab ?? throw new ArgumentNullException(nameof(anchorDisplayPrefab));
            this.matTranslucent = matTranslucent ?? throw new ArgumentNullException(nameof(matTranslucent));
            this.matWireframe = matWireframe ?? throw new ArgumentNullException(nameof(matWireframe));
            this.matTexture = matTexture ?? throw new ArgumentNullException(nameof(matTexture));

        }
        public override void Dispose()
        {
            foreach (var obj in generated3DObjects)
            {
                obj.Dispose();
            }
            generated3DObjects.Clear();
        }

        private Generated3DObject FindGeneratedObject(WVR_Uuid uuid)
        {
            foreach (var obj in generated3DObjects)
            {
                if (obj.uuid == uuid)
                {
                    return obj;
                }
            }
            return null;
        }

        enum ObjectAction { NONE, ADD, REMOVE, UPDATE_EXTENTS, UPDATE_POSE }

        IEnumerable<Tuple<ObjectAction, SceneObject, Generated3DObject>> ObjectActionEnumerator()
        {
            WVR_Result result = scenePerceptionManager.GetSceneObjects(ScenePerceptionManager.GetTrackingOriginModeFlags(), out SceneObject[] currentSceneObjects);
            if (result != WVR_Result.WVR_Success)
            {
                Debug.LogError("Failed to get scene objects");
                yield break;
            }

            //Check if generated object still exsits
            List<int> objectIndexToRemove = new List<int>();
            for (int i = 0; i < generated3DObjects.Count; i++)
            {
                bool objectExists = false;
                foreach (SceneObject obj in currentSceneObjects)
                {
                    if (generated3DObjects[i].uuid == obj.uuid) //object still exists
                    {
                        objectExists = true;
                        break;
                    }
                }

                if (!objectExists)
                {
                    objectIndexToRemove.Add(i);
                }
            }

            foreach (int index in objectIndexToRemove) //Remove all objects that no longer exists
            {
                yield return new Tuple<ObjectAction, SceneObject, Generated3DObject>(ObjectAction.REMOVE, default, generated3DObjects[index]);
            }

            //Process retrieved scene objects
            for (var index = 0; index < currentSceneObjects.Length; index++)
            {
                SceneObject currentSceneObject = currentSceneObjects[index];
                Generated3DObject generatedObject = FindGeneratedObject(currentSceneObject.uuid);
                if (generatedObject == null)
                {
                    yield return new Tuple<ObjectAction, SceneObject, Generated3DObject>(ObjectAction.ADD, currentSceneObject, null);
                }
                else
                {
                    //if (!ScenePerceptionManager.SceneObjectExtent3DEqual(generatedObject.so, currentSceneObject))

                    if (generatedObject.so.extent != currentSceneObject.extent)
                    {
                        yield return new Tuple<ObjectAction, SceneObject, Generated3DObject>(ObjectAction.UPDATE_EXTENTS, currentSceneObject, generatedObject);
                    }
                    else
                    {
                        //if (!ScenePerceptionManager.SceneObjectPoseEqual(generatedObject.so, currentSceneObject))
                        if (generatedObject.so.pose != currentSceneObject.pose)
                        {
                            yield return new Tuple<ObjectAction, SceneObject, Generated3DObject>(ObjectAction.UPDATE_POSE, currentSceneObject, generatedObject);
                        }
                        else
                        {
                            yield return new Tuple<ObjectAction, SceneObject, Generated3DObject>(ObjectAction.NONE, currentSceneObject, generatedObject);
                        }
                    }
                }
            }
        }

        //only call if scenePerceptionHelper.CurrentPerceptionTargetIsCompleted -- which was perceptionStateDictionary[currentPerceptionTarget] == WVR_ScenePerceptionState.WVR_ScenePerceptionState_Completed
        public override void UpdateAssumingThePerceptionTargetIsCompleted()
        {
            foreach (Tuple<ObjectAction, SceneObject, Generated3DObject> objectAction in ObjectActionEnumerator())
            {
                ObjectAction action = objectAction.Item1;
                SceneObject currentSceneObject = objectAction.Item2;
                Generated3DObject generatedObject = objectAction.Item3;

                switch (action)
                {
                    case ObjectAction.ADD:
                        //Log.d(LOG_TAG, "ObjectAction.ADD");
                        Generated3DObject newGenerated3DObject = NewGenerated3DObjectObject(currentSceneObject.uuid, currentSceneObject);
                        generated3DObjects.Add(newGenerated3DObject);
                        break;
                    case ObjectAction.REMOVE:
                        //Log.d(LOG_TAG, "ObjectAction.REMOVE");
                        generated3DObjects.Remove(generatedObject);
                        generatedObject.Dispose();
                        break;
                    case ObjectAction.UPDATE_EXTENTS:
                        //Log.d(LOG_TAG, "ObjectAction.UPDATE_EXTENTS");
                        generatedObject.so = currentSceneObject;
                        generatedObject.DestroyGameObject();
                        generatedObject.go = GenerateNewGameObject(currentSceneObject);
                        break;
                    case ObjectAction.UPDATE_POSE:
                        //Log.d(LOG_TAG, "ObjectAction.UPDATE_POSE");
                        {
                            var pose = currentSceneObject.pose;
                            ScenePerceptionObjectTools.TrackingSpaceToWorldSpace(context.GetTrackingOrigin(), pose.position, pose.rotation, out var pos, out var rot);
                            generatedObject.go.transform.SetPositionAndRotation(pos, rot);
                        }
                        break;
                    case ObjectAction.NONE:
                        //Log.d(LOG_TAG, "ObjectAction.NONE");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private Generated3DObject NewGenerated3DObjectObject(WVR_Uuid uuid, SceneObject obj)
        {
            //Log.d(LOG_TAG, "New Generated3DObject");
            var newObj = GenerateNewGameObject(obj);
            if (newObj == null)
                newObj = new GameObject("Create3DObject Error");
            return new Generated3DObject() { uuid = uuid, so = obj, go = newObj };
        }

        private GameObject GenerateNewGameObject(SceneObject so)
        {
            //Log.d(LOG_TAG, "New GeneratedObject GameObject");

            // According to extent, create a wireframe mesh.  If mesh exist, not to create collider.
            GameObject extentMesh = ScenePerceptionObjectTools.GenerateSceneObjectExtentMesh(so, matTranslucent, false, context.GetTrackingOrigin());
            if (extentMesh == null) return null;
            extentMesh.name = "Extent";

            GameObject mesh = null;
            if (so.meshBufferId != 0)
            {
                // According to meshBufferId, get the mesh data from native
                mesh = ScenePerceptionObjectTools.GenerateSceneObjectMesh(scenePerceptionManager, so, matTexture, true, context.GetTrackingOrigin());
                // It is possible if native only have extent but no mesh.
                if (mesh != null)
                    mesh.name = "Mesh";
            }

            // Create a parent for extent and mesh
            GameObject obj = new GameObject();
            obj.name = "SceneObject" + so.uuid.ToString();
            obj.transform.position = extentMesh.transform.position;
            obj.transform.rotation = extentMesh.transform.rotation;

            // Let extent and mesh be the child.
            extentMesh.transform.SetParent(obj.transform, true);
            if (mesh != null)
                mesh.transform.SetParent(obj.transform, true);

            GameObject axisDisplay = UnityEngine.Object.Instantiate(anchorDisplayPrefab, obj.transform, true);
            axisDisplay.name = "axisDisplay";
            axisDisplay.transform.localPosition = Vector3.zero;
            axisDisplay.transform.localRotation = Quaternion.identity;

            return obj;
        }
    }

    public class GeneratedPlaneContainer : GeneratedMeshContainer
    {
        private readonly List<GeneratedPlane> generatedPlanes = new List<GeneratedPlane>();

        private readonly ScenePerceptionManager scenePerceptionManager;
        private readonly Material generatedMeshMaterialTranslucent;
        private readonly GameObject anchorDisplayPrefab;

        private const string LOG_TAG = "GeneratedPlaneContainer";

        private ScenePerceptionHandler context;

        public GeneratedPlaneContainer(ScenePerceptionHandler context, ScenePerceptionManager scenePerceptionManager, Material generatedMeshMaterialTranslucent, GameObject anchorDisplayPrefab)
        {
            this.context = context;
            this.scenePerceptionManager = scenePerceptionManager ?? throw new ArgumentNullException(nameof(scenePerceptionManager));
            this.generatedMeshMaterialTranslucent = generatedMeshMaterialTranslucent ?? throw new ArgumentNullException(nameof(generatedMeshMaterialTranslucent));
            this.anchorDisplayPrefab = anchorDisplayPrefab ?? throw new ArgumentNullException(nameof(anchorDisplayPrefab));
        }
        public override void Dispose()
        {
            foreach (var plane in generatedPlanes)
            {
                plane.Dispose();
            }
            generatedPlanes.Clear();
        }

        private GeneratedPlane FindGeneratedPlane(WVR_Uuid uuid)
        {
            foreach (var plane in generatedPlanes)
            {
                if (plane.uuid == uuid)
                {
                    return plane;
                }
            }
            return null;
        }

        enum PlaneAction { NONE, ADD, REMOVE, UPDATE_EXTENTS, UPDATE_POSE }
        IEnumerable<Tuple<PlaneAction, ScenePlane, GeneratedPlane>> PlaneActionEnumerator()
        {
            WVR_Result result = scenePerceptionManager.GetScenePlanes(ScenePerceptionManager.GetTrackingOriginModeFlags(), out ScenePlane[] currentScenePlanes);
            if (result != WVR_Result.WVR_Success)
            {
                Log.e(LOG_TAG, "Failed to get scene planes");
                yield break;
            }

            //Check if generated plane still exsits
            List<int> planeIndexToRemove = new List<int>();
            for (int i = 0; i < generatedPlanes.Count; i++)
            {
                bool planeExists = false;
                foreach (ScenePlane plane in currentScenePlanes)
                {
                    if (generatedPlanes[i].uuid == plane.uuid) //plane still exists
                    {
                        planeExists = true;
                        break;
                    }
                }

                if (!planeExists)
                {
                    planeIndexToRemove.Add(i);
                }
            }

            foreach (int index in planeIndexToRemove) //Remove all planes that no longer exists
            {
                yield return new Tuple<PlaneAction, ScenePlane, GeneratedPlane>(PlaneAction.REMOVE, default, generatedPlanes[index]);
            }

            //Process retrieved scene planes
            for (var index = 0; index < currentScenePlanes.Length; index++)
            {
                ScenePlane currentScenePlane = currentScenePlanes[index];
                GeneratedPlane generatedPlane = FindGeneratedPlane(currentScenePlane.uuid);
                if (generatedPlane == null)
                {
                    yield return new Tuple<PlaneAction, ScenePlane, GeneratedPlane>(PlaneAction.ADD, currentScenePlane, null);
                }
                else
                {
                    var newExt = generatedPlane.plane.extent;
                    var currExt = currentScenePlane.extent;

                    if (newExt != currExt)
                    {
                        yield return new Tuple<PlaneAction, ScenePlane, GeneratedPlane>(PlaneAction.UPDATE_EXTENTS, currentScenePlane, generatedPlane);
                    }
                    else
                    {
                        if (generatedPlane.plane.pose != currentScenePlane.pose)
                        {
                            yield return new Tuple<PlaneAction, ScenePlane, GeneratedPlane>(PlaneAction.UPDATE_POSE, currentScenePlane, generatedPlane);
                        }
                        else
                        {
                            yield return new Tuple<PlaneAction, ScenePlane, GeneratedPlane>(PlaneAction.NONE, currentScenePlane, generatedPlane);
                        }
                    }
                }
            }
        }

        //only call if scenePerceptionHelper.CurrentPerceptionTargetIsCompleted -- which was perceptionStateDictionary[currentPerceptionTarget] == WVR_ScenePerceptionState.WVR_ScenePerceptionState_Completed
        public override void UpdateAssumingThePerceptionTargetIsCompleted()
        {
            foreach (Tuple<PlaneAction, ScenePlane, GeneratedPlane> planeAction in PlaneActionEnumerator())
            {
                PlaneAction action = planeAction.Item1;
                ScenePlane currentScenePlane = planeAction.Item2;
                GeneratedPlane generatedPlane = planeAction.Item3;

                switch (action)
                {
                    case PlaneAction.ADD:
                        //Log.d(LOG_TAG, "PlaneAction.ADD");
                        GeneratedPlane newGeneratedPlane = NewGeneratedPlanePlane(currentScenePlane.uuid, currentScenePlane);
                        generatedPlanes.Add(newGeneratedPlane);
                        break;
                    case PlaneAction.REMOVE:
                        //Log.d(LOG_TAG, "PlaneAction.REMOVE");
                        generatedPlanes.Remove(generatedPlane);
                        generatedPlane.Dispose();
                        break;
                    case PlaneAction.UPDATE_EXTENTS:
                        //Log.d(LOG_TAG, "PlaneAction.UPDATE_EXTENTS");
                        generatedPlane.plane = currentScenePlane;
                        generatedPlane.DestroyGameObject();
                        generatedPlane.go = GenerateNewGameObject(currentScenePlane);
                        break;
                    case PlaneAction.UPDATE_POSE:
                        //Log.d(LOG_TAG, "PlaneAction.UPDATE_POSE");
                        {
                            var pose = currentScenePlane.pose;
                            ScenePerceptionObjectTools.TrackingSpaceToWorldSpace(context.GetTrackingOrigin(), pose.position, pose.rotation, out var pos, out var rot);
                            generatedPlane.go.transform.SetPositionAndRotation(pos, rot);
                        }
                        break;
                    case PlaneAction.NONE:
                        //Log.d(LOG_TAG, "PlaneAction.NONE");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private GeneratedPlane NewGeneratedPlanePlane(WVR_Uuid uuid, ScenePlane plane)
        {
            //Log.d(LOG_TAG, "New GeneratedPlane");
            return new GeneratedPlane() { uuid = uuid, plane = plane, go = GenerateNewGameObject(plane) };
        }

        private GameObject GenerateNewGameObject(ScenePlane plane)
        {
            //Log.d(LOG_TAG, "New GeneratedPlane GameObject");
            GameObject newPlaneMeshGO = ScenePerceptionObjectTools.GenerateScenePlaneMesh(plane, generatedMeshMaterialTranslucent, true, context.GetTrackingOrigin());
            newPlaneMeshGO.name = "ScenePlane_" + plane.uuid.ToString();
            GameObject axisDisplay = UnityEngine.Object.Instantiate(anchorDisplayPrefab, newPlaneMeshGO.transform, true);
            axisDisplay.transform.localPosition = Vector3.zero;
            axisDisplay.transform.localRotation = Quaternion.identity;

            return newPlaneMeshGO;
        }
    }
}