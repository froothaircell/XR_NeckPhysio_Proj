using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wave.Native;

namespace GameResources.PerceptionManagement.MeshManagement
{
    public class ScenePerceptionMeshFacade
    {
        // private readonly ScenePerceptionHelper scenePerceptionHelper;
        private readonly GeneratedPlaneContainer generatedPlaneContainer;
        private readonly Generated3DObjectContainer generated3DObjectContainer;
        private readonly GeneratedSceneMeshContainer generatedSceneMeshContainer;

        private ScenePerceptionHandler context;

        public ScenePerceptionMeshFacade(ScenePerceptionHandler context, GameObject anchorDisplayPrefab, Material matTranslucent, Material matWireframe, Material matTexture)
        {
            this.context = context;
            var manager = context.PerceptionManager;
            if (matTranslucent == null) throw new ArgumentNullException(nameof(matTranslucent));
            generatedPlaneContainer = new GeneratedPlaneContainer(context, manager, matTranslucent, anchorDisplayPrefab);
            generated3DObjectContainer = new Generated3DObjectContainer(context, manager, matTranslucent, matWireframe, matTexture, anchorDisplayPrefab);
            generatedSceneMeshContainer = new GeneratedSceneMeshContainer(context, manager, matWireframe);
        }

        void UpdateScenePerceptionMesh(SceneTarget target, GeneratedMeshContainer container)
        {
            var state = context.GetState(target);
            if (state != ScenePerceptionState.Completed)
            {
                if (state == ScenePerceptionState.Empty)
                    container.Dispose();
            }
            
            Debug.Log($"UpdateScenePerceptionMesh: Perception target {target} is {state}.");
            container.UpdateAssumingThePerceptionTargetIsCompleted();

        }

        public void UpdateScenePerceptionMesh()
        {
            if (context.Target2DPlane)
                UpdateScenePerceptionMesh(SceneTarget.TwoDimensionPlane, generatedPlaneContainer);
            if (context.Target3DObject)
                UpdateScenePerceptionMesh(SceneTarget.ThreeDimensionObject, generated3DObjectContainer);
            if (context.TargetSceneMesh)
                UpdateScenePerceptionMesh(SceneTarget.SceneMesh, generatedSceneMeshContainer);
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