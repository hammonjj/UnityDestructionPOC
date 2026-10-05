using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DestructionLab.Tests
{
    /// <summary>
    /// Spike S1 (#3): residual ConfigurableJoint hang / arrest / tear under gravity, stepped with
    /// SimulationMode.Script. Numeric pass/fail criteria from the approved plan.
    /// </summary>
    public class ResidualSpikeTests
    {
        WorldFixture f;

        [TearDown]
        public void TearDown() => f?.Dispose();

        static float FloorAngle(Transform t) => Vector3.Angle(t.forward, Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized);

        [UnityTest]
        public IEnumerator S1_Hang_SettlesAtLimit_WithoutDrift_AndForceMatchesWeight()
        {
            f = WorldFixture.Create("hanging");
            f.world.Settings.residual.strengthMultiplier = 1.6f; // holds indefinitely for the stability check
            f.world.RefreshResidualCapacities();
            f.Trigger();
            f.Seconds(1f);
            var c = f.Between("Floor", "Back wall");
            Assert.AreEqual(ConnectionState.Residual, c.state, f.LogText());
            Assert.AreEqual(FailureReason.StructuralOverload, c.reason);
            Assert.IsTrue(c.jointActive, "joint should exist between the floor cluster and the static wall");

            f.Seconds(7f);
            var floor = f.PieceTransform("Floor");
            var rb = f.world.pieces[f.Piece("Floor")].cluster.body;
            var rec = f.world.Joints[c.id];
            Vector3 hingeOnFloor = rec.lastHingeWorld;
            Vector3 hingeOnWall = rec.joint.connectedAnchor; // world space (connected body is static)
            float angle0 = FloorAngle(floor);

            f.Seconds(60f); // plan criterion: no drift over 60 s
            float angle1 = FloorAngle(floor);
            float drift = Vector3.Distance(rec.lastHingeWorld, hingeOnWall);
            float weight = rb.mass * LoadModel.Gravity;
            float force = c.emaForce.magnitude;
            Vector3 com = rb.worldCenterOfMass;
            Vector3 lever = com - rec.lastHingeWorld; lever.y = 0f;
            float analyticMoment = weight * lever.magnitude;
            Debug.Log($"[S1] angle {angle0:0.0}°→{angle1:0.0}°, sag {c.sagDegrees:0.0}, |ω| {rb.angularVelocity.magnitude:0.0000}, drift {drift * 100f:0.00} cm, " +
                      $"force {force / 1000f:0.0} kN vs weight {weight / 1000f:0.0} kN, axis torque {c.emaAxisTorque / 1000f:0.0} kN·m vs analytic {analyticMoment / 1000f:0.0}, q_res {c.qResidual:0.00}");

            Assert.AreEqual(ConnectionState.Residual, c.state, f.LogText());
            Assert.That(Mathf.Abs(angle1 - angle0), Is.LessThan(2f), "angle should be settled");
            Assert.That(rb.angularVelocity.magnitude, Is.LessThan(0.01f));
            Assert.That(drift, Is.LessThan(0.01f));
            Assert.That(force, Is.EqualTo(weight).Within(0.15f * weight));
            Assert.That(c.emaAxisTorque, Is.EqualTo(analyticMoment).Within(0.25f * analyticMoment + 500f), "torque reference point");
            Assert.That(c.qResidual, Is.LessThan(1f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator S2_Arrest_StaysAttached_DamageStopsGrowing()
        {
            f = WorldFixture.Create("arrested");
            f.Trigger();
            f.Seconds(10f);
            var c = f.Between("Floor", "Back wall");
            Assert.AreEqual(ConnectionState.Residual, c.state, f.LogText());
            var floor = f.PieceTransform("Floor");
            float d0 = c.residualDamage;
            float a0 = FloorAngle(floor);
            var rec = f.world.Joints[c.id];
            f.Seconds(20f);
            float drift = Vector3.Distance(rec.lastHingeWorld, rec.joint.connectedAnchor);
            Debug.Log($"[S2] angle {a0:0.0}°→{FloorAngle(floor):0.0}°, D {d0:0.000}→{c.residualDamage:0.000}, q_res {c.qResidual:0.00}, drift {drift * 100f:0.00} cm, sag {c.sagDegrees:0.0}");
            Assert.AreEqual(ConnectionState.Residual, c.state, f.LogText());
            Assert.That(c.residualDamage - d0, Is.LessThan(0.01f));
            Assert.That(drift, Is.LessThan(0.02f));
            Assert.That(FloorAngle(floor), Is.GreaterThan(5f), "floor should have rotated onto the low wall");
            Assert.That(f.world.pieces[f.Piece("Floor")].transform.position.y, Is.GreaterThan(1.2f), "floor rests on the low wall, not the ground");
            yield return null;
        }

        [UnityTest]
        public IEnumerator S3_Tear_UnderOwnHangingLoad_AfterVisibleIntermediateState()
        {
            f = WorldFixture.Create("hanging");
            f.Trigger();
            var c = f.Between("Floor", "Back wall");
            float residualAt = -1f, severedAt = -1f;
            for (int i = 0; i < 1000 && severedAt < 0f; i++)
            {
                f.Steps(1);
                if (residualAt < 0f && c.state == ConnectionState.Residual) residualAt = f.world.SimTime;
                if (c.state == ConnectionState.Severed) severedAt = f.world.SimTime;
            }
            Debug.Log($"[S3] residual at {residualAt:0.00}s, severed at {severedAt:0.00}s, reason {c.reason}\n{f.LogText()}");
            Assert.That(residualAt, Is.GreaterThan(0f));
            Assert.That(severedAt, Is.GreaterThan(0f), "hanging floor should tear under its own load");
            Assert.That(severedAt - residualAt, Is.GreaterThan(3f), "the partially attached state must be visible");
            Assert.AreEqual(FailureReason.ResidualJointFailure, c.reason);
            yield return null;
        }

        [UnityTest]
        public IEnumerator S3b_DirectDamage_TearsResidual()
        {
            f = WorldFixture.Create("hanging");
            f.world.Settings.residual.strengthMultiplier = 1.6f;
            f.world.RefreshResidualCapacities();
            f.Trigger();
            f.Seconds(3f);
            var c = f.Between("Floor", "Back wall");
            Assert.AreEqual(ConnectionState.Residual, c.state);
            int floor = f.Piece("Floor");
            for (int i = 0; i < 3; i++) f.world.Damage(floor, 0.35f);
            f.Steps(2);
            Assert.AreEqual(ConnectionState.Severed, c.state);
            Assert.AreEqual(FailureReason.DirectDamage, c.reason);
            float y0 = f.PieceTransform("Floor").position.y;
            f.Seconds(1f);
            Assert.That(f.PieceTransform("Floor").position.y, Is.LessThan(y0 - 0.5f), "floor falls after tearing");
            yield return null;
        }

        [UnityTest]
        public IEnumerator JointRecreation_DoesNotReleaseSag()
        {
            // Hang the floor, then force the joint to be recreated mid-hang and check it does not swing further.
            f = WorldFixture.Create("hanging");
            f.world.Settings.residual.strengthMultiplier = 1.6f;
            f.world.RefreshResidualCapacities();
            f.Trigger();
            f.Seconds(8f);
            var floor = f.PieceTransform("Floor");
            float before = FloorAngle(floor);
            f.world.DebugRecreateAllJoints();
            f.Seconds(4f);
            float after = FloorAngle(floor);
            Debug.Log($"[Recreate] angle {before:0.0}° → {after:0.0}°");
            Assert.That(Mathf.Abs(after - before), Is.LessThan(3f));
            yield return null;
        }
    }
}
