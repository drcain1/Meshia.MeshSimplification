using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Meshia.MeshSimplification.Ndmf.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class ModularAvatarCutBoundaryTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void ShouldPreserveBothStatesOfStockMaToggleableCuts(int influences)
        {
            // Exercise MA's installed implementation without a production dependency
            // on its internal types. Meshia itself only consumes the resulting mesh.
            var ma = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                a.GetType("nadena.dev.modular_avatar.core.editor.NaNimationFilter") != null);
            if (ma == null) Assert.Ignore("MA cutter implementation unavailable.");
            const string ns = "nadena.dev.modular_avatar.core.editor.";
            var owner = new GameObject("MA cut fixture");
            var renderer = owner.AddComponent<SkinnedMeshRenderer>();
            var source = CutMeshPreparationTests.Grid();
            Mesh cut = null; var output = new Mesh();
            try
            {
                renderer.sharedMesh = source;
                if (influences == 2)
                {
                    source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                    source.boneWeights = Enumerable.Repeat(new BoneWeight
                        { boneIndex0 = 0, weight0 = .75f, boneIndex1 = 1, weight1 = .25f }, source.vertexCount).ToArray();
                }
                var keyType = ma.GetType(ns + "TargetProp");
                var key = Activator.CreateInstance(keyType);
                keyType.GetField("TargetObject").SetValue(key, renderer);
                keyType.GetField("PropertyName").SetValue(key, "deletedMeshByMask.Cut");
                var selectorType = ma.GetType(ns + "VertexFilterByShape");
                var ctor = selectorType.GetConstructors().Single(c => c.GetParameters()[0].ParameterType == typeof(string));
                var mode = Enum.ToObject(ctor.GetParameters()[2].ParameterType, 0);
                var selector = ctor.Invoke(new[] { (object)"Cut", .001f, mode });
                var tupleType = typeof(ValueTuple<,>).MakeGenericType(keyType, ma.GetType(ns + "IMeshSelector"));
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tupleType));
                list.Add(Activator.CreateInstance(tupleType, key, selector));
                object[] args = { renderer, source, list };
                var plan = (IDictionary)ma.GetType(ns + "NaNimationFilter")
                    .GetMethod("ComputeNaNPlan", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
                cut = (Mesh)args[1];
                Assert.Greater(plan.Count, 0);
                Assert.Greater(cut.bindposeCount, 1);
                var bones = new Transform[cut.bindposeCount]; bones[0] = owner.transform;
                var buffer = new GameObject("NaNimatedBuffer$fixture"); buffer.transform.SetParent(owner.transform);
                for (var i = 1; i < bones.Length; i++)
                {
                    var bone = new GameObject(i < influences ? "Ordinary rig bone" : "NaNimatedBone for fixture" + i);
                    bone.transform.SetParent(i < influences ? owner.transform : buffer.transform); bones[i] = bone.transform;
                }
                renderer.bones = bones;
                var options = VisibilityCutProtection.Resolve(renderer, MeshSimplifierOptions.AvatarInitial);
                MeshSimplifier.Simplify(cut, new MeshSimplificationTarget
                    { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 1 }, options, output);
                Assert.Less(output.triangles.Length, cut.triangles.Length);
                foreach (var hidden in new[] { false, true })
                {
                    var before = Visible(cut, hidden, influences); var after = Visible(output, hidden, influences);
                    try
                    {
                        Assert.That(Area(after), Is.EqualTo(Area(before)).Within(1e-7), "Toggle state changed its visible surface.");
                        CollectionAssert.AreEquivalent(CutMeshPreparationTests.BoundaryPositions(before),
                            CutMeshPreparationTests.BoundaryPositions(after), "Toggle cut rim moved.");
                    }
                    finally { Object.DestroyImmediate(before); Object.DestroyImmediate(after); }
                }
            }
            finally
            {
                Object.DestroyImmediate(output);
                if (cut != null && cut != source) Object.DestroyImmediate(cut);
                Object.DestroyImmediate(source); Object.DestroyImmediate(owner);
            }
        }

        private static Mesh Visible(Mesh source, bool hide, int originalBoneCount)
        {
            var result = Object.Instantiate(source);
            if (!hide) return result;
            var weights = source.boneWeights;
            bool Hidden(int i) => (weights[i].weight0 > 0 && weights[i].boneIndex0 >= originalBoneCount) ||
                (weights[i].weight1 > 0 && weights[i].boneIndex1 >= originalBoneCount) ||
                (weights[i].weight2 > 0 && weights[i].boneIndex2 >= originalBoneCount) ||
                (weights[i].weight3 > 0 && weights[i].boneIndex3 >= originalBoneCount);
            var t = source.triangles; var kept = new List<int>();
            for (var i = 0; i < t.Length; i += 3)
                if (!Hidden(t[i]) && !Hidden(t[i + 1]) && !Hidden(t[i + 2]))
                    kept.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
            result.triangles = kept.ToArray();
            return result;
        }

        private static double Area(Mesh mesh)
        {
            var t = mesh.triangles; var v = mesh.vertices; double area = 0;
            for (var i = 0; i < t.Length; i += 3)
                area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * .5;
            return area;
        }
    }
}
