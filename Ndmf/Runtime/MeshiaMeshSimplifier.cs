using UnityEngine;


namespace Meshia.MeshSimplification.Ndmf
{
    [AddComponentMenu("Meshia Mesh Simplification/Meshia Mesh Simplifier")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public class MeshiaMeshSimplifier : MonoBehaviour
#if ENABLE_VRCHAT_BASE
    , VRC.SDKBase.IEditorOnly
#endif
    {
        public MeshSimplificationTarget target = new()
        {
            Kind = MeshSimplificationTargetKind.RelativeVertexCount,
            Value = 0.5f,
        };
        public MeshSimplifierOptions options = MeshSimplifierOptions.Default;

        void Reset()
        {
            options = MeshSimplifierOptions.AvatarInitial;
            var mesh = RendererUtility.GetMesh(GetComponent<Renderer>());
            target = new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
                Value = mesh != null ? Mathf.Max(1, mesh.GetTriangleCount() / 2) : 70000,
            };
        }

        void Start() { } // To show enabled checkbox in inspector
    }

}
