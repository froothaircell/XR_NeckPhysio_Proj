using UnityEngine;
using VIVE.OpenXR.Passthrough;
// using VIVE.OpenXR.Samples;

namespace VIVE.OpenXR.CompositionLayer.Samples.Passthrough
{
    public class CameraPassthroughTest : MonoBehaviour
    {
        XrResult ID;
        VIVE.OpenXR.Passthrough.XrPassthroughHTC res;

        // Start is called before the first frame update
        void Start()
        {
            ID = PassthroughAPI.CreatePlanarPassthrough(out res, LayerType.Underlay);
        }

        private void OnDestroy()
        {
            PassthroughAPI.DestroyPassthrough(res);
        }
    }
}