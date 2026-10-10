#nullable enable
using System;
using System.Text;

namespace Renci.SshNet.Channels
{
    /// <summary>
    /// Checks the authorization of an X11 connection forwarded by the server, then replaces it before anything
    /// reaches the local X server, as OpenSSH does.
    /// </summary>
    /// <remarks>
    /// The server only knows a fake <c>MIT-MAGIC-COOKIE-1</c> cookie. The connection setup sent by the X11 client
    /// (byte order, protocol version, authorization name and data) must carry that cookie; it is then replaced
    /// with the authorization of the local X server, or removed when there is none. A connection that does not
    /// present the fake cookie is rejected and nothing is sent to the local X server.
    /// </remarks>
    internal sealed class X11ConnectionSetup
    {
        /// <summary>
        /// The authorization protocol of the fake cookie sent to the server.
        /// </summary>
        internal const string CookieProtocol = "MIT-MAGIC-COOKIE-1";

        private const int HeaderLength = 12;

        /// <summary>
        /// Longest authorization name or data accepted from the X11 client.
        /// </summary>
        private const int MaxFieldLength = 1024;

        private static readonly byte[] CookieProtocolBytes = Encoding.ASCII.GetBytes(CookieProtocol);

        private readonly byte[] _fakeCookie;
        private readonly byte[] _protocol;
        private readonly byte[] _cookie;
        private byte[] _buffer = new byte[64];
        private int _count;

        /// <summary>
        /// Initializes a new instance of the <see cref="X11ConnectionSetup"/> class.
        /// </summary>
        /// <param name="fakeCookie">The fake cookie sent to the server.</param>
        /// <param name="protocol">The authorization protocol of the local X server, or <see langword="null"/> for none.</param>
        /// <param name="cookie">The authorization data of the local X server, or <see langword="null"/> for none.</param>
        public X11ConnectionSetup(byte[] fakeCookie, string? protocol, byte[]? cookie)
        {
            _fakeCookie = fakeCookie;
            _protocol = protocol is null ? [] : Encoding.ASCII.GetBytes(protocol);
            _cookie = cookie ?? [];
        }

        /// <summary>
        /// Gets the state of the connection setup.
        /// </summary>
        public X11SetupState State { get; private set; }

        /// <summary>
        /// Adds data received from the server.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns>
        /// The connection setup with the local authorization, followed by any data received after it, once the
        /// setup is complete and authorized; otherwise <see langword="null"/> (incomplete, or rejected: see
        /// <see cref="State"/>).
        /// </returns>
        public byte[]? Add(ArraySegment<byte> data)
        {
            if (State != X11SetupState.Pending)
            {
                throw new InvalidOperationException("The X11 connection setup is already complete.");
            }

            Append(data);
            if (_count < HeaderLength)
            {
                return null;
            }

            bool msbFirst;
            switch (_buffer[0])
            {
                case (byte)'B':
                    msbFirst = true;
                    break;
                case (byte)'l':
                    msbFirst = false;
                    break;
                default:
                    return Reject();
            }

            var nameLength = ReadUInt16(_buffer, 6, msbFirst);
            var dataLength = ReadUInt16(_buffer, 8, msbFirst);
            if (nameLength > MaxFieldLength || dataLength > MaxFieldLength)
            {
                return Reject();
            }

            var setupLength = HeaderLength + Pad(nameLength) + Pad(dataLength);
            if (_count < setupLength)
            {
                return null;
            }

            var dataOffset = HeaderLength + Pad(nameLength);
            if (!Matches(_buffer, HeaderLength, nameLength, CookieProtocolBytes)
                || dataLength != _fakeCookie.Length
                || !FixedTimeEquals(_buffer, dataOffset, _fakeCookie))
            {
                return Reject();
            }

            var rest = _count - setupLength;
            var result = new byte[HeaderLength + Pad(_protocol.Length) + Pad(_cookie.Length) + rest];
            Buffer.BlockCopy(_buffer, 0, result, 0, HeaderLength);
            WriteUInt16(result, 6, _protocol.Length, msbFirst);
            WriteUInt16(result, 8, _cookie.Length, msbFirst);
            Buffer.BlockCopy(_protocol, 0, result, HeaderLength, _protocol.Length);
            Buffer.BlockCopy(_cookie, 0, result, HeaderLength + Pad(_protocol.Length), _cookie.Length);
            Buffer.BlockCopy(_buffer, setupLength, result, result.Length - rest, rest);

            Clear();
            State = X11SetupState.Accepted;
            return result;
        }

        private static int Pad(int length)
        {
            return (length + 3) & ~3;
        }

        private static int ReadUInt16(byte[] buffer, int offset, bool msbFirst)
        {
            return msbFirst
                ? (buffer[offset] << 8) | buffer[offset + 1]
                : (buffer[offset + 1] << 8) | buffer[offset];
        }

        private static void WriteUInt16(byte[] buffer, int offset, int value, bool msbFirst)
        {
            buffer[offset + (msbFirst ? 0 : 1)] = (byte)(value >> 8);
            buffer[offset + (msbFirst ? 1 : 0)] = (byte)value;
        }

        private static bool Matches(byte[] buffer, int offset, int length, byte[] expected)
        {
            if (length != expected.Length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (buffer[offset + i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FixedTimeEquals(byte[] buffer, int offset, byte[] expected)
        {
            var difference = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                difference |= buffer[offset + i] ^ expected[i];
            }

            return difference == 0;
        }

        private void Append(ArraySegment<byte> data)
        {
            if (_count + data.Count > _buffer.Length)
            {
                var larger = new byte[Math.Max(_buffer.Length * 2, _count + data.Count)];
                Buffer.BlockCopy(_buffer, 0, larger, 0, _count);
                Array.Clear(_buffer, 0, _buffer.Length);
                _buffer = larger;
            }

            Buffer.BlockCopy(data.Array!, data.Offset, _buffer, _count, data.Count);
            _count += data.Count;
        }

        private byte[]? Reject()
        {
            Clear();
            State = X11SetupState.Rejected;
            return null;
        }

        private void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _count = 0;
        }
    }
}
