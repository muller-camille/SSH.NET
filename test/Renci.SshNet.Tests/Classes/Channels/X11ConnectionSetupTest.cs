using System;
using System.Linq;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Renci.SshNet.Channels;

namespace Renci.SshNet.Tests.Classes.Channels
{
    [TestClass]
    public class X11ConnectionSetupTest
    {
        private static readonly byte[] FakeCookie = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

        [TestMethod]
        [DataRow((byte)'l')]
        [DataRow((byte)'B')]
        public void FakeCookieIsReplacedWithTheLocalCookie(byte byteOrder)
        {
            var realCookie = Enumerable.Range(100, 16).Select(i => (byte)i).ToArray();
            var setup = new X11ConnectionSetup(FakeCookie, X11ConnectionSetup.CookieProtocol, realCookie);

            var result = setup.Add(new ArraySegment<byte>(Setup(byteOrder, X11ConnectionSetup.CookieProtocol, FakeCookie)));

            Assert.AreEqual(X11SetupState.Accepted, setup.State);
            CollectionAssert.AreEqual(Setup(byteOrder, X11ConnectionSetup.CookieProtocol, realCookie), result);
        }

        [TestMethod]
        [DataRow((byte)'l')]
        [DataRow((byte)'B')]
        public void FakeCookieIsRemovedWhenTheLocalServerHasNone(byte byteOrder)
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);

            var result = setup.Add(new ArraySegment<byte>(Setup(byteOrder, X11ConnectionSetup.CookieProtocol, FakeCookie)));

            Assert.AreEqual(X11SetupState.Accepted, setup.State);
            CollectionAssert.AreEqual(Setup(byteOrder, string.Empty, []), result);
        }

        [TestMethod]
        public void SetupSplitAcrossPacketsIsCompletedFirst()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);
            var bytes = Setup((byte)'l', X11ConnectionSetup.CookieProtocol, FakeCookie);

            for (var i = 0; i < bytes.Length - 1; i++)
            {
                Assert.IsNull(setup.Add(new ArraySegment<byte>(bytes, i, 1)));
                Assert.AreEqual(X11SetupState.Pending, setup.State);
            }

            Assert.IsNotNull(setup.Add(new ArraySegment<byte>(bytes, bytes.Length - 1, 1)));
            Assert.AreEqual(X11SetupState.Accepted, setup.State);
        }

        [TestMethod]
        public void DataAfterTheSetupIsKept()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);
            var request = new byte[] { 55, 0, 4, 0, 1, 2, 3, 4 };

            var result = setup.Add(new ArraySegment<byte>(Setup((byte)'l', X11ConnectionSetup.CookieProtocol, FakeCookie).Concat(request).ToArray()));

            CollectionAssert.AreEqual(Setup((byte)'l', string.Empty, []).Concat(request).ToArray(), result);
        }

        [TestMethod]
        public void WrongCookieIsRejected()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);
            var wrong = FakeCookie.ToArray();
            wrong[15] ^= 1;

            Assert.IsNull(setup.Add(new ArraySegment<byte>(Setup((byte)'l', X11ConnectionSetup.CookieProtocol, wrong))));
            Assert.AreEqual(X11SetupState.Rejected, setup.State);
        }

        [TestMethod]
        public void MissingCookieIsRejected()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);

            Assert.IsNull(setup.Add(new ArraySegment<byte>(Setup((byte)'l', string.Empty, []))));
            Assert.AreEqual(X11SetupState.Rejected, setup.State);
        }

        [TestMethod]
        public void OtherProtocolIsRejected()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);

            Assert.IsNull(setup.Add(new ArraySegment<byte>(Setup((byte)'l', "XDM-AUTHORIZATION-1", FakeCookie))));
            Assert.AreEqual(X11SetupState.Rejected, setup.State);
        }

        [TestMethod]
        public void UnknownByteOrderIsRejected()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);

            Assert.IsNull(setup.Add(new ArraySegment<byte>(Setup((byte)'x', X11ConnectionSetup.CookieProtocol, FakeCookie))));
            Assert.AreEqual(X11SetupState.Rejected, setup.State);
        }

        [TestMethod]
        public void OversizedAuthorizationIsRejectedWithoutWaitingForIt()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);
            var header = new byte[] { (byte)'l', 0, 11, 0, 0, 0, 18, 0, 0xFF, 0xFF, 0, 0 };

            Assert.IsNull(setup.Add(new ArraySegment<byte>(header)));
            Assert.AreEqual(X11SetupState.Rejected, setup.State);
        }

        [TestMethod]
        public void AddingAfterTheSetupThrows()
        {
            var setup = new X11ConnectionSetup(FakeCookie, protocol: null, cookie: null);
            _ = setup.Add(new ArraySegment<byte>(Setup((byte)'l', X11ConnectionSetup.CookieProtocol, FakeCookie)));

            _ = Assert.ThrowsExactly<InvalidOperationException>(() => setup.Add(new ArraySegment<byte>([1])));
        }

        /// <summary>
        /// X11 connection setup: byte order, unused, protocol 11.0, name and data lengths, unused, then the padded name and data.
        /// </summary>
        private static byte[] Setup(byte byteOrder, string name, byte[] data)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            var result = new byte[12 + Pad(nameBytes.Length) + Pad(data.Length)];
            var msbFirst = byteOrder == (byte)'B';
            result[0] = byteOrder;
            Write(result, 2, 11, msbFirst);
            Write(result, 6, nameBytes.Length, msbFirst);
            Write(result, 8, data.Length, msbFirst);
            nameBytes.CopyTo(result, 12);
            data.CopyTo(result, 12 + Pad(nameBytes.Length));
            return result;
        }

        private static int Pad(int length)
        {
            return (length + 3) & ~3;
        }

        private static void Write(byte[] buffer, int offset, int value, bool msbFirst)
        {
            buffer[offset + (msbFirst ? 0 : 1)] = (byte)(value >> 8);
            buffer[offset + (msbFirst ? 1 : 0)] = (byte)value;
        }
    }
}
