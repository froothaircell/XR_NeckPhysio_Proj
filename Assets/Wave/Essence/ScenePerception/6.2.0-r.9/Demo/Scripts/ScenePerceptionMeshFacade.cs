using System;
using UnityEngine;
using Wave.Native;

namespace Wave.Essence.ScenePerception.Sample
{
	public class ScenePerceptionMeshFacade
	{
		private readonly ScenePerceptionHelper scenePerceptionHelper;
		private readonly GeneratedPlaneContainer generatedPlaneContainer;
		private readonly Generated3DObjectContainer generated3DObjectContainer;
		private readonly GeneratedSceneMeshContainer generatedSceneMeshContainer;

		private const string LOG_TAG = "ScenePerceptionMeshFacade";
		private ScenePerceptionDemo context;

		public ScenePerceptionMeshFacade(ScenePerceptionDemo context, ScenePerceptionHelper scenePerceptionHelper, GameObject anchorDisplayPrefab, Material matTranslucent, Material matWireframe, Material matTexture)
		{
			this.context = context;
			var manager = scenePerceptionHelper.scenePerceptionManager;
			this.scenePerceptionHelper = scenePerceptionHelper ?? throw new ArgumentNullException(nameof(scenePerceptionHelper));
			if (matTranslucent == null) throw new ArgumentNullException(nameof(matTranslucent));
			generatedPlaneContainer = new GeneratedPlaneContainer(context, manager, matTranslucent, anchorDisplayPrefab);
			generated3DObjectContainer = new Generated3DObjectContainer(context, manager, matTranslucent, matWireframe, matTexture, anchorDisplayPrefab);
			generatedSceneMeshContainer = new GeneratedSceneMeshContainer(context, manager, matWireframe);
		}

		void UpdateScenePerceptionMesh(ScenePerceptionHelper.SceneTarget target, GeneratedMeshContainer containner)
		{
			var state = scenePerceptionHelper.GetState(target);
			if (state != ScenePerceptionHelper.ScenePerceptionState.Completed)
			{
				if (state == ScenePerceptionHelper.ScenePerceptionState.Empty)
					containner.Dispose();
			}
			if (Log.gpl.Print)
				Log.d(LOG_TAG, $"UpdateScenePerceptionMesh: Perception target {target} is {state}.");
			containner.UpdateAssumingThePerceptionTargetIsCompleted();

		}

		public void UpdateScenePerceptionMesh()
		{
			if (scenePerceptionHelper.target2DPlane)
				UpdateScenePerceptionMesh(ScenePerceptionHelper.SceneTarget.TwoDimensionPlane, generatedPlaneContainer);
			if (scenePerceptionHelper.target3DObject)
				UpdateScenePerceptionMesh(ScenePerceptionHelper.SceneTarget.ThreeDimensionObject, generated3DObjectContainer);
			if (scenePerceptionHelper.targetSceneMesh)
				UpdateScenePerceptionMesh(ScenePerceptionHelper.SceneTarget.SceneMesh, generatedSceneMeshContainer);
		}

		public void ChangeSceneMeshType(WVR_SceneMeshType sceneMeshType)
		{
			generatedSceneMeshContainer.currentSceneMeshType = sceneMeshType;
		}

		public void DestroyGeneratedMeshes()
		{
			generatedPlaneContainer.Dispose();
			generated3DObjectContainer.Dispose();
			generatedSceneMeshContainer.Dispose();
		}
	}
}
