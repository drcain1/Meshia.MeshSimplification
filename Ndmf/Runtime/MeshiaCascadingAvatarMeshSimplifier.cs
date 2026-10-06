#nullable enable

#if ENABLE_MODULAR_AVATAR

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using UnityEngine.Pool;
using System.Diagnostics.CodeAnalysis;
using System.Collections;
using Unity.Mathematics;
using Unity.Collections;

namespace Meshia.MeshSimplification.Ndmf
{
    /// <summary>
    /// Selects the collapse policy used by the cascading avatar simplifier.
    /// </summary>
    public enum MeshiaCascadingSimplificationAlgorithm
    {
        /// <summary>Use Blender's decimate collapse policy while retaining the allocated triangle count.</summary>
        BlenderDecimate,
        /// <summary>Use Meshia's standard absolute-triangle-count simplifier.</summary>
        Meshia,
        /// <summary>
        /// Reconstruct and dissolve UV-aware quad loops before using Blender Decimate
        /// to reach the allocated triangle count.
        /// </summary>
        UvLoopDissolve,
        /// <summary>
        /// Use FA-QEM geometry simplification with the existing UV layout and materials.
        /// </summary>
        FaQem,
    }

    [AddComponentMenu("Meshia Mesh Simplification/Meshia Cascading Avatar Mesh Simplifier")]
    public class MeshiaCascadingAvatarMeshSimplifier : MonoBehaviour
#if ENABLE_VRCHAT_BASE
    , VRC.SDKBase.IEditorOnly
#endif
    {
        public List<MeshiaCascadingAvatarMeshSimplifierRendererEntry> Entries = new();
        public int TargetTriangleCount = 70000;
        public bool AutoAdjustEnabled = true;
        /// <summary>Signed allowance from complete builds: positive reserves budget, negative restores headroom. Never relaxes geometry protection.</summary>
        [HideInInspector] public int BuildTriangleReserve;


        public void RefreshEntries()
        {
            using (ListPool<Renderer>.Get(out var ownedRenderers))
            {
                GetOwnedRenderers(ownedRenderers);
                var currentEntries = Entries.Select(t => t.GetTargetRenderer(this));
                var addedEntries = ownedRenderers.Except(currentEntries).Where(MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsValidTarget).Select(renderer => new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer!)).ToArray();

                Entries.AddRange(addedEntries);
            }


        }

        private void GetOwnedRenderers(List<Renderer> ownedRenderers)
        {
            var myScopeOrigin = transform.parent;

            if(myScopeOrigin == null)
            {
                throw new InvalidOperationException($"{nameof(MeshiaCascadingAvatarMeshSimplifier)} should not be attached to root GameObject.");
            }
            using (ListPool<MeshiaCascadingAvatarMeshSimplifier>.Get(out var childSimplifiers))
            using (HashSetPool<Transform>.Get(out var otherScopeOrigins))
            {
                myScopeOrigin.gameObject.GetComponentsInChildren(childSimplifiers);
                foreach (var childSimplifier in childSimplifiers)
                {
                    if (childSimplifier != this)
                    {
                        var otherScopeOrigin = childSimplifier.transform.parent;
                        if(otherScopeOrigin == myScopeOrigin)
                        {
                            throw new InvalidOperationException($"Multiple {nameof(MeshiaCascadingAvatarMeshSimplifier)} is attached to direct children of GameObject. This is not allowed.");
                        }
                        otherScopeOrigins.Add(otherScopeOrigin);

                    }
                }

                using (ListPool<Renderer>.Get(out var childRenderers))
                {
                    myScopeOrigin.gameObject.GetComponentsInChildren(childRenderers);
                    for (int i = 0; i < childRenderers.Count;)
                    {
                        Renderer? childRenderer = childRenderers[i];

                        if (childRenderer is MeshRenderer or SkinnedMeshRenderer)
                        {
                            i++;
                        }
                        else
                        {
                            childRenderers[i] = childRenderers[^1];
                            childRenderers.RemoveAt(childRenderers.Count - 1);
                        }
                    }
                    if (otherScopeOrigins.Count != 0)
                    {
                        for (int i = 0; i < childRenderers.Count;)
                        {
                            Renderer? childRenderer = childRenderers[i];
                            if (IsOwnedByThis(childRenderer))
                            {
                                i++;
                            }
                            else
                            {
                                childRenderers[i] = childRenderers[^1];
                                childRenderers.RemoveAt(childRenderers.Count - 1);
                            }
                            bool IsOwnedByThis(Renderer childRenderer)
                            {
                                var currentTransform = childRenderer.transform;
                                while (currentTransform != myScopeOrigin)
                                {
                                    if (otherScopeOrigins.Contains(currentTransform))
                                    {
                                        return false;
                                    }
                                    else
                                    {
                                        currentTransform = currentTransform.parent;
                                    }

                                }
                                return true;
                            }
                        }
                    }
                    ownedRenderers.AddRange(childRenderers);
                }
            }


        }

