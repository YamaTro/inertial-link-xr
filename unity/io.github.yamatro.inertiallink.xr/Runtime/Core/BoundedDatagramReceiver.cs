using System;
using System.Net;
using System.Net.Sockets;

namespace YamaTro.InertialLink.Core
{
    public static class BoundedDatagramReceiver
    {
        public const int DetectionBufferLength = ProtocolConstants.MaximumDatagramLength + 1;

        public static bool TryReceive(Socket socket, byte[] detectionBuffer, ref EndPoint remote,
            out int receivedLength)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            if (detectionBuffer == null) throw new ArgumentNullException("detectionBuffer");
            if (detectionBuffer.Length != DetectionBufferLength)
                throw new ArgumentException("Detection buffer must be exactly 513 bytes.", "detectionBuffer");
            try
            {
                receivedLength = socket.ReceiveFrom(detectionBuffer, 0, detectionBuffer.Length,
                    SocketFlags.None, ref remote);
                return receivedLength <= ProtocolConstants.MaximumDatagramLength;
            }
            catch (SocketException exception)
            {
                if (exception.SocketErrorCode != SocketError.MessageSize) throw;
                receivedLength = DetectionBufferLength;
                return false;
            }
        }
    }
}
