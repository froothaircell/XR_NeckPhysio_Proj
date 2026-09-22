using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wave.Essence.ScenePerception;

namespace GameResources.PerceptionManagement.MeshManagement
{
    public class GeneratedSceneMesh : IDisposable
    {
        public SceneMesh sceneMesh;
        public GameObject go;

        public void DestroyGameObject()
        {
            if (go == null) return;

            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh)
            {
                UnityEngine.Object.Destroy(meshFilter.sharedMesh);
            }

            UnityEngine.Object.Destroy(go);
            go = null;
        }


        public void Dispose()
        {
            DestroyGameObject();
        }
    }
}