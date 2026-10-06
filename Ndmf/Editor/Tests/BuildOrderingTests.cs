#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nadena.dev.ndmf;
using NUnit.Framework;

namespace Meshia.MeshSimplification.Ndmf.Editor.Tests
{
    public class BuildOrderingTests
    {
        // These stand-ins are intentionally not exported as installed plugins.
        public class TwistPlugin : Plugin<TwistPlugin>
        {
            public override string QualifiedName => "dev.hai-vr.prefabulous.universal.GenerateTwistBones";
            protected override void Configure()
            {
                InPhase(BuildPhase.Transforming).Run("Earlier twist phase", _ => { });
                InPhase(BuildPhase.Optimizing).Run("Optional late twist", _ => { });
            }
        }

        public class OptimizerPlugin : Plugin<OptimizerPlugin>
        {
            public override string QualifiedName => "com.anatawa12.avatar-optimizer";
            protected override void Configure()
                => InPhase(BuildPhase.Optimizing).Run("Optimizer starts", _ => { });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SimplificationPrecedesOptionalTwistGenerationAndOptimizer(bool withTwistPlugin)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var plugins = new List<Type> { typeof(OptimizerPlugin), typeof(NdmfPlugin) };
            if (withTwistPlugin) plugins.Add(typeof(TwistPlugin));
            // Resolve the actual NDMF graph: an accidental cycle must fail this test.
            var type = typeof(BuildContext).Assembly.GetType("nadena.dev.ndmf.PluginResolver", true)!;
            var constructor = type.GetConstructors(flags).Single(c =>
                c.GetParameters()[0].ParameterType == typeof(IEnumerable<Type>));
            var resolver = constructor.Invoke(new object?[] { plugins, null, false });
            var phases = (IEnumerable)type.GetProperty("Passes", flags)!.GetValue(resolver)!;
            var descriptions = new List<string>();
            foreach (var phase in phases)
            {
                var tupleType = phase.GetType();
                if (!Equals(tupleType.GetField("Item1")!.GetValue(phase), BuildPhase.Optimizing)) continue;
                foreach (var pass in (IEnumerable)tupleType.GetField("Item2")!.GetValue(phase)!)
                    descriptions.Add((string)pass.GetType().GetProperty("Description", flags)!.GetValue(pass)!);
            }
            var meshia = descriptions.IndexOf("Simplify meshes");
            var optimizer = descriptions.IndexOf("Optimizer starts");
            Assert.That(meshia, Is.GreaterThanOrEqualTo(0));
            Assert.That(optimizer, Is.GreaterThan(meshia));
            if (withTwistPlugin)
            {
                var twist = descriptions.IndexOf("Optional late twist");
                Assert.That(twist, Is.GreaterThan(meshia));
                Assert.That(optimizer, Is.GreaterThan(twist));
            }
        }
    }
}
