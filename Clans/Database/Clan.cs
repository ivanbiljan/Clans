using System;
using System.Collections.Generic;
using System.Linq;
using Clans.Extensions;
using JetBrains.Annotations;
using TShockAPI;

namespace Clans.Database
{
    /// <summary>
    ///     Represents a clan.
    /// </summary>
    public sealed class Clan
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Clan" /> class with the specified name and owner.
        /// </summary>
        /// <param name="name">The name, which must not be <c>null</c>.</param>
        /// <param name="owner">The owner's name, which must not be <c>null</c>.</param>
        public Clan([NotNull] string name, [NotNull] string owner)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        ///     Gets or sets the chat color.
        /// </summary>
        public string ChatColor { get; set; } = "255,255,255";

        /// <summary>
        ///     Gets or sets the value indicating whether friendly fire is enabled for this clan.
        /// </summary>
        public bool IsFriendlyFire { get; set; }

        /// <summary>
        ///     Gets the members.
        /// </summary>
        [ItemNotNull]
        public IList<string> Members { get; } = new List<string>();

        /// <summary>
        ///     Gets or sets the message of the day.
        /// </summary>
        public string Motd { get; set; }

        /// <summary>
        ///     Gets or sets the name.
        /// </summary>
        [NotNull]
        public string Name { get; set; }

        /// <summary>
        ///     Gets the owner.
        /// </summary>
        [NotNull]
        public string Owner { get; }

        /// <summary>
        ///     Gets or sets the prefix.
        /// </summary>
        public string Prefix { get; set; }

        /// <summary>
        ///     Gets the ranks.
        /// </summary>
        public IList<ClanRank> Ranks { get; } = new List<ClanRank>();

        /// <summary>
        ///     Broadcasts the specified message to online members.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="ignoreIndexes">
        ///     A collection of player indexes that specifies which players should not receive the message.
        /// </param>
        public void SendMessage(string message, params int[] ignoreIndexes)
        {
            foreach (var player in TShock.Players.Where(p =>
                Members.Any(m => m.Equals(p?.User?.Name)) && !ignoreIndexes.Contains(p.Index)))
            {
                player.SendMessage(message, ChatColor.GetColor());
            }
        }
    }
}