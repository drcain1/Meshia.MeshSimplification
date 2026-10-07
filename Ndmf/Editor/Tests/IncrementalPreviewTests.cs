#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meshia.MeshSimplification.Ndmf.Editor.Preview;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class IncrementalPreviewTests
    {
        private sealed class Filter : MeshiaMeshSimplifierPreviewBase<Filter>
        {
            internal readonly Dictionary<Renderer, int> Targets = new();
            internal MeshSimplifierOptions Options = MeshSimplifierOptions.Default;
            public override ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context) => ImmutableList<RenderGroup>.Empty;
            protected override (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(
                ComputeContext context, RenderGroup group, Renderer original, Renderer proxy)
                => (new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
                    Value = Targets[original] }, Options, null);
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Root = new("Preview fixture", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            internal readonly Filter Filter = new();
            internal readonly Renderer[] Renderers;
            internal readonly Mesh Source;
            internal readonly List<IRenderFilterNode> Nodes = new();
            internal IEnumerable<(Renderer, Renderer)> Pairs => Renderers.Select(r => (r, r));
            internal Fixture()
            {
                Source = CutMeshPreparationTests.Grid();
                Renderers = Enumerable.Range(0, 3).Select(i =>
                {
                    var go = new GameObject("Mesh " + i, typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(Root.transform);
                    go.GetComponent<MeshFilter>().sharedMesh = Source;
                    var r = go.GetComponent<Renderer>(); Filter.Targets[r] = 200;
                    return r;
                }).ToArray();
            }
            internal async Task<IRenderFilterNode> First(ComputeContext? context = null)
            {
                var node = await ((IRenderFilter)Filter).Instantiate(RenderGroup.For(Renderers), Pairs,
                    context ?? new ComputeContext("Initial"));
                Nodes.Add(node); return node;
            }
            internal async Task<IRenderFilterNode> Refresh(IRenderFilterNode old, RenderAspects aspects = 0)
            {
                var node = await old.Refresh(Pairs, new ComputeContext("Refresh"), aspects);
                Nodes.Add(node); return node;
            }
            internal Mesh Output(IRenderFilterNode node, Renderer renderer)
            {
                var probe = new GameObject("Output probe", typeof(MeshFilter), typeof(MeshRenderer));
                try { node.OnFrame(renderer, probe.GetComponent<Renderer>()); return probe.GetComponent<MeshFilter>().sharedMesh; }
                finally { Object.DestroyImmediate(probe); }
            }
            public void Dispose()
            {
                foreach (var node in Nodes) node.Dispose();
                Object.DestroyImmediate(Root); Object.DestroyImmediate(Source);
            }
        }

        private static int Recomputed(IRenderFilterNode node) => (int)node.GetType()
            .GetField("RecomputedMeshes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(node);

        [Test]
        public async Task SliderChangeOnlyRebuildsItsMeshAndMatchesFreshResult()
        {
            using var fixture = new Fixture();
            var first = await fixture.First();
            Assert.AreEqual(3, Recomputed(first));
            fixture.Filter.Targets[fixture.Renderers[0]] = 120;
            var changed = await fixture.Refresh(first);
            Assert.AreEqual(1, Recomputed(changed));
            var shared = fixture.Output(changed, fixture.Renderers[1]);
            Assert.AreSame(fixture.Output(first, fixture.Renderers[1]), shared);
            first.Dispose();
            Assert.IsTrue(shared != null, "An old node cannot destroy meshes still used by its successor.");
            var fresh = await fixture.First();
            for (var i = 0; i < 3; i++)
            {
                var expected = fixture.Output(fresh, fixture.Renderers[i]);
                var actual = fixture.Output(changed, fixture.Renderers[i]);
                CollectionAssert.AreEqual(expected.vertices, actual.vertices);
                CollectionAssert.AreEqual(expected.triangles, actual.triangles);
                CollectionAssert.AreEqual(expected.uv, actual.uv);
                CollectionAssert.AreEqual(expected.normals, actual.normals);
            }
            var unchanged = await fixture.Refresh(changed);
            Assert.AreEqual(0, Recomputed(unchanged));
            changed.Dispose(); Assert.IsTrue(shared != null);
            unchanged.Dispose(); Assert.IsTrue(shared == null);
        }

        [Test]
        public async Task ChangingProtectionSettingsReplacesTheReplayPlan()
        {
            using var fixture = new Fixture();
            var first = await fixture.First();
            fixture.Filter.Options.PreserveBorderEdges = false;
            fixture.Filter.Options.FaQem.MaxSurfaceDeviation = .01f;
            var changed = await fixture.Refresh(first);
            Assert.AreEqual(3, Recomputed(changed));
            var expected = new Mesh();
            try
            {
                await MeshSimplifier.SimplifyAsync(fixture.Source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 200
                }, fixture.Filter.Options, expected);
                var actual = fixture.Output(changed, fixture.Renderers[0]);
                CollectionAssert.AreEqual(expected.vertices, actual.vertices);
                CollectionAssert.AreEqual(expected.triangles, actual.triangles);
                CollectionAssert.AreEqual(expected.uv, actual.uv);
            }
            finally { Object.DestroyImmediate(expected); }
        }

        [Test]
        public async Task UpstreamGeometryPoseAndCutInvalidationRebuildPreparedState()
        {
            using var fixture = new Fixture();
            var first = await fixture.First();
            var upstream = await fixture.Refresh(first, RenderAspects.Mesh);
            Assert.AreEqual(3, Recomputed(upstream));
            fixture.Renderers[0].transform.position = Vector3.one;
            var moved = await fixture.Refresh(upstream);
            Assert.AreEqual(3, Recomputed(moved));
            var prepared = moved.GetType().GetField("Inputs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(moved);
            var geometryContext = (ComputeContext)prepared.GetType().GetField("Context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(prepared);
            geometryContext.Invalidate();
            var invalidated = await fixture.Refresh(moved);
            Assert.AreEqual(3, Recomputed(invalidated));
        }

        [Test]
        public async Task ActivityLastsUntilDisplayAndOlderRequestsCannotClearNewerOnes()
        {
            using var fixture = new Fixture();
            var first = await fixture.First();
            Assert.IsTrue(PreviewActivity.IsPending(fixture.Renderers[0]));
            var second = await fixture.Refresh(first);
            first.OnFrameGroup();
            Assert.IsTrue(PreviewActivity.IsPending(fixture.Renderers[0]));
            second.OnFrameGroup();
            Assert.IsFalse(PreviewActivity.IsPending(fixture.Renderers[0]));
        }

        [Test]
        public async Task CancelledRefreshKeepsTheLastCompleteMeshAndAcceptsTheNextEdit()
        {
            using var fixture = new Fixture();
            var first = await fixture.First();
            var original = fixture.Output(first, fixture.Renderers[0]);
            fixture.Filter.Targets[fixture.Renderers[0]] = 100;
            var context = new ComputeContext("Superseded slider edit");
            var pending = first.Refresh(fixture.Pairs, context, 0);
            context.Invalidate();
            ComputeContext.FlushInvalidates();
            var cancelled = await pending;
            fixture.Nodes.Add(cancelled);
            Assert.AreSame(first, cancelled);
            Assert.AreSame(original, fixture.Output(cancelled, fixture.Renderers[0]));
            fixture.Filter.Targets[fixture.Renderers[0]] = 150;
            var latest = await fixture.Refresh(cancelled);
            Assert.AreEqual(1, Recomputed(latest));
            Assert.AreNotSame(original, fixture.Output(latest, fixture.Renderers[0]));
        }

        [Test]
        public async Task InvalidatedRequestCompletesWithoutPublishingPartialWorkOrStallingNdmf()
        {
            using var fixture = new Fixture();
            var context = new ComputeContext("Cancelled request");
            context.Invalidate();
            var node = await fixture.First(context);
            Assert.IsNull(fixture.Output(node, fixture.Renderers[0]));
            Assert.IsFalse(PreviewActivity.IsPending(fixture.Renderers[0]));
            var retried = await fixture.Refresh(node);
            Assert.AreEqual(3, Recomputed(retried));
        }
    }
}
