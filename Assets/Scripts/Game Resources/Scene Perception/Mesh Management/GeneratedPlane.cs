using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Wave.Essence.ScenePerception;
using Wave.Native;

namespace GameResources.PerceptionManagement.MeshManagement
{
    public class GeneratedPlane : IDisposable
    {
        public WVR_Uuid uuid;
        public ScenePlane plane;
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