using System;
using JetBrains.Annotations;
using Microsoft.Xna.Framework;
using TShockAPI;

namespace Clans.Extensions
{
    /// <summary>
    ///     Provides extension methods for the <see cref="TSPlayer" /> type.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static class TSPlayerExtensions
    {
        /// <summary>
        ///     Sends a prefixed message to the specified player.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="message">The message, which must not be <c>null</c>.</param>
        /// <param name="color">The message color.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message" /> is null.</exception>
        private static void SendClansMessage(this TSPlayer player, [NotNull] string message, Color color)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            player.SendMessage($"[Clans] {message}", color);
        }
        
        /// <summary>
        ///     Sends a prefixed error message to the specified player.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="message">The message, which must not be <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message" /> is null.</exception>
        public static void SendClansErrorMessage(this TSPlayer player, [NotNull] string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            SendClansMessage(player, message, Color.Red);
        }
        
        /// <summary>
        ///     Sends a prefixed info message to the specified player.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="message">The message, which must not be <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message" /> is null.</exception>
        public static void SendClansInfoMessage(this TSPlayer player, [NotNull] string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            SendClansMessage(player, message, Color.LightYellow);
        }

        /// <summary>
        ///     Sends a prefixed success message to the specified player.
        /// </summary>
        /// <param name="player">The player.</param>
        /// <param name="message">The message, which must not be <c>null</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message" /> is null.</exception>
        public static void SendClansSuccessMessage(this TSPlayer player, [NotNull] string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            SendClansMessage(player, message, Color.Green);
        }
    }
}