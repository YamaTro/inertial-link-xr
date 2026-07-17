namespace YamaTro.InertialLink.Core
{
    public static class ProtocolConstants
    {
        public const byte MajorVersion = 1;
        public const byte MinorVersion = 0;
        public const int HeaderLength = 32;
        public const int ImuPayloadLength = 80;
        public const int SyncRequestPayloadLength = 16;
        public const int SyncResponsePayloadLength = 32;
        public const int AuthenticationTagLength = 16;
        public const int MaximumDatagramLength = 512;
        public const int PairingKeyLength = 16;
        public const int DefaultPort = 28461;

        public const byte AuthenticationFlag = 1;
        public const float MaximumAccelerationComponent = 200f;
        public const float MaximumGravityComponent = 30f;
        public const float MaximumAngularVelocityComponent = 50f;
        public const float MaximumQuaternionComponent = 1.5f;
        public const float MinimumQuaternionNorm = 0.5f;
        public const float MaximumQuaternionNorm = 1.5f;
    }

    [System.Flags]
    public enum SensorStatusBits : uint
    {
        None = 0,
        RawAccelerationValid = 1U << 0,
        GyroscopeValid = 1U << 1,
        GravityValid = 1U << 2,
        LinearAccelerationValid = 1U << 3,
        RotationValid = 1U << 4,
        Calibrated = 1U << 5,
        Calibrating = 1U << 6,
        SensorAccuracyLow = 1U << 8,
        SensorAccuracyMedium = 1U << 9,
        SensorAccuracyHigh = 1U << 10
    }

    public static class SensorStatus
    {
        public const SensorStatusBits RequiredForMotion = SensorStatusBits.GyroscopeValid | SensorStatusBits.LinearAccelerationValid;
        public const uint AccuracyMask = (uint)(SensorStatusBits.SensorAccuracyLow | SensorStatusBits.SensorAccuracyMedium | SensorStatusBits.SensorAccuracyHigh);
        public const uint KnownMask = 0x0000077FU;

        public static bool HasRequiredMotionInputs(uint statusBits)
        {
            return ((SensorStatusBits)statusBits & RequiredForMotion) == RequiredForMotion;
        }

        public static bool IsValid(uint statusBits)
        {
            if ((statusBits & ~KnownMask) != 0) return false;
            var accuracy = statusBits & AccuracyMask;
            return accuracy == 0 || (accuracy & (accuracy - 1)) == 0;
        }
    }

    public enum PacketType : byte
    {
        Imu = 1,
        SyncRequest = 2,
        SyncResponse = 3
    }

    public enum PacketError
    {
        None = 0,
        NullDatagram,
        TooShort,
        TooLarge,
        BadMagic,
        UnsupportedVersion,
        UnsupportedType,
        UnsupportedFlags,
        BadHeaderLength,
        BadPayloadLength,
        LengthMismatch,
        AuthenticationRequired,
        MissingPairingKey,
        AuthenticationFailed,
        InvalidSession,
        InvalidTimestamp,
        NonFiniteValue,
        ValueOutOfRange,
        InvalidQuaternion,
        InvalidStatusBits
    }
}
