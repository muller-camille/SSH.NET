#nullable enable
using System;
using System.Net;

using Renci.SshNet.Common;

namespace Renci.SshNet
{
    /// <summary>
    /// X11 forwarding for a shell, like the <c>ssh -X</c> option: X11 clients started from the shell on the server
    /// show their windows on a local X server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server receives a fake <c>MIT-MAGIC-COOKIE-1</c> cookie. Each X11 connection it forwards must present
    /// that cookie; the cookie is then replaced with <see cref="AuthenticationProtocol"/> and
    /// <see cref="AuthenticationData"/>, or removed when they are not set, before anything is sent to the local
    /// X server. A connection that does not present the cookie is closed.
    /// </para>
    /// <para>
    /// Whoever controls the server can still use the local X server through the connections it forwards (read the
    /// other windows of the X server, send key strokes to them): only forward X11 to servers you trust.
    /// </para>
    /// </remarks>
    public sealed class X11Forwarding
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="X11Forwarding"/> class.
        /// </summary>
        /// <param name="display">The TCP endpoint of the local X server, for example 127.0.0.1:6000 for display <c>:0</c>.</param>
        /// <param name="screenNumber">The screen number announced to the server.</param>
        public X11Forwarding(IPEndPoint display, uint screenNumber = 0)
        {
            ArgumentNullException.ThrowIfNull(display);

            Display = display;
            ScreenNumber = screenNumber;
        }

        /// <summary>
        /// Occurs when the server opens an X11 connection, with the address and port of the X11 client as seen by the server.
        /// </summary>
        public event EventHandler<PortForwardEventArgs>? RequestReceived;

        /// <summary>
        /// Occurs when a forwarded X11 connection fails.
        /// </summary>
        public event EventHandler<ExceptionEventArgs>? Exception;

        /// <summary>
        /// Gets the TCP endpoint of the local X server.
        /// </summary>
        public IPEndPoint Display { get; }

        /// <summary>
        /// Gets the screen number announced to the server.
        /// </summary>
        public uint ScreenNumber { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the server may forward a single X11 connection only.
        /// </summary>
        public bool SingleConnection { get; set; }

        /// <summary>
        /// Gets or sets the authorization protocol of the local X server, for example <c>MIT-MAGIC-COOKIE-1</c>,
        /// or <see langword="null"/> when the local X server does not check the authorization.
        /// </summary>
        public string? AuthenticationProtocol { get; set; }

        /// <summary>
        /// Gets or sets the authorization data of the local X server (the cookie itself, not its hexadecimal form),
        /// or <see langword="null"/> when the local X server does not check the authorization.
        /// </summary>
        public byte[]? AuthenticationData { get; set; }

        internal void RaiseRequestReceived(string host, uint port)
        {
            RequestReceived?.Invoke(this, new PortForwardEventArgs(host, port));
        }

        internal void RaiseException(Exception exception)
        {
            Exception?.Invoke(this, new ExceptionEventArgs(exception));
        }
    }
}
