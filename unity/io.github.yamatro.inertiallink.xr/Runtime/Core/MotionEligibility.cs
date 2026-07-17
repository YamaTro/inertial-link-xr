namespace YamaTro.InertialLink.Core
{
    public static class MotionEligibility
    {
        // Live network motion is diagnostic-only until a trusted clock exchange succeeds.
        // Synthetic/replay sources explicitly mark their local timestamps synchronized.
        public static bool CanDrive(bool timestampSynchronized, uint statusBits)
        {
            return timestampSynchronized && SensorStatus.IsValid(statusBits) &&
                   SensorStatus.HasRequiredMotionInputs(statusBits);
        }
    }
}
