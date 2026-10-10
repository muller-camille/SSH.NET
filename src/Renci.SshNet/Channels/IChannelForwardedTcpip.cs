using System;
using System.Net;

using Renci.SshNet.Common;

namespace Renci.SshNet.Channels
{
    /// <summary>
    /// A "forwarded-tcpip" SSH channel.
    /// </summary>
    internal interface IChannelForwardedTcpip : IDisposable
    {
        /// <summary>
        /// Occurs when an exception is thrown while processing channel messages.
        /// </summary>
        event EventHandler<ExceptionEventArgs> Exception;

        /// <summary>
        /// Binds the channel to the specified endpoint.
        /// </summary>
        /// <param name="remoteEndpoint">The endpoint to connect to.</param>
        /// <param name="forwardedPort">The forwarded port for which the channel is opened.</param>
        void Bind(IPEndPoint remoteEndpoint, IForwardedPort forwardedPort);

        /// <summary>
        /// Binds an X11 channel to the local X server.
        /// </summary>
        /// <param name="remoteEndpoint">The endpoint of the local X server.</param>
        /// <param name="forwardedPort">The forwarded port for which the channel is opened.</param>
        /// <param name="x11Setup">Checks and replaces the authorization of the X11 connection before anything reaches the local X server.</param>
        void Bind(IPEndPoint remoteEndpoint, IForwardedPort forwardedPort, X11ConnectionSetup x11Setup);
    }
}
