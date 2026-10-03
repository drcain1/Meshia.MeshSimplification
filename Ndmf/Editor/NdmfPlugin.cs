#nullable enable
using Meshia.MeshSimplification.Ndmf.Editor.Preview;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[assembly: ExportsPlugin(typeof(Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin))]

namespace Meshia.MeshSimplification.Ndmf.Editor
{
#if ENABLE_NDMF_PLATFORM
    [RunsOnAllPlatforms]
#endif
    internal sealed class NdmfPlugin : Plugin<NdmfPlugin>
    {
        // Analysis subscribes only while building its own transient avatar clone.
        internal static event System.Action<GameObject, Renderer, Mesh, MeshSimplificationTarget,
            MeshSimplifierOptions, BitArray?, Mesh, FaQemCountProfile?>? MeshMeasured;
        private sealed class Work
        {
            internal readonly Renderer Renderer;
            internal readonly Mesh Source;
            internal readonly MeshSimplificationTarget Target;
            internal readonly MeshSimplifierOptions Options;
            internal readonly BitArray? PreserveBones;
            internal readonly Mesh Simplified = new();
            internal bool Retained;
            internal FaQemCountProfile? CountProfile;
            internal Work(Renderer renderer, Mesh source, MeshSimplificationTarget target, MeshSimplifierOptions options, BitArray? preserveBones)
            {
                Renderer = renderer; Source = source; Target = target; Options = options; PreserveBones = preserveBones;
            }
        }

        public override string DisplayName => "Meshia NDMF Mesh Simplifier";
        public override string QualifiedName => "io.github.drcain1.meshia.mesh-simplification";

        protected override void Configure()
        {
#if ENABLE_MODULAR_AVATAR
            InPhase(BuildPhase.Resolving).Run("Resolve References", context =>
            {
                foreach (var component in context.AvatarRootObject.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true)) component.ResolveReferences();
            });
#endif
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Simplify meshes", Simplify)
                .PreviewingWith(new IRenderFilter[]
                {
                    new MeshiaMeshSimplifierPreview(),
#if ENABLE_MODULAR_AVATAR
                    new MeshiaCascadingAvatarMeshSimplifierPreview(),
#endif
                });
        }

