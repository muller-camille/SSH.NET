#nullable enable
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Renci.SshNet.Abstractions;
using Renci.SshNet.Channels;
using Renci.SshNet.Messages.Connection;

namespace Renci.SshNet
{
    /// <summary>
    /// Accepts the X11 channels opened by the server for the shells of a session that requested X11 forwarding, and
    /// connects each one to the local X server once its fake cookie has been checked.
    /// </summary>
    /// <remarks>
    /// The server does not tell which shell an X11 channel belongs to: one handler serves all the shells of a session,
    /// with the options and the fake cookie of the first one, until the last of them is disposed.
    /// </remarks>
    internal sealed class X11ForwardingHandler : IForwardedPort
    {
        private const int CookieLength = 16;

        private static readonly ConditionalWeakTable<ISession, X11ForwardingHandler> Handlers = new ConditionalWeakTable<ISession, X11ForwardingHandler>();

        private readonly ISession _session;
        private readonly X11Forwarding _options;
        private readonly byte[] _fakeCookie;
        private int _references;
        private int _connections;
        private volatile bool _stopped;

        private X11ForwardingHandler(ISession session, X11Forwarding options)
        {
            _session = session;
            _options = options;
            _fakeCookie = RandomNumberGenerator.GetBytes(CookieLength);

            // Without it, a channel opened by the server would stop the message loop of the session.
            _session.RegisterMessage("SSH_MSG_CHANNEL_OPEN");
            _session.ChannelOpenReceived += Session_ChannelOpenReceived;
        }

        /// <summary>
        /// Occurs when X11 forwarding stops, to close the forwarded connections.
        /// </summary>
        public event EventHandler? Closing;

        /// <summary>
        /// Gets the fake cookie to send to the server in the <c>x11-req</c> request, in hexadecimal as RFC 4254 requires.
        /// </summary>
        public byte[] FakeCookieHex
        {
            get
            {
                var hex = new StringBuilder(_fakeCookie.Length * 2);
                foreach (var b in _fakeCookie)
                {
                    _ = hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return Encoding.ASCII.GetBytes(hex.ToString());
            }
        }

        /// <summary>
        /// Gets the handler of a session, created for this shell if the session has none.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="options">The X11 forwarding options of the shell.</param>
        /// <returns>The handler; call <see cref="Release"/> when the shell no longer forwards X11.</returns>
        public static X11ForwardingHandler Attach(ISession session, X11Forwarding options)
        {
            lock (Handlers)
            {
                if (!Handlers.TryGetValue(session, out var handler))
                {
                    handler = new X11ForwardingHandler(session, options);
                    Handlers.Add(session, handler);
                }

                handler._references++;
                return handler;
            }
        }

        /// <summary>
        /// Releases the handler for a shell; X11 forwarding stops with the last shell.
        /// </summary>
        public void Release()
        {
            lock (Handlers)
            {
                if (--_references > 0)
                {
                    return;
                }
            }

            Dispose();
        }

        /// <summary>
        /// Stops X11 forwarding for all the shells of the session and closes the forwarded connections.
        /// </summary>
        public void Dispose()
        {
            lock (Handlers)
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                _references = 0;
                _session.ChannelOpenReceived -= Session_ChannelOpenReceived;
                _ = Handlers.Remove(_session);
            }

            Closing?.Invoke(this, EventArgs.Empty);
        }

        private void Session_ChannelOpenReceived(object? sender, MessageEventArgs<ChannelOpenMessage> e)
        {
            var message = e.Message;
            if (message.Info is not X11ChannelOpenInfo info)
            {
                return;
            }

            if (_stopped || (_options.SingleConnection && Interlocked.Increment(ref _connections) > 1))
            {
                _session.SendMessage(new ChannelOpenFailureMessage(message.LocalChannelNumber,
                                                                   "X11 forwarding is not active.",
                                                                   ChannelOpenFailureMessage.AdministrativelyProhibited));
                return;
            }

            // Bind blocks for the life of the connection: not on the message loop of the session.
            ThreadAbstraction.ExecuteThread(() =>
            {
                try
                {
                    _options.RaiseRequestReceived(info.OriginatorAddress, info.OriginatorPort);

                    using (var channel = _session.CreateChannelForwardedTcpip(message.LocalChannelNumber, message.InitialWindowSize, message.MaximumPacketSize))
                    {
                        channel.Exception += (_, args) => _options.RaiseException(args.Exception);
                        channel.Bind(_options.Display,
                                     this,
                                     new X11ConnectionSetup(_fakeCookie, _options.AuthenticationProtocol, _options.AuthenticationData));
                    }
                }
                catch (Exception exception)
                {
                    _options.RaiseException(exception);
                }
            });
        }
    }
}
