using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Clans.Commands;
using Clans.Database;
using Clans.Extensions;
using JetBrains.Annotations;
using Mono.Data.Sqlite;
using Newtonsoft.Json;
using Terraria;
using Terraria.GameContent.NetModules;
using Terraria.Localization;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace Clans
{
    /// <summary>
    ///     Represents the Clans plugin.
    /// </summary>
    [ApiVersion(2, 1)]
    public sealed class Plugin : TerrariaPlugin
    {
        private const string DataKey = "Clans_PlayerMetadata";
        private const string InvitationKey = "Clans_Invitation";

        private static readonly string ConfigPath = Path.Combine("clans", "config.json");
        private readonly CommandRegistry _commandRegistry;

        private ClanManager _clanManager;
        private ClansConfig _configuration = new ClansConfig();
        private MemberManager _memberManager;

        /// <inheritdoc />
        public Plugin(Main game) : base(game)
        {
            _commandRegistry = new CommandRegistry(this);
        }

        /// <inheritdoc />
        public override string Author => "Ivan";

        /// <inheritdoc />
        public override string Description => "In-game clan system.";

        /// <inheritdoc />
        public override string Name => "Clans";

        /// <inheritdoc />
        public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;

        /// <inheritdoc />
        public override void Initialize()
        {
            Directory.CreateDirectory("clans");
            if (File.Exists(ConfigPath))
                _configuration = JsonConvert.DeserializeObject<ClansConfig>(File.ReadAllText(ConfigPath));

            var databaseConnection =
                new SqliteConnection($"uri=file://{Path.Combine("clans", "database.sqlite")},Version=3");
            (_clanManager = new ClanManager(databaseConnection)).Load();
            (_memberManager = new MemberManager(databaseConnection, _clanManager)).Load();
            _commandRegistry.RegisterCommands();

            GeneralHooks.ReloadEvent += OnReload;
            PlayerHooks.PlayerLogout += OnPlayerLogout;
            PlayerHooks.PlayerPermission += OnPlayerPermission;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            ServerApi.Hooks.NetSendBytes.Register(this, OnNetSendBytes);
            ServerApi.Hooks.ServerChat.Register(this, OnServerChat);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_configuration, Formatting.Indented));

                _clanManager.Dispose();
                _memberManager.Dispose();
                _commandRegistry.Dispose();

                GeneralHooks.ReloadEvent -= OnReload;
                PlayerHooks.PlayerLogout -= OnPlayerLogout;
                PlayerHooks.PlayerPermission -= OnPlayerPermission;
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                ServerApi.Hooks.NetSendBytes.Deregister(this, OnNetSendBytes);
                ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
            }

            base.Dispose(disposing);
        }

        private static void OnPlayerLogout(PlayerLogoutEventArgs e)
        {
            e.Player.RemoveData(DataKey);
        }

        [UsedImplicitly]
        [Command("clans.admin", "Allows taking administrative actions over clans.", "aclan")]
        private void AdminClanCommand(CommandArgs e)
        {
            var parameters = e.Parameters;
            var player = e.Player;
            if (parameters.Count < 1)
            {
                player.SendClansErrorMessage("Invalid syntax! Proper syntax:");
                player.SendClansErrorMessage($"{TShock.Config.CommandSpecifier}aclan addperm <clan name> <permissions...>");
                player.SendClansErrorMessage($"{TShock.Config.CommandSpecifier}aclan delperm <clan name> <permissions...>");
                player.SendClansErrorMessage($"{TShock.Config.CommandSpecifier}aclan listperm <clan name>");
                return;
            }

            var subcommand = parameters[0].ToLowerInvariant();
            if (subcommand.Equals("addperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 3)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}aclan addperm <clan name> <permissions>");
                    return;
                }

                var clanName = parameters[1];
                var clan = _clanManager.Get(clanName);
                if (clan == null)
                {
                    player.SendClansErrorMessage($"Invalid clan '{clanName}'.");
                    return;
                }

                parameters.RemoveRange(0, 2);
                parameters.ForEach(p => clan.Permissions.Add(p));
                _clanManager.Update(clan);
                player.SendClansSuccessMessage($"Clan '{clanName}' has been modified successfully.");
            }
            else if (subcommand.Equals("delperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 3)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}aclan delperm <clan name> <permissions>");
                    return;
                }

                var clanName = parameters[1];
                var clan = _clanManager.Get(clanName);
                if (clan == null)
                {
                    player.SendClansErrorMessage($"Invalid clan '{clanName}'.");
                    return;
                }

                parameters.RemoveRange(0, 2);
                parameters.ForEach(p => clan.Permissions.Remove(p));
                _clanManager.Update(clan);
                player.SendClansSuccessMessage($"Clan '{clanName}' has been modified successfully.");
            }
            else if (subcommand.Equals("listperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 2)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}aclan listperm <clan name>");
                    return;
                }

                parameters.RemoveAt(0);
                var clanName = string.Join(" ", parameters);
                var clan = _clanManager.Get(clanName);
                if (clan == null)
                {
                    player.SendClansErrorMessage($"Invalid clan '{clanName}'.");
                    return;
                }

                player.SendClansInfoMessage(
                    $"Permissions for clan '{clan.Name}': {(clan.Permissions.Any() ? string.Join(", ", clan.Permissions) : "none")}");
            }
        }

        [UsedImplicitly]
        [Command("clans.use", "Allows clan communication.", "c", "csay")]
        private void ClanChatCommand(CommandArgs e)
        {
            var parameters = e.Parameters;
            var player = e.Player;
            var playerMetadata = player.GetData<PlayerMetadata>(DataKey);
            if (playerMetadata == null)
            {
                player.SendClansErrorMessage("You are not in a clan!");
                return;
            }

            if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionSendClanMessages))
            {
                player.SendClansErrorMessage("You do not have permission to use the clan chat!");
                return;
            }

            if (player.mute)
            {
                player.SendClansErrorMessage("You are muted!");
                return;
            }

            if (parameters.Count < 1)
            {
                player.SendClansErrorMessage($"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}c <message>");
                return;
            }

            var message = string.Join(" ", parameters);
            playerMetadata.Clan.SendMessage(string.Format(_configuration.ClanChatFormat, player.Name,
                playerMetadata.Rank.Tag, message));
        }

        [UsedImplicitly]
        [Command("clans.use", "The main clans command.", "clan")]
        private void ClanCommand(CommandArgs e)
        {
            int pageNumber;
            var parameters = e.Parameters;
            var player = e.Player;
            var invitationData = player.GetData<Clan>(InvitationKey);
            var playerMetadata = player.GetData<PlayerMetadata>(DataKey);
            if (parameters.Count < 1)
            {
                player.SendClansErrorMessage($"Invalid syntax! Use {TShock.Config.CommandSpecifier}clan help for help.");
                return;
            }

            switch (parameters[0].ToLowerInvariant())
            {
                case "accept":
                {
                    if (playerMetadata != null)
                    {
                        player.SendClansErrorMessage("You are already in a clan!");
                        return;
                    }

                    if (invitationData == null)
                    {
                        player.SendClansErrorMessage("You do not have a pending invitation.");
                        return;
                    }

                    var metadata = _memberManager.Add(invitationData, ClanRank.DefaultRank, player.User.Name);
                    player.RemoveData(InvitationKey);
                    player.SetData(DataKey, metadata);
                    player.SendClansInfoMessage($"You have joined clan '{metadata.Clan.Name}'!");
                    metadata.Clan.SendMessage($"(Clan) {player.User.Name} has joined the clan!", player.Index);
                }
                    break;
                case "ban":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionBanPlayers))
                    {
                        player.SendClansErrorMessage("You do not have permission to ban players!");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan ban <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var username = string.Join(" ", parameters);
                    var players = TShock.Users.GetUsersByName(username);
                    if (players.Count == 0)
                    {
                        player.SendClansErrorMessage($"Invalid player '{username}'.");
                        return;
                    }

                    if (players.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(player, players.Select(p => p.Name));
                        return;
                    }

                    if (playerMetadata.Clan.BannedUsers.Contains(players[0].Name))
                    {
                        player.SendClansInfoMessage($"Player '{players[0].Name}' is already banned.");
                        return;
                    }

                    var targetMetadata = _memberManager.Get(players[0].Name);
                    if (targetMetadata != null && targetMetadata.Clan.Name == playerMetadata.Clan.Name)
                        if (targetMetadata.Rank.HasPermission(ClansPermissions.RankPermissionImmuneToKick) &&
                            playerMetadata.Clan.Owner != player.User.Name)
                        {
                            player.SendClansErrorMessage("You cannot ban this player!");
                            return;
                        }

                    playerMetadata.Clan.Members.Remove(players[0].Name);
                    playerMetadata.Clan.BannedUsers.Add(players[0].Name);
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage($"{players[0].Name} is now banned from the clan.");

                    var targetPlayer = TShock.Players.SingleOrDefault(p => p?.User?.Name == players[0].Name);
                    if (targetPlayer != null)
                    {
                        targetPlayer.RemoveData(DataKey);
                        targetPlayer.SendClansInfoMessage("You have been banned from the clan!");
                    }
                }
                    break;
                case "color":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionSetClanChatColor))
                    {
                        player.SendClansErrorMessage("You do not have permission to change the clan's chat color.");
                        return;
                    }

                    if (parameters.Count != 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan color <rrr,ggg,bbb>");
                        return;
                    }

                    var colorString = parameters[1].Split(',');
                    if (colorString.Length != 3 || !byte.TryParse(colorString[0], out _) ||
                        !byte.TryParse(colorString[1], out _) || !byte.TryParse(colorString[2], out _))
                    {
                        player.SendClansErrorMessage("Invalid color format.");
                        return;
                    }

                    playerMetadata.Clan.ChatColor = parameters[1];
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage($"Set clan chat color to '{parameters[1]}'.");
                }
                    break;
                case "create":
                {
                    if (!player.IsLoggedIn)
                    {
                        player.SendClansErrorMessage("You must be logged in to do that.");
                        return;
                    }

                    if (!player.HasPermission(ClansPermissions.PluginPermissionCreatePermission))
                    {
                        player.SendClansErrorMessage("You do not have permission to create clans.");
                        return;
                    }

                    if (playerMetadata != null)
                    {
                        player.SendClansErrorMessage("You are already in a clan!");
                        return;
                    }

                    if (_clanManager.GetAll().Count == _configuration.ClanLimit)
                    {
                        player.SendClansErrorMessage("The clan limit has been reached.");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan create <clan name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var clanName = string.Join(" ", parameters);
                    if (clanName.Length > _configuration.MaximumNameLength)
                    {
                        player.SendClansErrorMessage(
                            $"Clan name must not be longer than {_configuration.MaximumNameLength} characters.");
                        return;
                    }

                    var clan = _clanManager.Get(clanName);
                    if (clan != null)
                    {
                        player.SendClansErrorMessage($"Clan '{clanName}' already exists.");
                        return;
                    }

                    clan = _clanManager.Add(clanName, player.User.Name);
                    var metadata = _memberManager.Add(clan, ClanRank.OwnerRank, player.User.Name);
                    player.SetData(DataKey, metadata);
                    player.SendClansInfoMessage($"You have created clan '{clanName}'.");
                    if (!e.Silent) TSPlayer.All.SendClansInfoMessage($"Clan '{clanName}' has been established!");
                }
                    break;
                case "deny":
                case "decline":
                {
                    if (playerMetadata != null)
                    {
                        player.SendClansErrorMessage("You are already in a clan!");
                        return;
                    }

                    if (invitationData == null)
                    {
                        player.SendClansErrorMessage("You do not have a pending invitation.");
                        return;
                    }

                    player.RemoveData(InvitationKey);
                    player.SendClansSuccessMessage("You have declined the invitation.");
                }
                    break;
                case "disband":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (playerMetadata.Clan.Owner != player.User.Name)
                    {
                        player.SendClansErrorMessage("You are not the clan's owner!");
                        return;
                    }

                    _clanManager.Remove(playerMetadata.Clan.Name);
                    var players = from plr in TShock.Players
                        where plr != null && plr.IsLoggedIn
                        let metadata = plr.GetData<PlayerMetadata>(DataKey)
                        where metadata != null && metadata.Clan.Name == playerMetadata.Clan.Name
                        select plr;
                    foreach (var player2 in players)
                    {
                        player2.RemoveData(DataKey);
                        _memberManager.Remove(player2.User.Name);
                    }

                    TSPlayer.All.SendClansInfoMessage($"Clan '{playerMetadata.Clan.Name}' has been disbanded!");
                }
                    break;
                case "ff":
                case "friendlyfire":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionToggleFriendlyFire))
                    {
                        player.SendClansErrorMessage(
                            "You do not have permission to change the clan's friendly fire status!");
                        return;
                    }

                    playerMetadata.Clan.IsFriendlyFire = !playerMetadata.Clan.IsFriendlyFire;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage(
                        $"Friendly fire is now {(playerMetadata.Clan.IsFriendlyFire ? "ON" : "OFF")}.");
                }
                    break;
                case "help":
                {
                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendClansErrorMessage("Invalid page number!");
                        return;
                    }

                    var help = new List<string>
                    {
                        "create <clan name> - creates a new clan with the specified name",
                        "disband - disbands a clan",
                        "quit - leaves a clan",
                        "friendlyfire - toggles friendly fire",
                        "private - toggles invite-only mode",
                        "join - joins a clan",
                        "invite <player name> - invites a player to join the clan",
                        "accept - accepts a clan invitation",
                        "decline = declines a clan invitation",
                        "setrank <player name> <rank name> - sets a player's rank",
                        "kick <player name> - kicks a player from the clan",
                        "prefix <new prefix> - sets a clan's prefix",
                        "color <rrr,ggg,bbb> - sets a clan's chat color",
                        "motd <new MOTD> - sets a clan's message of the day",
                        "members - lists a clan's members",
                        "list - lists all clans"
                    };

                    PaginationTools.SendPage(player, pageNumber, help, new PaginationTools.Settings
                    {
                        HeaderFormat = "Clan Sub-commands ({0}/{1})",
                        FooterFormat = $"Type {TShock.Config.CommandSpecifier}clan help {{0}} for more."
                    });
                }
                    break;
                case "invite":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionInvitePlayers))
                    {
                        player.SendClansErrorMessage("You do not have permission to invite players.");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan invite <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var playerName = string.Join(" ", parameters);
                    var matches = TShock.Utils.FindPlayer(playerName);
                    if (matches.Count == 0)
                    {
                        player.SendClansErrorMessage("Invalid player!");
                        return;
                    }

                    if (matches.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(player, matches.Select(p => p.Name));
                        return;
                    }

                    var match = matches[0];
                    if (!match.IsLoggedIn)
                    {
                        player.SendClansErrorMessage("The player is not logged in.");
                        return;
                    }

                    if (match.GetData<PlayerMetadata>(DataKey) != null)
                    {
                        player.SendClansErrorMessage("This player is already in a clan!");
                        return;
                    }

                    if (match.GetData<Clan>(InvitationKey) != null)
                    {
                        player.SendClansErrorMessage("This player already has a pending invitation.");
                        return;
                    }

                    if (playerMetadata.Clan.BannedUsers.Contains(match.User.Name))
                    {
                        player.SendClansErrorMessage("This player is banned from the clan.");
                        return;
                    }

                    match.SetData(InvitationKey, playerMetadata.Clan);
                    match.SendClansInfoMessage($"You have been invited to join clan '{playerMetadata.Clan.Name}'!");
                    match.SendClansInfoMessage(
                        $"Type {TShock.Config.CommandSpecifier}clan accept to accept the invitation.");
                    match.SendClansInfoMessage(
                        $"Type {TShock.Config.CommandSpecifier}clan decline to decline the invitation.");
                    player.SendClansSuccessMessage($"'{match.Name}' has been invited to join your clan!");
                }
                    break;
                case "join":
                {
                    if (!player.IsLoggedIn)
                    {
                        player.SendClansErrorMessage("You must be logged in to do that.");
                        return;
                    }

                    if (playerMetadata != null)
                    {
                        player.SendClansErrorMessage("You are already in a clan!");
                        return;
                    }

                    if (invitationData != null)
                    {
                        player.SendClansInfoMessage(
                            "You have a pending clan invitation. In order to join a clan you must first decline the current invitation.");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan join <clan name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var clanName = string.Join(" ", parameters);
                    var clan = _clanManager.Get(clanName);
                    if (clan == null)
                    {
                        player.SendClansErrorMessage($"Invalid clan '{clanName}'.");
                        return;
                    }

                    if (clan.BannedUsers.Contains(player.User.Name))
                    {
                        player.SendClansInfoMessage("You have been banned from this clan.");
                        return;
                    }

                    if (clan.IsPrivate)
                    {
                        player.SendClansInfoMessage("This clan is set to invite-only.");
                        return;
                    }

                    playerMetadata = _memberManager.Add(clan, ClanRank.DefaultRank, player.User.Name);
                    player.SetData(DataKey, playerMetadata);
                    player.SendClansInfoMessage($"You have joined clan '{clan.Name}'!");
                    clan.SendMessage($"(Clan) {player.User.Name} has joined the clan!", player.Index);
                }
                    break;
                case "kick":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionKickMembers))
                    {
                        player.SendClansErrorMessage("You do not have permission to kick members!");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan kick <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var username = string.Join(" ", parameters);
                    var users = TShock.Users.GetUsersByName(username);
                    if (users.Count == 0)
                    {
                        player.SendClansErrorMessage("Invalid player!");
                        return;
                    }

                    if (users.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(player, users.Select(u => u.Name));
                        return;
                    }

                    var user = users[0];
                    var userMetadata = _memberManager.Get(user.Name);
                    if (userMetadata?.Clan.Name != playerMetadata.Clan.Name)
                    {
                        player.SendClansErrorMessage("This player is not in your clan!");
                        return;
                    }

                    if (userMetadata.Rank.HasPermission(ClansPermissions.RankPermissionImmuneToKick) &&
                        playerMetadata.Clan.Owner != player.User.Name)
                    {
                        player.SendClansErrorMessage("You cannot kick this player!");
                        return;
                    }

                    playerMetadata.Clan.Members.Remove(user.Name);
                    _memberManager.Remove(user.Name);
                    var kickedPlayer = TShock.Players.Single(p => p?.User?.Name == user.Name);
                    if (kickedPlayer != null)
                    {
                        kickedPlayer.RemoveData(DataKey);
                        kickedPlayer.SendClansInfoMessage("You have been kicked from the clan!");
                    }

                    player.SendClansInfoMessage($"'{user.Name}' has been kicked from the clan!");
                }
                    break;
                case "list":
                {
                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendClansErrorMessage("Invalid page number!");
                        return;
                    }

                    var clanList = PaginationTools.BuildLinesFromTerms(_clanManager.GetAll().Select(c => c.Name));
                    PaginationTools.SendPage(player, pageNumber, clanList, new PaginationTools.Settings
                    {
                        HeaderFormat = "Clan List ({0}/{1})",
                        FooterFormat = $"Type {TShock.Config.CommandSpecifier}clan list {{0}} for more.",
                        NothingToDisplayString = "There are no clans to list."
                    });
                }
                    break;
                case "listban":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendClansErrorMessage("Invalid page number!");
                        return;
                    }

                    var bans = PaginationTools.BuildLinesFromTerms(playerMetadata.Clan.BannedUsers);
                    PaginationTools.SendPage(player, pageNumber, bans, new PaginationTools.Settings
                    {
                        HeaderFormat = "Clan Bans ({0}/{1})",
                        FooterFormat = $"Type {TShock.Config.CommandSpecifier}clan listban {{0}} for more.",
                        NothingToDisplayString = "There are no bans to list."
                    });
                }
                    break;
                case "members":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendClansErrorMessage("Invalid page number!");
                        return;
                    }

                    var memberList = PaginationTools.BuildLinesFromTerms(playerMetadata.Clan.Members);
                    PaginationTools.SendPage(player, pageNumber, memberList, new PaginationTools.Settings
                    {
                        HeaderFormat = "Clan Members ({0}/{1})",
                        FooterFormat = $"Type {TShock.Config.CommandSpecifier}clan members {{0}} for more."
                    });
                }
                    break;
                case "motd":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        if (string.IsNullOrWhiteSpace(playerMetadata.Clan.Motd))
                        {
                            player.SendClansInfoMessage("Your clan does not have a message of the day set.");
                            return;
                        }

                        player.SendMessage(
                            $"[Clan '{playerMetadata.Clan.Name}' Message of the Day] {playerMetadata.Clan.Motd}",
                            playerMetadata.Clan.ChatColor.GetColor());
                    }
                    else
                    {
                        if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionSetClanMotd))
                        {
                            player.SendClansErrorMessage(
                                "You do not have permission to change the clan's message of the day!");
                            return;
                        }

                        parameters.RemoveAt(0);
                        var newMotd = string.Join(" ", parameters);
                        playerMetadata.Clan.Motd = newMotd;
                        _clanManager.Update(playerMetadata.Clan);
                        player.SendClansInfoMessage($"The clan's message of the day has been set to '{newMotd}'.");
                    }

                    break;
                }
                case "prefix":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionSetClanPrefix))
                    {
                        player.SendClansErrorMessage("You do not have permission to change the clan's prefix.");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan prefix <prefix>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var prefix = string.Join(" ", parameters);
                    if (prefix.Length > _configuration.MaximumPrefixLength)
                    {
                        player.SendClansErrorMessage(
                            $"Clan prefix must not be longer than {_configuration.MaximumPrefixLength} characters.");
                        return;
                    }

                    playerMetadata.Clan.Prefix = prefix;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage($"Set clan prefix to '{prefix}'.");
                }
                    break;
                case "private":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionTogglePrivateStatus))
                    {
                        player.SendClansErrorMessage("You do not have permission to change the clan's private flag!");
                        return;
                    }

                    playerMetadata.Clan.IsPrivate = !playerMetadata.Clan.IsPrivate;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage(
                        $"The clan is {(playerMetadata.Clan.IsPrivate ? "now" : "no longer")} private.");
                }
                    break;
                case "setbase":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionSetClanBase))
                    {
                        player.SendClansErrorMessage("You do not have permission to change the clan's base spawn point!");
                        return;
                    }

                    playerMetadata.Clan.BaseCoordinates = player.TPlayer.position;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage("Clan spawn point has been set to your position.");
                }
                    break;
                case "setrank":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionSetMemberRank))
                    {
                        player.SendClansErrorMessage("You do not have permission to modify ranks!");
                        return;
                    }

                    if (parameters.Count != 3)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan setrank <player name> <rank>");
                        return;
                    }

                    var username = e.Parameters[1];
                    var users = TShock.Users.GetUsersByName(username);
                    if (users.Count == 0)
                    {
                        player.SendClansErrorMessage("Invalid player!");
                        return;
                    }

                    if (users.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(player, users.Select(u => u.Name));
                        return;
                    }

                    var user = users[0];
                    var userMetadata = _memberManager.Get(user.Name);
                    if (userMetadata?.Clan.Name != playerMetadata.Clan.Name)
                    {
                        player.SendClansErrorMessage("This player is not in your clan!");
                        return;
                    }

                    var rankName = e.Parameters[2];
                    var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                    if (rank == null)
                    {
                        player.SendClansErrorMessage($"Invalid rank '{rankName}'.");
                        return;
                    }

                    userMetadata.Rank = rank;
                    _memberManager.Update(user.Name, rank.Name);
                    player.SendClansInfoMessage($"Set {user.Name}'s rank to '{rank.Name}'.");
                }
                    break;
                case "tp":
                case "teleport":
                {
                    if (!player.HasPermission(ClansPermissions.PluginPermissionClanTeleport))
                    {
                        player.SendClansErrorMessage("You do not have access to this command.");
                        return;
                    }

                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionClanTeleport))
                    {
                        player.SendClansErrorMessage("You do not have permission to teleport to the clan's base.");
                        return;
                    }

                    if (playerMetadata.Clan.BaseCoordinates == null)
                    {
                        player.SendClansErrorMessage("Your clan does not have a base set.");
                        return;
                    }

                    var location = playerMetadata.Clan.BaseCoordinates.Value;
                    player.Teleport(location.X, location.Y);
                    player.SendClansInfoMessage("You have been teleported to your clan's base spawn point.");
                }
                    break;
                case "unban":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.RankPermissionBanPlayers))
                    {
                        player.SendClansErrorMessage("You do not have permission to ban players!");
                        return;
                    }

                    if (parameters.Count < 2)
                    {
                        player.SendClansErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan unban <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var username = string.Join(" ", parameters);
                    var players = TShock.Users.GetUsersByName(username);
                    if (players.Count == 0)
                    {
                        player.SendClansErrorMessage($"Invalid player '{username}'.");
                        return;
                    }

                    if (players.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(player, players.Select(p => p.Name));
                        return;
                    }

                    if (!playerMetadata.Clan.BannedUsers.Contains(players[0].Name))
                    {
                        player.SendClansInfoMessage($"Player '{players[0].Name}' is not banned.");
                        return;
                    }

                    playerMetadata.Clan.BannedUsers.Remove(players[0].Name);
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendClansInfoMessage($"{players[0].Name} is no longer banned from the clan.");
                }
                    break;
                case "quit":
                case "leave":
                {
                    if (playerMetadata == null)
                    {
                        player.SendClansErrorMessage("You are not in a clan!");
                        return;
                    }

                    if (playerMetadata.Clan.Owner == player.User.Name) goto case "disband";

                    _memberManager.Remove(player.User.Name);
                    playerMetadata.Clan.Members.Remove(player.User.Name);
                    player.RemoveData(DataKey);
                    player.SendClansSuccessMessage("You have left the clan!");
                }
                    break;
                default:
                    player.SendClansErrorMessage(
                        $"Invalid sub-command! Type {TShock.Config.CommandSpecifier}clan help for a list of valid commands.");
                    break;
            }
        }

        [UsedImplicitly]
        [Command("clans.use", "Allows clan owners to manage ranks.", "clanrank")]
        private void ClanRankCommand(CommandArgs e)
        {
            var parameters = e.Parameters;
            var player = e.Player;
            var playerMetadata = player.GetData<PlayerMetadata>(DataKey);
            if (parameters.Count < 1)
            {
                player.SendErrorMessage("Invalid syntax! Proper syntax:");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}clanrank add <rank name>");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}clanrank del <rank name>");
                player.SendErrorMessage(
                    $"{TShock.Config.CommandSpecifier}clanrank addperm <rank name> <permissions...>");
                player.SendErrorMessage(
                    $"{TShock.Config.CommandSpecifier}clanrank delperm <rank name> <permissions...>");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}clanrank permissions [rank name]");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}clanrank list");
                return;
            }

            if (playerMetadata == null)
            {
                player.SendClansErrorMessage("You are not in a clan!");
                return;
            }

            if (playerMetadata.Clan.Owner != player.User.Name)
            {
                player.SendClansErrorMessage("You are not the clan's owner!");
                return;
            }

            var command = parameters[0];
            parameters.RemoveAt(0);
            if (command.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank add <rank name>");
                    return;
                }

                var rankName = string.Join(" ", parameters);
                if (playerMetadata.Clan.Ranks.Any(r => r.Name == rankName))
                {
                    player.SendClansErrorMessage($"Rank '{rankName}' already exists.");
                    return;
                }

                playerMetadata.Clan.Ranks.Add(new ClanRank(rankName));
                _clanManager.Update(playerMetadata.Clan);
                player.SendClansInfoMessage($"You have created a new rank '{rankName}'.");
            }
            else if (command.Equals("addperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank addperm <rank name> <permissions>");
                    return;
                }

                var rankName = parameters[0];
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendClansErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                parameters.RemoveAt(0);
                parameters.ForEach(p => rank.Permissions.Add(p));
                _clanManager.Update(playerMetadata.Clan);
                player.SendClansSuccessMessage($"Rank '{rankName}' has been modified successfully.");
            }
            else if (command.Equals("del", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank del <rank name>");
                    return;
                }

                var rankName = string.Join(" ", parameters);
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendClansErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                var players = from plr in TShock.Players
                    where plr != null && plr.IsLoggedIn
                    let metadata = plr.GetData<PlayerMetadata>(DataKey)
                    where metadata?.Clan.Name == playerMetadata.Clan.Name && metadata.Rank.Name == rankName
                    select plr;
                foreach (var player2 in players)
                {
                    player2.GetData<PlayerMetadata>(DataKey).Rank = ClanRank.DefaultRank;
                    _memberManager.Update(player2.User.Name, ClanRank.DefaultRank.Name);
                }

                playerMetadata.Clan.Ranks.Remove(rank);
                _clanManager.Update(playerMetadata.Clan);
                player.SendClansInfoMessage($"You have deleted rank '{rankName}'.");
            }
            else if (command.Equals("delperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank delperm <rank name> <permissions>");
                    return;
                }

                var rankName = parameters[0];
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendClansErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                parameters.RemoveAt(0);
                parameters.ForEach(p => rank.Permissions.Remove(p));
                _clanManager.Update(playerMetadata.Clan);
                player.SendClansSuccessMessage($"Rank '{rankName}' has been modified successfully.");
            }
            else if (command.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                var ranks = string.Join(", ", playerMetadata.Clan.Ranks.Select(r => r.Name));
                player.SendClansInfoMessage($"Ranks: {(ranks.Length > 0 ? ranks : "none")}");
            }
            else if (command.Equals("permissions", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank permissions <rank name>");
                    return;
                }

                var rankName = string.Join(" ", parameters);
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendClansErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                var permissions = string.Join(", ", rank.Permissions);
                player.SendClansInfoMessage(
                    $"Permissions for rank '{rankName}': {(permissions.Length > 0 ? permissions : "none")}");
            }
            else if (command.Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 2)
                {
                    player.SendClansErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank tag <rank name> <tag>");
                    return;
                }

                var rankName = parameters[0];
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendClansErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                parameters.RemoveAt(0);
                rank.Tag = string.Join(" ", parameters);
                player.SendClansSuccessMessage($"Rank '{rankName}' has been modified successfully.");
            }
        }

        private void OnNetSendBytes(SendBytesEventArgs e)
        {
            if (!_configuration.ToggleFriendlyFire) return;

            var packetType = e.Buffer[2];
            if (e.Handled && packetType == (int) PacketTypes.PlayerHurtV2) return;

            var playerMetadata = TShock.Players[e.Socket.Id]?.GetData<PlayerMetadata>(DataKey);
            if (playerMetadata == null || playerMetadata.Clan.IsFriendlyFire) return;

            var dealerIndex = -1;
            var playerDeathReason = (BitsByte) e.Buffer[4];
            if (playerDeathReason[0]) dealerIndex = e.Buffer[5];
            if (dealerIndex != -1)
            {
                var dealer = TShock.Players[dealerIndex];
                var dealerMetadata = dealer?.GetData<PlayerMetadata>(DataKey);
                if (dealerMetadata?.Clan.Name != playerMetadata.Clan.Name) return;

                using (var writer = new BinaryWriter(new MemoryStream(e.Buffer, 3, e.Count - 3)))
                {
                    writer.Write((byte) 255);
                    writer.Write((byte) 0);
                    writer.Write((short) 0);
                }
            }

            e.Handled = true;
        }

        private void OnPlayerPermission(PlayerPermissionEventArgs e)
        {
            if (!_configuration.ToggleClanPermissions) return;

            var clan = e.Player.GetData<PlayerMetadata>(DataKey)?.Clan;
            if (clan == null) return;

            e.Handled = clan.Permissions.Contains(e.Permission);
        }

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var metadata = _memberManager.Get(e.Player.User.Name);
            if (metadata == null) return;

            e.Player.SetData(DataKey, metadata);
        }

        private void OnReload(ReloadEventArgs e)
        {
            _configuration = JsonConvert.DeserializeObject<ClansConfig>(File.ReadAllText(ConfigPath));
            e.Player.SendClansSuccessMessage("Clans configuration file reloaded!");
        }

        private void OnServerChat(ServerChatEventArgs e)
        {
            if (e.Handled) return;
            if (string.IsNullOrWhiteSpace(_configuration.ChatFormat)) return;
            if (e.Text.StartsWith(TShock.Config.CommandSpecifier) ||
                e.Text.StartsWith(TShock.Config.CommandSilentSpecifier))
                return;

            var player = TShock.Players[e.Who];
            var playerMetadata = player?.GetData<PlayerMetadata>(DataKey);
            if (playerMetadata == null) return;
            if (!player.HasPermission(Permissions.canchat) || player.mute) return;

            var chatColor = _configuration.ChatColorsEnabled
                ? playerMetadata.Clan.ChatColor.GetColor()
                : player.Group.ChatColor.GetColor();
            if (!TShock.Config.EnableChatAboveHeads)
            {
                var message = string.Format(_configuration.ChatFormat, player.Name, player.Group.Name,
                    player.Group.Prefix, player.Group.Suffix, playerMetadata.Clan.Prefix, e.Text);
                TSPlayer.All.SendMessage(message, chatColor);
                TSPlayer.Server.SendMessage(message, chatColor);
                TShock.Log.Info($"Broadcast: {message}");
            }
            else
            {
                var playerName = player.TPlayer.name;
                player.TPlayer.name = string.Format(TShock.Config.ChatAboveHeadsFormat, player.Group.Name,
                    playerMetadata.Clan.Prefix, player.Name, player.Group.Suffix);
                NetMessage.SendData((int) PacketTypes.PlayerInfo, -1, -1, NetworkText.FromLiteral(player.TPlayer.name),
                    e.Who);

                player.TPlayer.name = playerName;
                var packet =
                    NetTextModule.SerializeServerMessage(NetworkText.FromLiteral(e.Text), chatColor, (byte) e.Who);
                NetManager.Instance.Broadcast(packet, e.Who);
                NetMessage.SendData((int) PacketTypes.PlayerInfo, -1, -1, NetworkText.FromLiteral(playerName), e.Who);

                var msg =
                    $"<{string.Format(TShock.Config.ChatAboveHeadsFormat, player.Group.Name, playerMetadata.Clan.Prefix, player.Name, player.Group.Suffix)}> {e.Text}";
                player.SendMessage(msg, chatColor);
                TSPlayer.Server.SendMessage(msg, chatColor);
                TShock.Log.Info($"Broadcast: {msg}");
            }

            e.Handled = true;
        }
    }
}