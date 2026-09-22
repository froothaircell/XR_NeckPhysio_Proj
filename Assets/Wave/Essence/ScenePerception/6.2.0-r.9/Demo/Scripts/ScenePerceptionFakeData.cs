using UnityEngine;
using Wave.Essence.ScenePerception;
using Wave.Essence.ScenePerception.Sample;
using Wave.Native;

public class ScenePerceptionFakeData : MonoBehaviour
{
	public const string TAG = "SPFakeData";

	public ScenePerceptionManager manager;

	public LogPanel logPanel;

	public Transform fakePlane;
	public Transform fakeObject;
	public Transform fakeMesh;

	public bool useFakeDataInApp = false;

	public static ScenePerceptionFakeData Instance { get; private set; }

	private void Start()
	{
		if (!Application.isEditor && !useFakeDataInApp) return;

		if (logPanel) logPanel.AddLog("Start Fake Test");

		while (fakePlane != null)
		{
			var filter = fakePlane.gameObject.GetComponent<MeshFilter>();
			if (filter == null) break;
			var mesh = filter.mesh;
			if (mesh == null) break;

			var fakeObj = new ScenePerceptionManager.ScenePerceptionTestObject();
			fakeObj.mesh = mesh;
			fakeObj.position = fakePlane.position + (fakePlane.rotation * Vector3.Scale(mesh.bounds.center, fakePlane.lossyScale));
			fakeObj.rotation = fakePlane.rotation * Quaternion.Euler(0, 180f, 0);
			fakeObj.scale = fakePlane.lossyScale;
			fakeObj.type = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_2dPlane;

			manager.SetFakeData(fakeObj);
			break;
		}

		while (fakeObject != null)
		{
			var filter = fakeObject.GetComponent<MeshFilter>();
			if (filter == null) break;
			var mesh = filter.mesh;
			if (mesh == null) break;
			if (mesh.isReadable == false) break;

			var fakeObj = new ScenePerceptionManager.ScenePerceptionTestObject();
			fakeObj.mesh = mesh;
			fakeObj.position = fakeObject.position + (fakeObject.rotation * Vector3.Scale(mesh.bounds.center, fakeObject.lossyScale));
			fakeObj.rotation = fakeObject.rotation * Quaternion.Euler(0, 180f, 0);
			fakeObj.scale = fakeObject.lossyScale;
			fakeObj.type = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_3dObject;

			manager.SetFakeData(fakeObj);
			break;
		}

		while (fakeMesh != null)
		{
			var filter = fakeMesh.GetComponent<MeshFilter>();
			if (filter == null) break;
			var mesh = filter.mesh;
			if (mesh == null) break;
			if (mesh.isReadable == false) break;

			var fakeObj = new ScenePerceptionManager.ScenePerceptionTestObject();
			fakeObj.mesh = mesh;
			fakeObj.position = fakeMesh.position;
			fakeObj.rotation = fakeMesh.rotation;
			fakeObj.scale = fakeMesh.lossyScale;
			fakeObj.type = WVR_ScenePerceptionTarget.WVR_ScenePerceptionTarget_SceneMesh;

			manager.SetFakeData(fakeObj);
			break;
		}
	}
}
