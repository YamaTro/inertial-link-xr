using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;
using YamaTro.InertialLink.Core;

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
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RootContainingCameraIsRejected()
        {
            var root = new GameObject("VisualContent");
            var camera = new GameObject("Camera");
            camera.transform.SetParent(root.transform);
            camera.AddComponent<Camera>();
            try { Assert.That(EnvironmentMotionDriver.IsSafeTarget(root.transform), Is.False); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RootUnderCameraIsRejected()
        {
            var camera = new GameObject("Camera");
            camera.AddComponent<Camera>();
            var root = new GameObject("HeadLockedContent");
            root.transform.SetParent(camera.transform);
            try { Assert.That(EnvironmentMotionDriver.IsSafeTarget(root.transform), Is.False); }
            finally { Object.DestroyImmediate(camera); }
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
            finally { Object.DestroyImmediate(xrOrigin); }
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
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(visualRoot);
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
            finally { Object.DestroyImmediate(receiverObject); }
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
            finally { Object.DestroyImmediate(receiverObject); }
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
            finally { Object.DestroyImmediate(controller); }
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
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
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
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(visualRoot);
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
            finally { Object.DestroyImmediate(controller); }
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
            finally { Object.DestroyImmediate(controller); }
        }

        [Test]
        public void OpenXrVectorsMapToUnityHandedness()
        {
            Assert.That(CoordinateMapping.PolarVectorToUnity(new Float3(1, 2, 3)), Is.EqualTo(new Vector3(1, 2, -3)));
            Assert.That(CoordinateMapping.AngularVelocityToUnity(new Float3(1, 2, 3)), Is.EqualTo(new Vector3(-1, -2, 3)));
            var rotation = CoordinateMapping.RotationToUnity(new Float4(0.1f, 0.2f, 0.3f, 0.9f));
            Assert.That(rotation, Is.EqualTo(new Quaternion(-0.1f, -0.2f, 0.3f, 0.9f)));
        }
    }
}
