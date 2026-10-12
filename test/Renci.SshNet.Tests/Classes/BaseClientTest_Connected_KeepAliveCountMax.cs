using System;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Renci.SshNet.Messages.Connection;

namespace Renci.SshNet.Tests.Classes
{
    /// <summary>
    /// <see cref="BaseClient.KeepAliveCountMax"/>: keep-alive requests that the server must answer, and the connection
    /// considered lost when it leaves them unanswered.
    /// </summary>
    [TestClass]
    public class BaseClientTest_Connected_KeepAliveCountMax : BaseClientTestBase
    {
        private ConnectionInfo _connectionInfo;
        private BaseClient _client;
        private long _receivedMessageCount;
        private int _requestCount;
        private ManualResetEventSlim _timedOut;

        protected override void SetupData()
        {
            _connectionInfo = new ConnectionInfo("host", "user", new PasswordAuthenticationMethod("user", "pwd"));
            _receivedMessageCount = 0;
            _requestCount = 0;
            _timedOut = new ManualResetEventSlim();
        }

        protected override void SetupMocks()
        {
            ServiceFactoryMock.Setup(p => p.CreateSocketFactory())
                               .Returns(SocketFactoryMock.Object);
            ServiceFactoryMock.Setup(p => p.CreateSession(_connectionInfo, SocketFactoryMock.Object))
                               .Returns(SessionMock.Object);
            SessionMock.Setup(p => p.Connect());
            SessionMock.Setup(p => p.IsConnected).Returns(true);
            SessionMock.Setup(p => p.ReceivedMessageCount).Returns(() => Interlocked.Read(ref _receivedMessageCount));
            SessionMock.Setup(p => p.TrySendMessage(It.Is<GlobalRequestMessage>(m => m.RequestName == "keepalive@openssh.com" && m.WantReply)))
                        .Returns(true)
                        .Callback(() => Interlocked.Increment(ref _requestCount));
            SessionMock.Setup(p => p.OnKeepAliveTimeout(It.IsAny<string>()))
                        .Callback(() => _timedOut.Set());
        }

        protected override void Arrange()
        {
            base.Arrange();

            _client = new MyClient(_connectionInfo, false, ServiceFactoryMock.Object);
            _client.Connect();
        }

        protected override void TearDown()
        {
            if (_client != null)
            {
                SessionMock.Setup(p => p.OnDisconnecting());
                SessionMock.Setup(p => p.Dispose());
                _client.Dispose();
            }

            _timedOut?.Dispose();
        }

        protected override void Act()
        {
        }

        [TestMethod]
        public void ServerSilent_ConnectionLostAfterCountMaxIntervals()
        {
            _client.KeepAliveCountMax = 3;
            _client.KeepAliveInterval = TimeSpan.FromMilliseconds(50d);

            Assert.IsTrue(_timedOut.Wait(TimeSpan.FromSeconds(5)));
            // One request after the first interval, then two more unanswered before the third unanswered interval.
            Assert.AreEqual(3, Interlocked.CompareExchange(ref _requestCount, 0, 0));
        }

        [TestMethod]
        public void ServerAnswering_ConnectionNotLost()
        {
            _client.KeepAliveCountMax = 2;
            _client.KeepAliveInterval = TimeSpan.FromMilliseconds(50d);

            for (var i = 0; i < 10; i++)
            {
                // any message from the server, like the answers to the keep-alive requests
                Interlocked.Increment(ref _receivedMessageCount);
                Thread.Sleep(30);
            }

            Assert.IsFalse(_timedOut.IsSet);
            Assert.IsTrue(Interlocked.CompareExchange(ref _requestCount, 0, 0) >= 3);
        }

        [TestMethod]
        public void ReceivedMessageCount_IsTheSessionCount()
        {
            Interlocked.Exchange(ref _receivedMessageCount, 5);

            Assert.AreEqual(5, _client.ReceivedMessageCount);
        }

        [TestMethod]
        public void SendServerAliveRequest_SendsARequestToAnswer()
        {
            Assert.IsTrue(_client.SendServerAliveRequest());

            Assert.AreEqual(1, Interlocked.CompareExchange(ref _requestCount, 0, 0));
            SessionMock.Verify(p => p.OnKeepAliveTimeout(It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public void KeepAliveCountMax_Negative_Throws()
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _client.KeepAliveCountMax = -1);
        }

        private class MyClient : BaseClient
        {
            public MyClient(ConnectionInfo connectionInfo, bool ownsConnectionInfo, IServiceFactory serviceFactory) : base(connectionInfo, ownsConnectionInfo, serviceFactory)
            {
            }
        }
    }
}