        private static void Simplify(BuildContext context)
        {
            using (ListPool<Work>.Get(out var works))
            using (ListPool<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, BitArray? Preserve, Mesh Destination)>.Get(out var batch))
            {
                try
                {
                    var autoProtectedRenderers = SelectAutomaticSkinningProtection(context.AvatarRootObject);
                    foreach (var component in context.AvatarRootObject.GetComponentsInChildren<MeshiaMeshSimplifier>(true))
                    {
                        if (!component.enabled || !component.TryGetComponent<Renderer>(out var renderer)) continue;
                        var source = RendererUtility.GetRequiredMesh(renderer);
                        var options = component.options;
                        options.SkinningProtection = options.SkinningProtection.Resolve(options.SkinningProtection.Policy == SkinningProtectionPolicy.AutoDeforming
                            ? HasDeformingSkinning(renderer as SkinnedMeshRenderer) : autoProtectedRenderers.Contains(renderer));
                        var work = new Work(renderer, source, component.target, options, null);
                        works.Add(work);
                    }
#if ENABLE_MODULAR_AVATAR
                    foreach (var component in context.AvatarRootObject.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true))
                    {
                        foreach (var entry in component.Entries)
                        {
                            if (!entry.IsValid(component) || !entry.Enabled) continue;
                            var renderer = entry.GetTargetRenderer(component)!;
                            var source = RendererUtility.GetRequiredMesh(renderer);
                            var target = entry.CreateTarget(source.GetTriangleCount());
                            var options = MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(context.AvatarRootObject, component, entry);
                            options.SkinningProtection = options.SkinningProtection.Resolve(options.SkinningProtection.Policy == SkinningProtectionPolicy.AutoDeforming
                                ? HasDeformingSkinning(renderer as SkinnedMeshRenderer) : autoProtectedRenderers.Contains(renderer));
                            var work = new Work(renderer, source, target,
                                options,
                                MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(context.AvatarRootObject, component, entry));
                            works.Add(work);
                        }
                    }
#endif
                    foreach (var work in works)
                    {
                        WarnIfSkinningProtectionCannotInspect(work);
                        batch.Add((work.Source, work.Target, work.Options, work.PreserveBones, work.Simplified));
                    }
                    if (batch.Count != 0)
                    {
                        // Only analysis needs the small count trace; normal builds
                        // and previews retain their existing simplification path.
                        var profiles = MeshMeasured == null ? null : new List<FaQemCountProfile?>();
                        MeshSimplifier.SimplifyBatch(batch, profiles);
                        if (profiles != null)
                            for (var i = 0; i < works.Count; i++) works[i].CountProfile = profiles[i];
                    }
                    foreach (var work in works) Commit(context, work);
                }
                finally
                {
                    foreach (var work in works)
                    {
                        if (!work.Retained && work.Simplified != null)
                            UnityEngine.Object.DestroyImmediate(work.Simplified);
                    }
                    // Components are build-only controls. Clean them up even when a
                    // simplification or asset save throws, so a failed build cannot
                    // leave stale controls on the transient avatar clone.
                    foreach (var component in context.AvatarRootObject.GetComponentsInChildren<MeshiaMeshSimplifier>(true))
                        UnityEngine.Object.DestroyImmediate(component);
#if ENABLE_MODULAR_AVATAR
                    foreach (var component in context.AvatarRootObject.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true))
                        UnityEngine.Object.DestroyImmediate(component);
#endif
                }
            }
        }

        internal static MeshSimplifierOptions ResolveOptions(GameObject avatarRoot, Renderer renderer,
            MeshSimplifierOptions options)
        {
            var automatic = options.SkinningProtection.Policy == SkinningProtectionPolicy.AutoDeforming
                ? HasDeformingSkinning(renderer as SkinnedMeshRenderer)
                : options.SkinningProtection.Policy == SkinningProtectionPolicy.Auto && avatarRoot != null &&
                  SelectAutomaticSkinningProtection(avatarRoot).Contains(renderer);
            options.SkinningProtection = options.SkinningProtection.Resolve(automatic);
            return options;
        }

        internal static MeshSimplifierOptions ResolvePreviewOptions(ComputeContext context,
            GameObject avatarRoot, Renderer renderer, MeshSimplifierOptions options)
        {
            var autoSelected = false;
            if (options.SkinningProtection.Policy == SkinningProtectionPolicy.AutoDeforming)
            {
                context.Observe(renderer);
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    context.Observe(skinned, r => r.bones, (a, b) => System.Linq.Enumerable.SequenceEqual(a, b));
                    if (skinned.sharedMesh != null) context.Observe(skinned.sharedMesh);
                    autoSelected = HasDeformingSkinning(skinned);
                }
            }
            else if (options.SkinningProtection.Policy == SkinningProtectionPolicy.Auto && avatarRoot != null)
                autoSelected = SelectAutomaticSkinningProtection(avatarRoot, context).Contains(renderer);
            options.SkinningProtection = options.SkinningProtection.Resolve(autoSelected);
            return options;
        }

        // Bone influences, not renderer names or a unique body classification, determine eligibility.
        internal static bool HasDeformingSkinning(SkinnedMeshRenderer? renderer)
        {
            if (renderer == null || renderer.sharedMesh == null) return false;
            var bones = renderer.bones;
            Transform? first = null;
            using var weights = renderer.sharedMesh.GetAllBoneWeights();
            foreach (var weight in weights)
            {
                if (!(weight.weight > 0f) || float.IsInfinity(weight.weight) ||
                    weight.boneIndex < 0 || weight.boneIndex >= bones.Length) continue;
                var bone = bones[weight.boneIndex];
                if (bone == null) continue;
                if (first == null) first = bone;
                else if (bone != first) return true;
            }
            return false;
        }

        private static HashSet<Renderer> SelectAutomaticSkinningProtection(GameObject avatarRoot,
            ComputeContext? previewContext = null)
        {
            var selected = new HashSet<Renderer>();
            var animator = previewContext != null
                ? previewContext.GetComponent<Animator>(avatarRoot)
                : avatarRoot.GetComponent<Animator>();
            if (animator != null)
            {
                previewContext?.Observe(animator);
                if (animator.avatar != null) previewContext?.Observe(animator.avatar);
            }
            if (animator == null || !animator.isHuman) return selected;

            var humanoidByTransform = new Dictionary<Transform, HumanBodyBones>();
            for (var value = 0; value < (int)HumanBodyBones.LastBone; value++)
            {
                var bone = (HumanBodyBones)value;
                var transform = animator.GetBoneTransform(bone);
                if (transform != null) humanoidByTransform[transform] = bone;
            }

            var configured = new HashSet<SkinnedMeshRenderer>();
            // A configured object's renderer can be added or replaced without
            // changing its simplifier component or serialized options.
            previewContext?.GetComponentsInChildren<Renderer>(avatarRoot, true);
            var directComponents = previewContext != null
                ? previewContext.GetComponentsInChildren<MeshiaMeshSimplifier>(avatarRoot, true)
                : avatarRoot.GetComponentsInChildren<MeshiaMeshSimplifier>(true);
            foreach (var direct in directComponents)
            {
                previewContext?.Observe(direct);
                if (direct.enabled && direct.TryGetComponent<SkinnedMeshRenderer>(out var directRenderer))
                    configured.Add(directRenderer);
            }
#if ENABLE_MODULAR_AVATAR
            var cascadingComponents = previewContext != null
                ? previewContext.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(avatarRoot, true)
                : avatarRoot.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true);
            foreach (var component in cascadingComponents)
            {
                previewContext?.Observe(component);
                foreach (var entry in component.Entries)
                    if (entry.Enabled && entry.GetTargetRenderer(component) is SkinnedMeshRenderer renderer)
                        configured.Add(renderer);
            }
