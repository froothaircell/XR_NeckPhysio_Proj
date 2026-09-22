using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wave.Essence.ScenePerception;

public class PAAlignWorld : MonoBehaviour
{
	public ScenePerceptionManager scenePerceptionManager;

	public Transform p1WorldRefTranfrom;
	public Transform p1CameraRig;
	public Transform p1PersistAnchorObj;

	public Transform p2WorldRefTranfrom;
	public Transform p2CameraRig;
	public Transform p2PersistAnchorObj;

	bool prepared = false;
	//ulong anchorHandle = 0;

	public void Start()
	{
		StartCoroutine(Prepare());
	}

	public IEnumerator Prepare()
	{
		scenePerceptionManager?.StartScene();

#if UNITY_EDITOR
		if (!Application.isEditor)
#endif
		{
			//// Skip some frames
			//yield return null;
			//yield return null;
			//yield return null;
			//yield return null;
			//string name = "WorldAlignAnchor" + DateTime.Now;
			//if (scenePerceptionManager.CreateSpatialAnchor(name.ToCharArray(), p1PersistAnchorObj.localPosition, p1PersistAnchorObj.localRotation, ScenePerceptionManager.GetCurrentPoseOriginModel(), out anchorHandle, true, false) != WVR_Result.WVR_Success)
			//{
			//	enabled = false;
			//	Debug.LogError("Fail to create spatial anchor");
			//}
		}

		yield return new WaitForSeconds(5);
		prepared = true;
	}

	void Update()
    {
		SimulateWorldAlignment();
	}


	/// <summary>
	/// <para>
	/// This function performs World Alignment for two players in a virtual reality environment, 
	/// using a shared Persist Anchor's pose as a reference point.
	/// The primary objective is to replicate the rigid spatial relationship between the two players and the Persist Anchor in the virtual world,
	/// mirroring their relative positions and orientations in the physical world.
	/// This ensures that the spatial arrangement and orientation of players relative to the Persist Anchor in the real world
	/// are accurately represented in the virtual environment, maintaining a consistent and unified spatial experience.
	/// </para>
	/// 
	/// <para>
	/// The diagram below illustrates the relationship between different points and vectors in the virtual world:
	///   World        Persist
	///   Center        Anchor
	///     W------d------A
	///      \ .         /|
	///       \  r2     / |
	///       r1   .  a1  a2
	///         \    ./   |
	///          \   / .  |
	///           \ /    .|
	///        P1  *      * P2
	///     CameraRig    CameraRig
	/// </para>
	/// 
	/// <para>
	/// Here's a breakdown of the symbols and terms used:
	/// - W represents the world center or reference point in the virtual world.
	/// - A represents the Persist Anchor.  It will be shared to all players.
	/// - d is the tranfrom from the world center W to the Persist Anchor A.
	/// - r1 and r2 represent the transforms from the world center W to the CameraRigs of Player1 (P1) and Player2 (P2), respectively.
	/// - a1 and a2 represent the pose from the Persist Anchor A to the CameraRigs of P1 and P2.
	/// - The dot "." line represent a line of the P2's CameraRigs to the world center.
	/// </para>
	/// 
	/// <para>
	/// Calculating r2 is crucial for aligning the virtual worlds between two players. It ensures that:
	/// - Both players have their CameraRigs positioned relative to the same Persist Anchor, 
	///   allowing for a shared point of reference in the virtual world.
	/// - The relative positions and orientations of the players' CameraRigs are adjusted 
	///   to maintain the perception of being in the same real world space, 
	///   which is essential for collaborative or competitive interactions within the VR environment.
	/// </para>
	/// 
	/// <para>
	/// The process involves using the known transform 'd' of player 1 to calculate
	/// the correct position and orientation for the other player's CameraRig in the virtual world.
	/// This is achieved through matrix transformations and inverse calculations in the script.
	/// </para>
	/// </summary>
	void SimulateWorldAlignment()
	{
		if (!prepared) return;

		var r1 = p1WorldRefTranfrom.localToWorldMatrix.inverse * p1CameraRig.localToWorldMatrix;
		var p1A2RPosition = p1PersistAnchorObj.localPosition;
		var p1A2RRotation = p1PersistAnchorObj.localRotation;

		// Anchor pose should not have a scale other than Vector3.one to camera rig.
		var a1 = Matrix4x4.TRS(p1A2RPosition, p1A2RRotation, Vector3.one);
		var d = r1 * a1;
		var p1A2WPosition = d.GetColumn(3);
		var p1A2WRotation = d.rotation;
		var p1A2WScale = d.lossyScale;
		// Send p1A2WPosition, p1A2WRotation, p1A2WScale and Persisted Anchor exported data to other player

		// In Player 2
		// Import persist anchor PA and create spatial anchor A from PA.

		// You should get the anchor pose by ScenePerceptionManager's API
		var p2A2RPosition = p2PersistAnchorObj.localPosition;
		var p2A2RRotation = p2PersistAnchorObj.localRotation;
		// Calculate new camera rig's pose.
		ScenePerceptionManager.AlignWorld(p1A2WPosition, p1A2WRotation, p1A2WScale, p2A2RPosition, p2A2RRotation, out Vector3 p2R2WPosition, out Quaternion p2R2WRotation, out Vector3 p2R2WScale);
		Matrix4x4 p2R2W = Matrix4x4.TRS(p2R2WPosition, p2R2WRotation, p2R2WScale);
		Matrix4x4 p2LocalToWorld = p2WorldRefTranfrom.localToWorldMatrix * p2R2W;

		p2CameraRig.position = p2LocalToWorld.GetColumn(3);
		p2CameraRig.rotation = p2LocalToWorld.rotation;
	}

	public void OnReloadSceneClicked()
	{
		SceneManager.LoadScene(SceneManager.GetActiveScene().name);
	}
}
