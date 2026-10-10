namespace Renci.SshNet.Channels
{
    /// <summary>
    /// The state of the setup of a forwarded X11 connection.
    /// </summary>
    internal enum X11SetupState
    {
        /// <summary>
        /// Waiting for the complete connection setup.
        /// </summary>
        Pending,

        /// <summary>
        /// The connection presented the fake cookie; its data now goes to the local X server.
        /// </summary>
        Accepted,

        /// <summary>
        /// The connection did not present the fake cookie; nothing is sent to the local X server.
        /// </summary>
        Rejected,
    }
}
