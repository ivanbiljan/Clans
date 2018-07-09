using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using JetBrains.Annotations;
using TShockAPI.DB;

namespace Clans.Database
{
    /// <summary>
    ///     Represents a member database manager.
    /// </summary>
    public sealed class MemberManager : IDisposable
    {
        private readonly ClanManager _clanManager;
        private readonly IDbConnection _connection;
        private readonly Dictionary<string, PlayerMetadata> _metadataCache = new Dictionary<string, PlayerMetadata>();
        private readonly object _syncLock = new object();

        /// <summary>
        ///     Initializes a new instance of the <see cref="MemberManager" /> class with the specified connection and clan manager
        ///     instance.
        /// </summary>
        /// <param name="connection">The connection, which must not be <c>null</c>.</param>
        /// <param name="clanManager">The clan manager, which must not be <c>null</c>.</param>
        public MemberManager([NotNull] IDbConnection connection, [NotNull] ClanManager clanManager)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _clanManager = clanManager ?? throw new ArgumentNullException(nameof(clanManager));

            _connection.Query("CREATE TABLE IF NOT EXISTS ClanMembers (" +
                              "Clan         TEXT, " +
                              "Rank         TEXT, " +
                              "Username     TEXT, " +
                              "UNIQUE(Username) ON CONFLICT REPLACE, " +
                              "FOREIGN KEY(Clan) REFERENCES Clans(Clan) ON DELETE CASCADE)");
        }

        /// <summary>
        ///     Disposes the member manager.
        /// </summary>
        public void Dispose()
        {
            _connection.Dispose();
        }

        /// <summary>
        ///     Creates a new <see cref="PlayerMetadata" /> object from the specified clan, rank and username and adds it to the
        ///     database.
        /// </summary>
        /// <param name="clan">The clan, which must not be <c>null</c>.</param>
        /// <param name="rank">The rank, which must not be <c>null</c>.</param>
        /// <param name="username">The username, which must not be <c>null</c>.</param>
        /// <returns>The metadata.</returns>
        public PlayerMetadata Add([NotNull] Clan clan, [NotNull] ClanRank rank, [NotNull] string username)
        {
            if (clan == null)
            {
                throw new ArgumentNullException(nameof(clan));
            }
            if (rank == null)
            {
                throw new ArgumentNullException(nameof(rank));
            }
            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            lock (_syncLock)
            {
                var metadata = new PlayerMetadata(clan, rank);

                clan.Members.Add(username);
                _metadataCache.Add(username, metadata);
                _connection.Query("INSERT INTO ClanMembers (Clan, Rank, Username) VALUES (@0, @1, @2)", clan.Name,
                    rank.Name, username);
                return metadata;
            }
        }

        /// <summary>
        ///     Gets player metadata for the specified user.
        /// </summary>
        /// <param name="username">The username, which must not be <c>null</c>.</param>
        /// <returns>The player metadata, or <c>null</c> if no match is found.</returns>
        public PlayerMetadata Get([NotNull] string username)
        {
            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            lock (_syncLock)
            {
                return _metadataCache.TryGetValue(username, out var playerMetadata)
                    ? playerMetadata
                    : default(PlayerMetadata);
            }
        }

        /// <summary>
        ///     Loads the members.
        /// </summary>
        public void Load()
        {
            lock (_syncLock)
            {
                _metadataCache.Clear();
                using (var reader = _connection.QueryReader("SELECT * FROM ClanMembers"))
                {
                    while (reader.Read())
                    {
                        var clanName = reader.Get<string>("Clan");
                        var rankName = reader.Get<string>("Rank");
                        var username = reader.Get<string>("Username");

                        var clan = _clanManager.Get(clanName);
                        if (clan == null) // Should never happen
                        {
                            continue;
                        }

                        var rank = clan.Owner == username
                            ? ClanRank.OwnerRank
                            : clan.Ranks.SingleOrDefault(r => r.Name == rankName) ?? ClanRank.DefaultRank;
                        clan.Members.Add(username);
                        _metadataCache.Add(username, new PlayerMetadata(clan, rank));
                    }
                }
            }
        }

        /// <summary>
        ///     Removes a player's information from the database.
        /// </summary>
        /// <param name="username">The player's username, which must not be <c>null</c>.</param>
        public void Remove([NotNull] string username)
        {
            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            lock (_syncLock)
            {
                _metadataCache.Remove(username);
                _connection.Query("DELETE FROM ClanMembers WHERE Username = @0", username);
            }
        }

        /// <summary>
        ///     Updates a player's database information.
        /// </summary>
        /// <param name="username">The player's account name, which must not be <c>null</c>.</param>
        /// <param name="rankName">The rank name, which must not be <c>null</c>.</param>
        public void Update([NotNull] string username, [NotNull] string rankName)
        {
            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }
            if (rankName == null)
            {
                throw new ArgumentNullException(nameof(rankName));
            }

            _connection.Query("UPDATE ClanMembers SET Rank = @0 WHERE Username = @1", rankName, username);
        }
    }
}