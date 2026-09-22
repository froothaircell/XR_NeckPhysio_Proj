using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LineTo : MonoBehaviour
{
	public Transform targetTransform;
	public float lineWidth = 0.02f;
	private LineRenderer lineRenderer;

	void Start()
	{
		lineRenderer = GetComponent<LineRenderer>();
		if (targetTransform == null)
		{
			Debug.LogError("Target Transform is not assigned for LineTo script in " + gameObject.name);
			enabled = false;
			return;
		}

		SetupLineRenderer(lineRenderer, lineWidth);
	}

	void Update()
	{
		DrawLine(lineRenderer, transform, targetTransform);
	}

	static void SetupLineRenderer(LineRenderer lineRenderer, float lineWidth)
	{
		lineRenderer.startWidth = lineWidth;
		lineRenderer.endWidth = lineWidth;
		if (lineRenderer.sharedMaterial == null)
			lineRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
		lineRenderer.alignment = LineAlignment.View;
	}

	static void DrawLine(LineRenderer lineRenderer, Transform sourceTransform, Transform targetTransform)
	{
		if (targetTransform == null)
			return;

		lineRenderer.positionCount = 2;
		lineRenderer.SetPosition(0, sourceTransform.position);
		lineRenderer.SetPosition(1, targetTransform.position);
	}

	private void OnValidate()
	{
		var lineRenderer = GetComponent<LineRenderer>();
		if (lineRenderer == null)
			return;
		SetupLineRenderer(lineRenderer, lineWidth);
		DrawLine(lineRenderer, transform, targetTransform);
	}
}