        public void ResolveReferences()
        {
            foreach (var target in Entries)
            {
                target.ResolveReference(this);
            }
        }
        /// <summary>Resolves the independent humanoid joint selection into this renderer's mesh bone indices.</summary>
        public static MeshSimplifierOptions GetJointProtectionOptions(GameObject avatarRoot,
            MeshiaCascadingAvatarMeshSimplifier component, MeshiaCascadingAvatarMeshSimplifierRendererEntry entry)
        {
            var options = entry.Options;
            if (entry.DisableProtections) return options.WithoutProtections();
            options.SkinningProtection.JointProtectionBoneIndices.Clear();
            if (!options.SkinningProtection.PreserveJointTransitions) return options;
            var animator = avatarRoot != null ? avatarRoot.GetComponent<Animator>() : null;
            var renderer = entry.GetTargetRenderer(component) as SkinnedMeshRenderer;
            if (animator != null && animator.isHuman && renderer != null)
            {
                var bones = renderer.bones;
                for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    if ((entry.PreserveJointTransitionsBones & (1ul << i)) == 0) continue;
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone == null) continue;
                    // A renderer may contain multiple slots for the same transform.
                    for (var slot = 0; slot < bones.Length; slot++)
                        if (bones[slot] == bone && !options.SkinningProtection.JointProtectionBoneIndices.Contains(slot))
                        {
                            if (options.SkinningProtection.JointProtectionBoneIndices.Length == options.SkinningProtection.JointProtectionBoneIndices.Capacity)
                                throw new InvalidOperationException("Too many mesh bone slots selected for joint protection. Select fewer Joint Protection Bones.");
                            options.SkinningProtection.JointProtectionBoneIndices.Add(slot);
                        }
                }
            }
            // An empty humanoid selection must never fall back to protecting every joint.
            if (options.SkinningProtection.JointProtectionBoneIndices.Length == 0)
                options.SkinningProtection.PreserveJointTransitions = false;
            return options;
        }

        public static BitArray? GetPreserveBorderEdgesBoneIndices(GameObject avatarRoot, MeshiaCascadingAvatarMeshSimplifier avatarMeshSimplifier, MeshiaCascadingAvatarMeshSimplifierRendererEntry entry)
        {
            if (entry.DisableProtections) return null;
            if (avatarRoot.TryGetComponent(out Animator avatarAnimator) && entry.GetTargetRenderer(avatarMeshSimplifier) is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                var bones = skinnedMeshRenderer.bones;
                var preserveBorderEdgeBoneIndices = new BitArray(bones.Length);

                for (ulong boneMask = entry.PreserveBorderEdgesBones; boneMask != 0ul; boneMask &= boneMask - 1)
                {
                    var bone = (HumanBodyBones)math.tzcnt(boneMask);
                    var boneTransform = avatarAnimator.GetBoneTransform(bone);
                    if (boneTransform != null)
                    {
                        var boneIndex = Array.IndexOf(bones, boneTransform);
                        if (boneIndex != -1)
                        {
                            preserveBorderEdgeBoneIndices.Set(boneIndex, true);
                        }
                    }
                }
                return preserveBorderEdgeBoneIndices;
            }
            else
            {
                return null;
            }


        }
    }

    [Serializable]
    public record MeshiaCascadingAvatarMeshSimplifierRendererEntry
    {
        public AvatarObjectReference RendererObjectReference;
        public int TargetTriangleCount;
        // Records a successful conversion from allocations made before permanent cut preparation.
        [HideInInspector] public int CutBudgetVersion;
        // Preserve the legacy value when deserializing data without an algorithm field.
        // Newly created entries select FA-QEM in the renderer constructor below.
        public MeshiaCascadingSimplificationAlgorithm Algorithm = MeshiaCascadingSimplificationAlgorithm.BlenderDecimate;
        public MeshSimplifierOptions Options = MeshSimplifierOptions.Default;
        /// <summary>Temporarily bypasses protections without discarding their saved settings.</summary>
        public bool DisableProtections;
        public ulong PreserveBorderEdgesBones = DefaultHandBones;
        public ulong PreserveJointTransitionsBones = DefaultHandBones;
        public const ulong DefaultHandBones =
            (1ul << (int)HumanBodyBones.LeftHand) |
            (1ul << (int)HumanBodyBones.RightHand) |
            (1ul << (int)HumanBodyBones.LeftThumbProximal) |
            (1ul << (int)HumanBodyBones.LeftThumbIntermediate) |
            (1ul << (int)HumanBodyBones.LeftThumbDistal) |
            (1ul << (int)HumanBodyBones.LeftIndexProximal) |
            (1ul << (int)HumanBodyBones.LeftIndexIntermediate) |
            (1ul << (int)HumanBodyBones.LeftIndexDistal) |
            (1ul << (int)HumanBodyBones.LeftMiddleProximal) |
            (1ul << (int)HumanBodyBones.LeftMiddleIntermediate) |
            (1ul << (int)HumanBodyBones.LeftMiddleDistal) |
            (1ul << (int)HumanBodyBones.LeftRingProximal) |
            (1ul << (int)HumanBodyBones.LeftRingIntermediate) |
            (1ul << (int)HumanBodyBones.LeftRingDistal) |
            (1ul << (int)HumanBodyBones.LeftLittleProximal) |
            (1ul << (int)HumanBodyBones.LeftLittleIntermediate) |
            (1ul << (int)HumanBodyBones.LeftLittleDistal) |
            (1ul << (int)HumanBodyBones.RightThumbProximal) |
            (1ul << (int)HumanBodyBones.RightThumbIntermediate) |
            (1ul << (int)HumanBodyBones.RightThumbDistal) |
            (1ul << (int)HumanBodyBones.RightIndexProximal) |
            (1ul << (int)HumanBodyBones.RightIndexIntermediate) |
            (1ul << (int)HumanBodyBones.RightIndexDistal) |
            (1ul << (int)HumanBodyBones.RightMiddleProximal) |
            (1ul << (int)HumanBodyBones.RightMiddleIntermediate) |
            (1ul << (int)HumanBodyBones.RightMiddleDistal) |
            (1ul << (int)HumanBodyBones.RightRingProximal) |
            (1ul << (int)HumanBodyBones.RightRingIntermediate) |
            (1ul << (int)HumanBodyBones.RightRingDistal) |
            (1ul << (int)HumanBodyBones.RightLittleProximal) |
            (1ul << (int)HumanBodyBones.RightLittleIntermediate) |
            (1ul << (int)HumanBodyBones.RightLittleDistal);
        /// <summary>Humanoid limb joints protected on new entries. Unmapped bones are ignored.</summary>
        public const ulong DefaultJointBones = DefaultHandBones |
            (1ul << (int)HumanBodyBones.LeftUpperArm) | (1ul << (int)HumanBodyBones.RightUpperArm) |
            (1ul << (int)HumanBodyBones.LeftLowerArm) | (1ul << (int)HumanBodyBones.RightLowerArm) |
            (1ul << (int)HumanBodyBones.LeftUpperLeg) | (1ul << (int)HumanBodyBones.RightUpperLeg) |
            (1ul << (int)HumanBodyBones.LeftLowerLeg) | (1ul << (int)HumanBodyBones.RightLowerLeg) |
            (1ul << (int)HumanBodyBones.LeftFoot) | (1ul << (int)HumanBodyBones.RightFoot);

        public bool Enabled = true;
        public bool Fixed = false;

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("For serialization only", true)]
#pragma warning disable CS8618
        private MeshiaCascadingAvatarMeshSimplifierRendererEntry()
