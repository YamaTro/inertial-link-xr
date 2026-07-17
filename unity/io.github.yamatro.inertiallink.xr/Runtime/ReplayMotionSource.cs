using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using YamaTro.InertialLink.Core;

namespace YamaTro.InertialLink
{
    [DisallowMultipleComponent]
    public sealed class ReplayMotionSource : MonoBehaviour, IMotionSource
    {
        private struct Entry
        {
            public float Time;
            public Float3 Linear;
            public Float3 Gyro;
        }

        [Tooltip("CSV rows: seconds,linearX,linearY,linearZ,gyroX,gyroY,gyroZ")]
        [SerializeField] private TextAsset recording = null;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool loop = true;
        [SerializeField, Range(0.1f, 4f)] private float playbackSpeed = 1f;
        [SerializeField, Range(1, 256)] private int maximumFramesPerUpdate = 128;

        private readonly Queue<MotionSourceFrame> frames = new Queue<MotionSourceFrame>();
        private readonly List<Entry> entries = new List<Entry>();
        private int index;
        private float startedAt;
        private bool playing;
        private bool loopPending;
        private uint sequence;
        private ulong session = 0x5245504c41593031UL;

        public bool IsReady { get { return (playing || loopPending) && entries.Count > 0; } }
        public string Status { get { return entries.Count == 0 ? "No valid replay data" : playing ? "Replaying local recording" : loopPending ? "Waiting for replay queue" : "Paused"; } }

        public bool TryDequeue(out MotionSourceFrame frame)
        {
            if (frames.Count > 0) { frame = frames.Dequeue(); return true; }
            frame = default(MotionSourceFrame);
            return false;
        }

        public void Play()
        {
            if (entries.Count == 0) ParseRecording();
            frames.Clear();
            index = 0;
            startedAt = Time.unscaledTime;
            playing = entries.Count > 0;
            loopPending = false;
            sequence = 0;
            session = session == ulong.MaxValue ? 1UL : session + 1UL;
        }

        public void Pause()
        {
            playing = false;
            loopPending = false;
            frames.Clear();
        }

        private void OnEnable()
        {
            ParseRecording();
            if (playOnEnable) Play();
        }

        private void OnDisable() { Pause(); }

        private void Update()
        {
            if (loopPending)
            {
                if (frames.Count != 0) return;
                Play();
            }
            if (!playing || entries.Count == 0) return;
            var elapsed = (Time.unscaledTime - startedAt) * playbackSpeed;
            var dueExclusive = FindDueExclusive(elapsed);
            // If a frame hitch makes a large range due, skip stale history and emit only
            // the most recent bounded window. It cannot burst on a later Update.
            index = Math.Max(index, dueExclusive - Mathf.Clamp(maximumFramesPerUpdate, 1, 256));
            while (index < dueExclusive)
            {
                var entry = entries[index++];
                var now = MonotonicClock.NowNanoseconds;
                var gravity = new Float3(0f, 9.80665f, 0f);
                var imu = new ImuPayload(now, entry.Linear + gravity, entry.Gyro, gravity,
                    entry.Linear, new Float4(0f, 0f, 0f, 1f), 1,
                    (uint)(SensorStatusBits.RawAccelerationValid | SensorStatusBits.GyroscopeValid |
                        SensorStatusBits.GravityValid | SensorStatusBits.LinearAccelerationValid |
                        SensorStatusBits.RotationValid | SensorStatusBits.Calibrated | SensorStatusBits.SensorAccuracyHigh));
                while (frames.Count >= 256) frames.Dequeue();
                frames.Enqueue(new MotionSourceFrame(session, ++sequence, now, now, true, imu));
            }
            if (index < entries.Count) return;
            playing = false;
            loopPending = loop;
        }

        private int FindDueExclusive(float elapsed)
        {
            var low = index;
            var high = entries.Count;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (entries[middle].Time <= elapsed) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private void ParseRecording()
        {
            entries.Clear();
            if (recording == null) return;
            var text = recording.text;
            if (text.Length > 2 * 1024 * 1024) return;
            var previousTime = -1f;
            var lineCount = 0;
            using (var reader = new StringReader(text))
            {
                string line;
                while (lineCount++ < 200000 && (line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line.Length > 512 || line.StartsWith("#", StringComparison.Ordinal) ||
                        line.StartsWith("seconds", StringComparison.OrdinalIgnoreCase)) continue;
                    var fields = line.Split(',');
                    if (fields.Length != 7) continue;
                    var values = new float[7];
                    var valid = true;
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (!float.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                            float.IsNaN(values[i]) || float.IsInfinity(values[i])) { valid = false; break; }
                    }
                    if (!valid || values[0] < 0f || values[0] < previousTime || Mathf.Abs(values[1]) > 200f || Mathf.Abs(values[2]) > 200f ||
                        Mathf.Abs(values[3]) > 200f || Mathf.Abs(values[4]) > 50f || Mathf.Abs(values[5]) > 50f || Mathf.Abs(values[6]) > 50f) continue;
                    previousTime = values[0];
                    entries.Add(new Entry { Time = values[0], Linear = new Float3(values[1], values[2], values[3]), Gyro = new Float3(values[4], values[5], values[6]) });
                    if (entries.Count >= 100000) break;
                }
            }
        }
    }
}
