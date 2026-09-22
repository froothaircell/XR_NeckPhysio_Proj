using System;
using UnityEngine;

namespace Wave.Essence.ScenePerception.Sample
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
