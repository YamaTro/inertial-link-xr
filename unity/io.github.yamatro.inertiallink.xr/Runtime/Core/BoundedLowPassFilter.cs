using System;

namespace YamaTro.InertialLink.Core
{
    public sealed class BoundedLowPassFilter3
    {
        private Float3 value;
        private long lastTime;
        private bool initialized;

        public BoundedLowPassFilter3(float cutoffHertz, float maximumAbsoluteValue)
        {
            CutoffHertz = Math.Max(0.01f, Math.Min(50f, cutoffHertz));
            MaximumAbsoluteValue = Math.Max(0.01f, maximumAbsoluteValue);
        }

        public float CutoffHertz { get; private set; }
        public float MaximumAbsoluteValue { get; private set; }
        public Float3 Value { get { return value; } }

        public bool TryUpdate(Float3 input, long timeNanoseconds, out Float3 output)
        {
            output = value;
            if (!input.IsFinite || timeNanoseconds <= 0 || !Within(input, MaximumAbsoluteValue)) return false;
            if (!initialized)
            {
                value = input;
                lastTime = timeNanoseconds;
                initialized = true;
                output = value;
                return true;
            }

            // Authenticated UDP packets may legitimately arrive out of order. Rewinding the
            // filter here would let an older sample jump the output and refresh liveness.
            if (timeNanoseconds <= lastTime) return false;

            // A long forward discontinuity starts a new filter epoch. VehicleMotionHub's
            // SafetyGate independently requires warm-up again after the corresponding dropout.
            if (timeNanoseconds - lastTime > 1000000000L)
            {
                value = input;
                lastTime = timeNanoseconds;
                output = value;
                return true;
            }

            var dt = Math.Max(0.001, Math.Min(0.1, (timeNanoseconds - lastTime) / 1000000000.0));
            var tau = 1.0 / (2.0 * Math.PI * CutoffHertz);
            var alpha = (float)(dt / (tau + dt));
            value = Float3.Lerp(value, input, alpha);
            lastTime = timeNanoseconds;
            output = value;
            return true;
        }

        public void Reset()
        {
            value = Float3.Zero;
            lastTime = 0;
            initialized = false;
        }

        private static bool Within(Float3 input, float bound)
        {
            return Math.Abs(input.X) <= bound && Math.Abs(input.Y) <= bound && Math.Abs(input.Z) <= bound;
        }
    }
}
