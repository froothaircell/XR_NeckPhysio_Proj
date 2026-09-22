using System;
using UnityEngine;
using Wave.Native;

namespace Wave.Essence.ScenePerception.Sample
{
    public class Generated3DObject : IDisposable
    {
        public WVR_Uuid uuid;
        public SceneObject so;
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
