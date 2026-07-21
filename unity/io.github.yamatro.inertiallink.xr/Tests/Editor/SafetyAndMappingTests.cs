using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;
using YamaTro.InertialLink.Core;
using UnityObject = UnityEngine.Object;

namespace YamaTro.InertialLink.Tests
{
    public sealed class SafetyAndMappingTests
    {
        private sealed class XROrigin : MonoBehaviour { }

        private sealed class QueueSource : IMotionSource
        {
            private readonly Queue<MotionSourceFrame> frames = new Queue<MotionSourceFrame>();
            public bool IsReady { get { return true; } }
            public string Status { get { return "Test"; } }
            public void Enqueue(MotionSourceFrame frame) { frames.Enqueue(frame); }
            public bool TryDequeue(out MotionSourceFrame frame)
            {
                if (frames.Count != 0) { frame = frames.Dequeue(); return true; }
                frame = default(MotionSourceFrame);
                return false;
            }
        }

        private sealed class ThrowingSource : IMotionSource
        {
            public bool IsReady { get { return true; } }
            public string Status { get { return "Test"; } }
            public bool TryDequeue(out MotionSourceFrame frame)
            {
                frame = default(MotionSourceFrame);
                throw new InvalidOperationException("untrusted detail must not be logged");
            }
        }

        [Test]
        public void PlainVisualRootIsSafe()
        {
            var root = new GameObject("VisualContent");
            try { Assert.That(EnvironmentMotionDriver.IsSafeTarget(root.transform), Is.True); }
            finally { UnityObject.DestroyImmediate(root); }
        }

        [Test]
        public void RootContainingCameraIsRejected()
        {
            var root = new GameObject("VisualContent");
            var camera = new GameObject("Camera");
            camera.transform.SetParent(root.transform);
            camera.AddComponent<Camera>();
            try { Assert.That(EnvironmentMotionDriver.IsSafeTarget(root.transform), Is.False); }
            finally { UnityObject.DestroyImmediate(root); }
        }

        [Test]
        public void RootUnderCameraIsRejected()
        {
            var camera = new GameObject("Camera");
            camera.AddComponent<Camera>();
            var root = new GameObject("HeadLockedContent");
            root.transform.SetParent(camera.transform);
            try { Assert.That(EnvironmentMotionDriver.IsSafeTarget(root.transform), Is.False); }
            finally { UnityObject.DestroyImmediate(camera); }
        }

        [Test]
        public void XrOriginInEitherDirectionIsRejected()
        {
            var xrOrigin = new GameObject("XR Origin");
            xrOrigin.AddComponent<XROrigin>();
            var content = new GameObject("Content");
            content.transform.SetParent(xrOrigin.transform);
            try
            {
                Assert.That(EnvironmentMotionDriver.IsSafeTarget(content.transform), Is.False, "ancestor XR Origin");
                Assert.That(EnvironmentMotionDriver.IsSafeTarget(xrOrigin.transform), Is.False, "target XR Origin");
            }
            finally { UnityObject.DestroyImmediate(xrOrigin); }
        }

