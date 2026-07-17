using System;

namespace YamaTro.InertialLink.Core
{
    public static class MotionSampleValidator
    {
        public static PacketError Validate(ImuPayload imu)
        {
            if (!imu.RawAcceleration.IsFinite || !imu.AngularVelocity.IsFinite ||
                !imu.Gravity.IsFinite || !imu.LinearAcceleration.IsFinite || !imu.Rotation.IsFinite)
                return PacketError.NonFiniteValue;
            if (!Within(imu.RawAcceleration, ProtocolConstants.MaximumAccelerationComponent) ||
                !Within(imu.Gravity, ProtocolConstants.MaximumGravityComponent) ||
                !Within(imu.LinearAcceleration, ProtocolConstants.MaximumAccelerationComponent) ||
                !Within(imu.AngularVelocity, ProtocolConstants.MaximumAngularVelocityComponent) ||
                Math.Abs(imu.Rotation.X) > ProtocolConstants.MaximumQuaternionComponent ||
                Math.Abs(imu.Rotation.Y) > ProtocolConstants.MaximumQuaternionComponent ||
                Math.Abs(imu.Rotation.Z) > ProtocolConstants.MaximumQuaternionComponent ||
                Math.Abs(imu.Rotation.W) > ProtocolConstants.MaximumQuaternionComponent)
                return PacketError.ValueOutOfRange;
            var norm = imu.Rotation.Norm;
            if (norm < ProtocolConstants.MinimumQuaternionNorm || norm > ProtocolConstants.MaximumQuaternionNorm)
                return PacketError.InvalidQuaternion;
            return SensorStatus.IsValid(imu.StatusBits) ? PacketError.None : PacketError.InvalidStatusBits;
        }

        private static bool Within(Float3 value, float limit)
        {
            return Math.Abs(value.X) <= limit && Math.Abs(value.Y) <= limit && Math.Abs(value.Z) <= limit;
        }
    }
}
