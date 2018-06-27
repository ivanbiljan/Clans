using System;
using JetBrains.Annotations;

namespace Clans.Database
{
    /// <summary>
    ///     Holds player information.
    /// </summary>
    public sealed class PlayerMetadata
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PlayerMetadata" /> class with the specified clan and rank.
        /// </summary>
        /// <param name="clan">The clan, which must not be <c>null</c>.</param>
        /// <param name="clanRank">The rank, which must not be <c>null</c>.</param>
        public PlayerMetadata([NotNull] Clan clan, [NotNull] ClanRank clanRank)
        {
            Clan = clan ?? throw new ArgumentNullException(nameof(clan));
            Rank = clanRank ?? throw new ArgumentNullException(nameof(clanRank));
        }

        /// <summary>
        ///     Gets the clan.
        /// </summary>
        [NotNull]
        public Clan Clan { get; }

        /// <summary>
        ///     Gets or sets the clan rank.
        /// </summary>
        [NotNull]
        public ClanRank Rank { get; set; }
    }
}