        [Test]
        public void DriverFailsClosedWhenCameraIsAddedAfterConfiguration()
        {
            var controller = new GameObject("Controller");
            var visualRoot = new GameObject("VisualContent");
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                var driver = controller.AddComponent<EnvironmentMotionDriver>();
                Assert.That(driver.Configure(hub, visualRoot.transform), Is.True);
                visualRoot.transform.localPosition = Vector3.one;
                var camera = new GameObject("Late Camera");
                camera.transform.SetParent(visualRoot.transform);
                camera.AddComponent<Camera>();

                LogAssert.Expect(LogType.Error,
                    "InertialLink stopped EnvironmentMotionDriver because its content hierarchy became unsafe.");
                typeof(EnvironmentMotionDriver).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, null);
                Assert.That(driver.enabled, Is.False);
                Assert.That(visualRoot.transform.localPosition, Is.EqualTo(Vector3.one),
                    "an unsafe hierarchy must not be written even for restoration");
            }
            finally
            {
                UnityObject.DestroyImmediate(controller);
                UnityObject.DestroyImmediate(visualRoot);
            }
        }

        [Test]
        public void InvalidRePairClearsPreviouslyConfiguredKey()
        {
            var receiverObject = new GameObject("Inactive Receiver");
            receiverObject.SetActive(false);
            try
            {
                var receiver = receiverObject.AddComponent<UdpMotionSource>();
                Assert.That(receiver.ConfigurePairingKey("000102030405060708090a0b0c0d0e0f"), Is.True);
                var field = typeof(UdpMotionSource).GetField("pairingKey", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field.GetValue(receiver), Is.Not.Null);
                typeof(UdpMotionSource).GetMethod("EnqueueBounded", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(receiver, new object[] { default(MotionSourceFrame) });
                typeof(UdpMotionSource).GetField("outstandingT0", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(receiver, 123L);
                typeof(UdpMotionSource).GetField("outstandingNonce", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(receiver, 456UL);
                Assert.That(receiver.ConfigurePairingKey("invalid"), Is.False);
                Assert.That(field.GetValue(receiver), Is.Null);
                Assert.That(receiver.Status, Is.EqualTo("Invalid pairing key"));
                MotionSourceFrame discarded;
                Assert.That(receiver.TryDequeue(out discarded), Is.False, "old-key frame queue is empty");
                Assert.That(typeof(UdpMotionSource).GetField("outstandingT0", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(receiver), Is.EqualTo(0L));
                Assert.That(typeof(UdpMotionSource).GetField("outstandingNonce", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(receiver), Is.EqualTo(0UL));
                Assert.That(receiver.IsReady, Is.False);
            }
            finally { UnityObject.DestroyImmediate(receiverObject); }
        }

        [Test]
        public void RuntimeConfigurationValidatesPortAndFailsClosed()
        {
            var receiverObject = new GameObject("Inactive Receiver");
            receiverObject.SetActive(false);
            try
            {
                var receiver = receiverObject.AddComponent<UdpMotionSource>();
                Assert.That(receiver.Configure("000102030405060708090a0b0c0d0e0f", 34567), Is.True);
                Assert.That(receiver.ListenPort, Is.EqualTo(34567));
                receiver.ClearPairingKey();
                Assert.That(receiver.Status, Is.EqualTo("Pairing key required"));
                var key = typeof(UdpMotionSource).GetField("pairingKey", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(key.GetValue(receiver), Is.Null);
                Assert.That(receiver.Configure("000102030405060708090a0b0c0d0e0f", 34567), Is.True);
                Assert.That(receiver.Configure("000102030405060708090a0b0c0d0e0f", 1023), Is.False);
                Assert.That(receiver.Status, Is.EqualTo("Invalid listen port"));
                Assert.That(key.GetValue(receiver), Is.Null);
                Assert.That(receiver.IsReady, Is.False);
            }
            finally { UnityObject.DestroyImmediate(receiverObject); }
        }

        [Test]
        public void HubDisablePublishesNeutralImmediately()
        {
            var controller = new GameObject("Controller");
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                typeof(VehicleMotionHub).GetProperty("Current").SetValue(hub,
                    new VehicleMotionState(Vector3.one, Vector3.one, Vector3.one, Vector3.one,
                        Quaternion.identity, 1f, MotionSafetyState.Active, true, 7, 8, 9, 0x0a));
                var updates = 0;
                hub.MotionUpdated += state => updates++;
                typeof(VehicleMotionHub).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                Assert.That(hub.Current.SafetyWeight, Is.Zero);
                Assert.That(hub.Current.SafetyState, Is.EqualTo(MotionSafetyState.Waiting));
                Assert.That(updates, Is.EqualTo(1));
            }
            finally { UnityObject.DestroyImmediate(controller); }
        }

        [Test]
        public void DriverRestoresOldSafeRootBeforeReconfiguration()
        {
            var controller = new GameObject("Controller");
            var first = new GameObject("First Content");
            var second = new GameObject("Second Content");
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                var driver = controller.AddComponent<EnvironmentMotionDriver>();
                Assert.That(driver.Configure(hub, first.transform), Is.True);
                first.transform.localPosition = Vector3.one;
                Assert.That(driver.Configure(hub, second.transform), Is.True);
                Assert.That(first.transform.localPosition, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                UnityObject.DestroyImmediate(controller);
                UnityObject.DestroyImmediate(first);
                UnityObject.DestroyImmediate(second);
            }
        }

        [Test]
        public void NonActiveSafetyStateIsExactlyNeutral()
        {
            var controller = new GameObject("Controller");
            var visualRoot = new GameObject("VisualContent");
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                var driver = controller.AddComponent<EnvironmentMotionDriver>();
                Assert.That(driver.Configure(hub, visualRoot.transform), Is.True);
                visualRoot.transform.localPosition = Vector3.one;
                visualRoot.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                typeof(EnvironmentMotionDriver).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(driver, null);
                Assert.That(visualRoot.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(visualRoot.transform.localRotation, Is.EqualTo(Quaternion.identity));
            }
            finally
            {
                UnityObject.DestroyImmediate(controller);
                UnityObject.DestroyImmediate(visualRoot);
            }
        }

        [Test]
        public void HubValidatesGenericSourcesAndMasksOptionalFields()
        {
            var controller = new GameObject("Controller");
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                typeof(VehicleMotionHub).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                var source = new QueueSource();
                typeof(VehicleMotionHub).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(hub, source);
                var now = MonotonicClock.NowNanoseconds;
                const uint required = (uint)(SensorStatusBits.GyroscopeValid | SensorStatusBits.LinearAccelerationValid);
                for (var i = 0; i < 3; i++)
                {
                    var eventTime = now - 3000000L + (i * 1000000L);
                    var imu = new ImuPayload(eventTime, new Float3(5, 6, 7), new Float3(0.1f, 0.2f, 0.3f),
                        new Float3(1, 2, 3), new Float3(0.4f, 0.5f, 0.6f), new Float4(0, 0, 0, 1), 1, required);
                    source.Enqueue(new MotionSourceFrame(1, (uint)(i + 1), now, eventTime, true, imu));
                }
                typeof(VehicleMotionHub).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                Assert.That(hub.Current.SafetyWeight, Is.EqualTo(1f));
                Assert.That(hub.Current.StatusBits, Is.EqualTo(required));
                Assert.That(hub.Current.RawAcceleration, Is.EqualTo(Vector3.zero));
                Assert.That(hub.Current.Gravity, Is.EqualTo(Vector3.zero));
                Assert.That(hub.Current.VehicleRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(hub.Current.RawAccelerationValid, Is.False);

                var safety = typeof(VehicleMotionHub).GetField("safety", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(hub);
                var lastAcceptedField = typeof(SafetyGate).GetField("lastAcceptedNanoseconds",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var lastAccepted = lastAcceptedField.GetValue(safety);
                var invalidTime = MonotonicClock.NowNanoseconds;
                var invalid = new ImuPayload(invalidTime, new Float3(float.NaN, 0, 0), Float3.Zero,
                    Float3.Zero, Float3.Zero, new Float4(0, 0, 0, 1), 1, required);
                source.Enqueue(new MotionSourceFrame(1, 4, invalidTime, invalidTime, true, invalid));
                typeof(VehicleMotionHub).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                Assert.That(lastAcceptedField.GetValue(safety), Is.EqualTo(lastAccepted),
                    "invalid custom data cannot refresh liveness");
                Assert.That(hub.Current.RawAcceleration, Is.EqualTo(Vector3.zero));
            }
            finally { UnityObject.DestroyImmediate(controller); }
        }

        [Test]
        public void SourceAndSubscriberExceptionsFailClosed()
        {
            var controller = new GameObject("Controller");
            Action<VehicleMotionState> throwing = state => throw new ApplicationException("secret detail");
            var laterCalls = 0;
            Action<VehicleMotionState> later = state => laterCalls++;
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                typeof(VehicleMotionHub).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                typeof(VehicleMotionHub).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(hub, new ThrowingSource());
                LogAssert.Expect(LogType.Error, "InertialLink source failed closed: InvalidOperationException");
                typeof(VehicleMotionHub).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                Assert.That(hub.Current.SafetyWeight, Is.Zero);
                Assert.That(hub.Current.SafetyState, Is.EqualTo(MotionSafetyState.Waiting));

                hub.MotionUpdated += throwing;
                hub.MotionUpdated += later;
                LogAssert.Expect(LogType.Error, "InertialLink MotionUpdated subscriber failed: ApplicationException");
                typeof(VehicleMotionHub).GetMethod("PublishState", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hub, null);
                Assert.That(laterCalls, Is.EqualTo(1), "one subscriber cannot block later subscribers");
                hub.MotionUpdated -= throwing;
                hub.MotionUpdated -= later;
            }
            finally { UnityObject.DestroyImmediate(controller); }
        }

        [Test]
        public void OpenXrVectorsMapToUnityHandedness()
        {
            Assert.That(CoordinateMapping.PolarVectorToUnity(new Float3(1, 2, 3)), Is.EqualTo(new Vector3(1, 2, -3)));
            Assert.That(CoordinateMapping.AngularVelocityToUnity(new Float3(1, 2, 3)), Is.EqualTo(new Vector3(-1, -2, 3)));
            var rotation = CoordinateMapping.RotationToUnity(new Float4(0.1f, 0.2f, 0.3f, 0.9f));
            Assert.That(rotation, Is.EqualTo(new Quaternion(-0.1f, -0.2f, 0.3f, 0.9f)));
        }

        [Test]
        public void AlignmentMonitorReportsBoundedCorrectionWithoutApplyingIt()
        {
            var snapshot = MotionAlignmentMonitor.Evaluate(new Vector3(2f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f), true, 0.35f, 0.5f, 0.6f);
            Assert.That(snapshot.Available, Is.True);
            Assert.That(snapshot.Mismatch, Is.EqualTo(new Vector3(1.5f, 0f, 0f)));
            Assert.That(snapshot.MismatchMagnitude, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(snapshot.SuggestedVirtualCorrection, Is.EqualTo(new Vector3(0.6f, 0f, 0f)));
            Assert.That(snapshot.WithinTolerance, Is.False);
        }

        [Test]
        public void AlignmentMonitorRejectsInvalidVirtualAccelerationAndFailsNeutral()
        {
            var controller = new GameObject("Alignment monitor");
            try
            {
                var monitor = controller.AddComponent<MotionAlignmentMonitor>();
                Assert.That(monitor.SetVirtualLinearAcceleration(new Vector3(float.NaN, 0f, 0f)), Is.False);
                Assert.That(monitor.Current.Available, Is.False);
                Assert.That(monitor.Current.SuggestedVirtualCorrection, Is.EqualTo(Vector3.zero));
                var unavailable = MotionAlignmentMonitor.Evaluate(Vector3.one, Vector3.zero, false, 1f, 1f, 1f);
                Assert.That(unavailable.Available, Is.False);
                Assert.That(unavailable.Mismatch, Is.EqualTo(Vector3.zero));
            }
            finally { UnityObject.DestroyImmediate(controller); }
        }

        [Test]
        public void MarginCueLayoutProtectsCentralVerticalContent()
        {
            var centers = PeripheralCueMargins.BuildCueCenters(4, 9, 0.72f, 1.45f, 1.05f, 2.8f);
            Assert.That(centers.Length, Is.EqualTo(72));
            foreach (var center in centers)
            {
                Assert.That(Mathf.Abs(center.x), Is.GreaterThan(0.72f));
                Assert.That(Mathf.Abs(center.x), Is.LessThan(1.45f));
                Assert.That(Mathf.Abs(center.y), Is.LessThan(1.05f));
                Assert.That(center.z, Is.EqualTo(2.8f));
            }
        }

        [Test]
        public void DirectionalDomeBuildsADeepCurvedGridBehindContent()
        {
            var centers = DirectionalMotionDome.BuildGroundCenters(48, 42, 3.2f, 38f, 20f, -1.15f, 0.0025f);
            Assert.That(centers.Length, Is.EqualTo(48 * 42 + 3));
            for (var index = 0; index < 48 * 42; index++)
            {
                Assert.That(centers[index].z, Is.GreaterThanOrEqualTo(3.2f));
                Assert.That(centers[index].z, Is.LessThanOrEqualTo(38f));
                Assert.That(float.IsNaN(centers[index].x), Is.False);
                Assert.That(float.IsNaN(centers[index].y), Is.False);
            }
            Assert.That(centers[0].y, Is.LessThan(centers[24].y),
                "curved edges must sit lower than the center horizon");
        }

        [Test]
        public void DirectionalDomeRejectsInvalidInputAndBoundsFlow()
        {
            var invalid = DirectionalMotionDome.EvaluateTargetFlow(
                new Vector3(float.NaN, 0f, 0f), Vector3.one, 3f, 6f, 8f, -1f);
            Assert.That(invalid, Is.EqualTo(Vector3.zero));

            var bounded = DirectionalMotionDome.EvaluateTargetFlow(
                new Vector3(1000f, 1000f, 1000f), new Vector3(0f, 1000f, 0f), 3f, 6f, 2f, -1f);
            Assert.That(bounded.magnitude, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void MarginCuesFailClosedIfCameraIsAddedToTheirHierarchy()
        {
            var controller = new GameObject("Controller");
            var cueObject = new GameObject("Margin cues");
            try
            {
                var hub = controller.AddComponent<VehicleMotionHub>();
                var cues = cueObject.AddComponent<PeripheralCueMargins>();
                cues.Configure(hub, null);
                var lateCamera = new GameObject("Late Camera");
                lateCamera.transform.SetParent(cueObject.transform);
                lateCamera.AddComponent<Camera>();
                LogAssert.Expect(LogType.Error,
                    "InertialLink stopped PeripheralCueMargins because its hierarchy became unsafe.");
                typeof(PeripheralCueMargins).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(cues, null);
                Assert.That(cues.enabled, Is.False);
                Assert.That(cueObject.GetComponent<MeshRenderer>().enabled, Is.False);
            }
            finally
            {
                UnityObject.DestroyImmediate(controller);
                UnityObject.DestroyImmediate(cueObject);
            }
        }
    }
}
