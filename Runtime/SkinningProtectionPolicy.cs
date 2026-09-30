#nullable enable
namespace Meshia.MeshSimplification
{
    /// <summary>Controls whether joint protection is selected automatically or explicitly.</summary>
    public enum SkinningProtectionPolicy
    {
        /// <summary>Compatibility value for scenes serialized before policy support; use <see cref="SkinningProtectionOptions.Enabled"/>.</summary>
        Legacy = 0,
        Auto = 1,
        On = 2,
        Off = 3,
    }
}
