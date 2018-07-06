using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using JetBrains.Annotations;
using Mono.Data.Sqlite;
using TShockAPI.DB;

namespace Clans.Database
{
    /// <summary>
    ///     Represents a clan database manager.
    /// </summary>
    public sealed class ClanManager : IDisposable
    {
        private readonly List<Clan> _clans = new List<Clan>();
        private readonly IDbConnection _connection;
        private readonly object _syncLock = new object();

        /// <summary>
        ///     Initializes a new instance of the <see cref="ClanManager" /> class with the specified database connection.
        /// </summary>
        /// <param name="connection">The connection, which must not be <c>null</c>.</param>
        public ClanManager([NotNull] IDbConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));

            _connection.Query("CREATE TABLE IF NOT EXISTS Clans (" +
                              "Clan             TEXT PRIMARY KEY, " +
                              "Owner            TEXT, " +
                              "Prefix           TEXT, " +
                              "ChatColor        TEXT, " +
                              "Motd             TEXT, " +
                              "IsFriendlyFire   INTEGER, " +
                              "UNIQUE(Clan) ON CONFLICT REPLACE)");
            _connection.Query("CREATE TABLE IF NOT EXISTS ClanHasPermission (" +
                              "Clan             TEXT, " +
                              "Permission       TEXT, " +
                              "FOREIGN KEY(Clan) REFERENCES Clans(Clan) ON DELETE CASCADE)");
            _connection.Query("CREATE TABLE IF NOT EXISTS ClanRanks (" +
                              "Clan             TEXT, " +
                              "Rank             TEXT, " +
                              "Tag              TEXT, " +
                              "FOREIGN KEY(Clan) REFERENCES Clans(Clan) ON DELETE CASCADE)");
            _connection.Query("CREATE TABLE IF NOT EXISTS ClanRankHasPermission (" +
                              "Clan             TEXT, " +
                              "Rank             TEXT, " +
                              "Permission       TEXT, " +
                              "FOREIGN KEY(Clan, Rank) REFERENCES ClanRanks(Clan, Rank) ON DELETE CASCADE)");
        }

        /// <summary>
        ///     Disposes the clan manager.
        /// </summary>
        public void Dispose()
        {
            _connection.Dispose();
        }

        /// <summary>
        ///     Adds a new clan.
        /// </summary>
        /// <param name="name">The clan's name, which must not be <c>null</c>.</param>
        /// <param name="owner">The clan's owner, which must not be <c>null</c>.</param>
        /// <returns>The clan.</returns>
        public Clan Add([NotNull] string name, [NotNull] string owner)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            lock (_syncLock)
            {
                var clan = new Clan(name, owner);
                _clans.Add(clan);
                _connection.Query(
                    "INSERT INTO Clans (Clan, Owner, Prefix, ChatColor, Motd, IsFriendlyFire) VALUES (@0, @1, @2, @3, @4, @5)",
                    clan.Name, clan.Owner, clan.Prefix, clan.ChatColor, clan.Motd, clan.IsFriendlyFire ? 1 : 0);
                return clan;
            }
        }

        /// <summary>
        ///     Gets a clan by name match.
        /// </summary>
        /// <param name="clanName">The clan's name, which must not be <c>null</c>.</param>
        /// <returns>The clan, or <c>null</c> if no match is found.</returns>
        public Clan Get([NotNull] string clanName)
        {
            if (clanName == null)
            {
                throw new ArgumentNullException(nameof(clanName));
            }

            lock (_syncLock)
            {
                return _clans.SingleOrDefault(c => c.Name == clanName);
            }
        }

        /// <summary>
        ///     Returns a read-only collection of all clans.
        /// </summary>
        /// <returns>A read-only collection of all clans.</returns>
        public IReadOnlyCollection<Clan> GetAll()
        {
            lock (_syncLock)
            {
                return _clans.AsReadOnly();
            }
        }

        /// <summary>
        ///     Loads the clans.
        /// </summary>
        public void Load()
        {
            lock (_syncLock)
            {
                _clans.Clear();
                using (var reader = _connection.QueryReader("SELECT * FROM Clans"))
                {
                    while (reader.Read())
                    {
                        var name = reader.Get<string>("Clan");
                        var owner = reader.Get<string>("Owner");
                        var prefix = reader.Get<string>("Prefix");
                        var chatColor = reader.Get<string>("ChatColor");
                        var motd = reader.Get<string>("Motd");
                        var isFriendlyFire = reader.Get<int>("IsFriendlyFire") == 1;

                        var clan = new Clan(name, owner)
                        {
                            Prefix = prefix,
                            ChatColor = chatColor,
                            Motd = motd,
                            IsFriendlyFire = isFriendlyFire
                        };
                        using (var reader2 =
                            _connection.QueryReader("SELECT Permission FROM ClanHasPermission WHERE Clan = @0", name))
                        {
                            while (reader2.Read())
                            {
                                var permission = reader2.Get<string>("Permission");
                                clan.Permissions.Add(permission);
                            }
                        }
                        using (var reader2 =
                            _connection.QueryReader("SELECT Rank, Tag FROM ClanRanks WHERE Clan = @0", name))
                        {
                            while (reader2.Read())
                            {
                                var rank = reader2.Get<string>("Rank");
                                var tag = reader2.Get<string>("Tag");
                                var clanRank = new ClanRank(rank, tag);

                                using (var reader3 = _connection.QueryReader(
                                    "SELECT Permission FROM ClanRankHasPermission WHERE Clan = @0 AND Rank = @1", clan,
                                    rank))
                                {
                                    while (reader3.Read())
                                    {
                                        var permission = reader3.Get<string>("Permission");
                                        clanRank.Permissions.Add(permission);
                                    }
                                }

                                clan.Ranks.Add(clanRank);
                            }
                        }

                        _clans.Add(clan);
                    }
                }
            }
        }

        /// <summary>
        ///     Removes a clan.
        /// </summary>
        /// <param name="name">The clan's name, which must not be <c>null</c>.</param>
        public void Remove([NotNull] string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            lock (_syncLock)
            {
                _clans.RemoveAll(c => c.Name == name);
                _connection.Query("DELETE FROM Clans WHERE Clan = @0", name);
            }
        }

        /// <summary>
        ///     Updates a clan.
        /// </summary>
        /// <param name="clan">The clan, which must not be <c>null</c>.</param>
        public void Update([NotNull] Clan clan)
        {
            if (clan == null)
            {
                throw new ArgumentNullException(nameof(clan));
            }

            _connection.Query(
                "UPDATE Clans SET Prefix = @0, ChatColor = @1, Motd = @2, IsFriendlyFire = @3 WHERE Clan = @4",
                clan.Prefix, clan.ChatColor, clan.Motd, clan.IsFriendlyFire ? 1 : 0, clan.Name);
            _connection.Query("DELETE FROM ClanHasPermission WHERE Clan = @0", clan.Name);
            _connection.Query("DELETE FROM ClanRanks WHERE Clan = @0", clan.Name);
            _connection.Query("DELETE FROM ClanRankHasPermission WHERE Clan = @0", clan.Name);
            using (var dbConnection = _connection.CloneEx())
            {
                dbConnection.Open();
                using (var transaction = dbConnection.BeginTransaction())
                {
                    using (var command = (SqliteCommand) dbConnection.CreateCommand())
                    {
                        command.CommandText = "INSERT INTO ClanHasPermission (Clan, Permission) VALUES (@0, @1)";
                        command.AddParameter("@0", clan.Name);
                        command.AddParameter("@1", null);

                        foreach (var permission in clan.Permissions)
                        {
                            command.Parameters["@1"].Value = permission;
                            command.ExecuteNonQuery();
                        }
                    }

                    foreach (var rank in clan.Ranks)
                    {
                        using (var command = (SqliteCommand) dbConnection.CreateCommand())
                        {
                            command.CommandText = "INSERT INTO ClanRanks (Clan, Rank, Tag) VALUES (@0, @1, @2)";
                            command.AddParameter("@0", clan.Name);
                            command.AddParameter("@1", rank.Name);
                            command.AddParameter("@2", rank.Tag);
                            command.ExecuteNonQuery();

                            foreach (var permission in rank.Permissions)
                            {
                                command.CommandText =
                                    "INSERT INTO ClanRankHasPermission (Clan, Rank, Permission) VALUES (@0, @1, @2)";
                                command.Parameters["@2"].Value = permission;
                                command.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                }
            }
        }
    }
}