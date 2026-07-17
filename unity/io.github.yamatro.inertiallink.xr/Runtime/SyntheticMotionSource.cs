using System.Collections.Concurrent;
using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    [DisallowMultipleComponent]
    public sealed class SyntheticMotionSource : MonoBehaviour, IMotionSource
    {
        [SerializeField, Range(1f, 120f)] private float samplesPerSecond = 60f;
        [SerializeField, Range(0f, 10f)] private float lateralAcceleration = 1.2f;
        [SerializeField, Range(0.01f, 2f)] private float frequencyHertz = 0.15f;
        [SerializeField, Range(0f, 2f)] private float yawRateRadiansPerSecond = 0.2f;

        private readonly ConcurrentQueue<MotionSourceFrame> frames = new ConcurrentQueue<MotionSourceFrame>();
        private float nextSampleTime;
        private uint sequence;
        private ulong session = 0x53594e5448455449UL;

        public bool IsReady { get { return isActiveAndEnabled; } }
        public string Status { get { return isActiveAndEnabled ? "Synthetic test signal" : "Stopped"; } }

        public bool TryDequeue(out MotionSourceFrame frame) { return frames.TryDequeue(out frame); }

        private void OnDisable()
        {
            MotionSourceFrame discarded;
            while (frames.TryDequeue(out discarded)) { }
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSampleTime) return;
            var sampleRate = float.IsNaN(samplesPerSecond) || float.IsInfinity(samplesPerSecond)
                ? 60f : Mathf.Clamp(samplesPerSecond, 1f, 120f);
            nextSampleTime = Time.unscaledTime + (1f / sampleRate);
            var now = MonotonicClock.NowNanoseconds;
            var phase = Time.unscaledTime * frequencyHertz * Mathf.PI * 2f;
            var linear = new Float3(Mathf.Sin(phase) * lateralAcceleration, 0f, 0f);
            var gyro = new Float3(0f, Mathf.Sin(phase) * yawRateRadiansPerSecond, 0f);
            var gravity = new Float3(0f, 9.80665f, 0f);
            var imu = new ImuPayload(now, linear + gravity, gyro, gravity, linear,
                new Float4(0f, 0f, 0f, 1f), 1,
                (uint)(SensorStatusBits.RawAccelerationValid | SensorStatusBits.GyroscopeValid |
                    SensorStatusBits.GravityValid | SensorStatusBits.LinearAccelerationValid |
                    SensorStatusBits.RotationValid | SensorStatusBits.Calibrated | SensorStatusBits.SensorAccuracyHigh));
            if (sequence == uint.MaxValue)
            {
                MotionSourceFrame oldFrame;
                while (frames.TryDequeue(out oldFrame)) { }
                sequence = 0;
                session = session == ulong.MaxValue ? 1UL : session + 1UL;
            }
            frames.Enqueue(new MotionSourceFrame(session, ++sequence, now, now, true, imu));
            MotionSourceFrame discarded;
            while (frames.Count > 8) frames.TryDequeue(out discarded);
        }
    }
}