#endif

            var candidates = new List<SkinnedMeshRenderer>();
            foreach (var renderer in configured)
            {
                previewContext?.Observe(renderer);
                if (renderer.sharedMesh == null) continue;
                previewContext?.Observe(renderer.sharedMesh);
                var bones = renderer.bones;
                var mapping = new int[bones.Length];
                for (var i = 0; i < mapping.Length; i++)
                    mapping[i] = bones[i] != null && humanoidByTransform.TryGetValue(bones[i], out var human)
                        ? (int)human
                        : (int)HumanBodyBones.LastBone;
                var weights = renderer.sharedMesh.boneWeights;
                var indices = new int[weights.Length * 4];
                var values = new float[indices.Length];
                for (var i = 0; i < weights.Length; i++)
                {
                    var weight = weights[i];
                    var offset = i * 4;
                    indices[offset] = weight.boneIndex0; values[offset] = weight.weight0;
                    indices[offset + 1] = weight.boneIndex1; values[offset + 1] = weight.weight1;
                    indices[offset + 2] = weight.boneIndex2; values[offset + 2] = weight.weight2;
                    indices[offset + 3] = weight.boneIndex3; values[offset + 3] = weight.weight3;
                }
                if (HumanoidSkinningCoverage.Evaluate(weights.Length, 4, mapping, indices, values).IsAnatomicalBody)
                    candidates.Add(renderer);
            }

            // Multiple fully weighted body-like meshes can include full-body clothing.
            // Fail closed instead of guessing from renderer names or mesh size.
            if (candidates.Count == 1) selected.Add(candidates[0]);
            return selected;
        }

        private static void WarnIfSkinningProtectionCannotInspect(Work work)
        {
            if (!work.Options.SkinningProtection.Enabled || work.Renderer is not SkinnedMeshRenderer) return;
            using var bonesPerVertex = work.Source.GetBonesPerVertex();
            var maximum = 0u;
            foreach (var count in bonesPerVertex) if (count > maximum) maximum = count;
            if (maximum > 32)
                Debug.LogWarning($"Meshia: joint deformation protection is unavailable for renderer '{work.Renderer.name}' because it has vertices with more than 32 bone influences; the configured protection will reject those collapse candidates.", work.Renderer);
        }

        private static void Commit(BuildContext context, Work work)
        {
            MeshMeasured?.Invoke(context.AvatarRootObject, work.Renderer, work.Source,
                work.Target, work.Options, work.PreserveBones, work.Simplified, work.CountProfile);
            if (work.Target.Kind == MeshSimplificationTargetKind.FaQemTriangleCount &&
                work.Simplified.GetTriangleCount() > work.Target.Value + 1)
                Debug.LogWarning(Meshia.MeshSimplification.Editor.Localization.LocalizationProvider.Format(
                    "Meshia: '{0}' retained {1:N0} triangles (requested {2:N0}). Protection and topology constraints take priority over the target. Adjust another mesh or review this mesh's protections; Meshia will not disable guards automatically.",
                    work.Renderer.name, work.Simplified.GetTriangleCount(), work.Target.Value), work.Renderer);
            context.AssetSaver.SaveAsset(work.Simplified);
            if (work.Simplified != work.Source)
            {
                using (new ObjectRegistryScope(context.ObjectRegistry))
                    ObjectRegistry.RegisterReplacedObject(work.Source, work.Simplified);
            }
            RendererUtility.SetMesh(work.Renderer, work.Simplified);
            work.Retained = true;
        }
    }
}
