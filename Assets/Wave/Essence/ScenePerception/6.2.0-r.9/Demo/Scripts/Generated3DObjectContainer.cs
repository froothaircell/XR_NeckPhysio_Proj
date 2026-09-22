using System;
using System.Collections.Generic;
using UnityEngine;
using Wave.Native;

namespace Wave.Essence.ScenePerception.Sample
{
	public class Generated3DObjectContainer : GeneratedMeshContainer
	{
		private readonly List<Generated3DObject> generated3DObjects = new List<Generated3DObject>();

		private readonly ScenePerceptionManager scenePerceptionManager;
		private readonly Material matTranslucent;
		private readonly Material matWireframe;
		private readonly Material matTexture;
		private readonly GameObject anchorDisplayPrefab;

		private const string TAG = "GeneratedObjectContainer";

		private ScenePerceptionDemo context;

		public Generated3DObjectContainer(ScenePerceptionDemo context, ScenePerceptionManager scenePerceptionManager, Material matTranslucent, Material matWireframe, Material matTexture, GameObject anchorDisplayPrefab)
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

		enum ObjectAction{ NONE, ADD, REMOVE, UPDATE_EXTENTS, UPDATE_POSE }

		IEnumerable<Tuple<ObjectAction, SceneObject, Generated3DObject>> ObjectActionEnumerator()
		{
			WVR_Result result = scenePerceptionManager.GetSceneObjects(ScenePerceptionManager.GetTrackingOriginModeFlags(), out SceneObject[] currentSceneObjects);
			if (result != WVR_Result.WVR_Success)
			{
				Log.e(TAG, "Failed to get scene objects");
				yield break;
			}

			//Check if generated object still exsits
			List<int> objectIndexToRemove = new List<int>();
			for (int i = 0; i < generated3DObjects.Count; i++)
			{
				bool objectExists = false;
				foreach (SceneObject obj in currentSceneObjects)
				{
					if (generated3DObjects[i].uuid ==obj.uuid) //object still exists
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
			return new Generated3DObject() {uuid = uuid, so = obj, go = newObj };
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
}
