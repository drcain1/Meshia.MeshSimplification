#nullable enable
namespace Meshia.MeshSimplification
{
    /// <summary>Controls whether joint protection is selected automatically or explicitly.</summary>
    public enum SkinningProtectionPolicy
    {
        /// <summary>Compatibility value for scenes serialized before policy support; use <see cref="SkinningProtectionOptions.Enabled"/>.</summary>
        Legacy = 0,
        /// <summary>Legacy automatic selection of one anatomical body; retained for saved configurations.</summary>
        Auto = 1,
        On = 2,
        Off = 3,
        /// <summary>Protect each mesh with valid weights using multiple bones, including clothing and custom rigs.</summary>
        AutoDeforming = 4,
    }
}
