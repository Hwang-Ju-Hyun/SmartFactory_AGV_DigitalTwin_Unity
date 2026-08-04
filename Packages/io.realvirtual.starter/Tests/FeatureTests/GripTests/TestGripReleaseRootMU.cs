// realvirtual.io (formerly game4automation) (R) a Framework for Automation Concept Design, Virtual Commissioning and 3D-HMI
// Copyright(c) 2019 realvirtual GmbH - Usage of this source code only allowed based on License conditions see https://realvirtual.io/unternehmen/lizenz

using UnityEngine;

namespace realvirtual
{
    //! Regression test: an MU created at scene root (Source without Destination) must be released
    //! cleanly on Place() and must NOT stay parented to the gripper.
    //!
    //! Background: MU.InitMu() used to set StandardParent = transform.root.gameObject, which for a
    //! parent-less (root) MU is the MU itself. On release MU.Unfix() then called SetParent(self),
    //! a silent no-op in Unity, leaving the MU stuck on the gripper. This test reproduces exactly
    //! that setup and verifies the MU detaches to scene root after Place().
    public class TestGripReleaseRootMU : FeatureTestBase
    {
        protected override string TestName => "Grip Release Root MU - MU without parent detaches from gripper";

        private MU mu;
        private Transform gripperTransform;

        protected override void SetupTest()
        {
            MinTestTime = 1f;

            // Gripper high in the air — nothing below on rvMU/rvTransport so Place() hits the Auto fallback
            var gripGO = CreateGameObject("TestGrip");
            gripGO.transform.position = TestPosition(5f, 5f, 5f);
            var gripRb = gripGO.AddComponent<Rigidbody>();
            gripRb.isKinematic = true;
            var grip = gripGO.AddComponent<Grip>();
            grip.PlaceMode = PlaceMode.Auto;
            grip.GripRange = 10f;
            gripperTransform = gripGO.transform;

            // MU created like a Source WITHOUT Destination: it lives at scene root (no parent).
            var muGO = CreatePrimitive(PrimitiveType.Cube, "TestRootMU");
            muGO.transform.SetParent(null);                       // <-- parent-less = the bug precondition
            muGO.transform.position = TestPosition(5f, 5f, 5f);
            muGO.transform.localScale = Vector3.one * 0.05f;
            muGO.layer = LayerMask.NameToLayer("rvMU");
            var muRb = muGO.AddComponent<Rigidbody>();
            muRb.isKinematic = false;
            mu = muGO.AddComponent<MU>();

            // Reproduce the real Source code path that assigns StandardParent for a root MU.
            mu.InitMu("TestRootBox", 1, 1);

            // Grip and release
            grip.Fix(mu);
            grip.Place();
        }

        protected override string ValidateResults()
        {
            if (mu == null) return "MU not found";

            // After release the MU must no longer be considered fixed
            if (mu.FixedBy != null)
                return "MU.FixedBy is not null after Place() - MU was not released";

            // The actual bug symptom: MU stays parented to the gripper
            if (mu.transform.parent == gripperTransform)
                return "MU is still parented to the gripper after release (StandardParent self-reference bug)";

            // StandardParent must never point at the MU itself
            if (mu.StandardParent == mu.gameObject)
                return "MU.StandardParent points at the MU itself (self-reference)";

            return "";
        }
    }
}