#pragma warning restore CS8618
        {
        }
        public MeshiaCascadingAvatarMeshSimplifierRendererEntry(Renderer renderer)
        {
            Algorithm = MeshiaCascadingSimplificationAlgorithm.FaQem;
            Enabled = IsEnabledByDefault(renderer);
            RendererObjectReference = new AvatarObjectReference();
            RendererObjectReference.Set(renderer.gameObject);
            TargetTriangleCount = RendererUtility.GetMesh(renderer)?.GetTriangleCount() ?? 0;
            Options = MeshSimplifierOptions.AvatarInitial;
            PreserveJointTransitionsBones = DefaultJointBones;
        }

        // Common avatar convention: Body is the face, while Body_base is the body.
        // This is a conservative name default, not anatomical face detection.
        internal static bool IsEnabledByDefault(Renderer? renderer)
            => renderer != null && !string.Equals(renderer.name, "Body", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Converts this entry's allocated triangle count to its selected simplification target.
        /// </summary>
        public MeshSimplificationTarget CreateTarget(int sourceTriangleCount)
        {
            if (Algorithm == MeshiaCascadingSimplificationAlgorithm.UvLoopDissolve)
            {
                return new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                    Value = TargetTriangleCount,
                };
            }

            if (Algorithm == MeshiaCascadingSimplificationAlgorithm.FaQem)
            {
                return new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
                    Value = TargetTriangleCount,
                };
            }

            if (Algorithm == MeshiaCascadingSimplificationAlgorithm.BlenderDecimate)
            {
                var ratio = sourceTriangleCount > 0 ? TargetTriangleCount / (float)sourceTriangleCount : 1f;
                return new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                    Value = math.saturate(ratio),
                };
            }

            return new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.AbsoluteTriangleCount,
                Value = TargetTriangleCount,
            };
        }

        internal static bool IsValidTarget([NotNullWhen(true)] Renderer? renderer)
        {
            if (renderer == null) return false;
            if (IsEditorOnlyInHierarchy(renderer.gameObject)) return false;
            if (renderer is not SkinnedMeshRenderer and not MeshRenderer) return false;
            var mesh = RendererUtility.GetMesh(renderer);
            if (mesh == null || mesh.GetTriangleCount() == 0) return false;
            return true;
        }

        internal Renderer? GetTargetRenderer(Component container)
        {
            var obj = RendererObjectReference.Get(container);
            if (obj == null) return null;
            return obj.TryGetComponent<Renderer>(out var renderer) && renderer is (MeshRenderer or SkinnedMeshRenderer) ? renderer : null;
        }

        internal bool IsValid(MeshiaCascadingAvatarMeshSimplifier container) => IsValidTarget(GetTargetRenderer(container));

        internal static bool IsEditorOnlyInHierarchy(GameObject gameObject)
        {
            if (gameObject == null) return false;
            Transform current = gameObject.transform;
            while (current != null)
            {
                if (current.CompareTag("EditorOnly"))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        internal void ResolveReference(Component container)
        {
            RendererObjectReference.Get(container);
        }
    }

}

#endif
