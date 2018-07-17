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
            {
                _configuration = JsonConvert.DeserializeObject<ClansConfig>(File.ReadAllText(ConfigPath));
            }

            var databaseConnection =
                new SqliteConnection($"uri=file://{Path.Combine("clans", "database.sqlite")},Version=3");
            (_clanManager = new ClanManager(databaseConnection)).Load();
            (_memberManager = new MemberManager(databaseConnection, _clanManager)).Load();
            _commandRegistry.RegisterCommands();

            GeneralHooks.ReloadEvent += OnReload;
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
                PlayerHooks.PlayerPermission -= OnPlayerPermission;
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                ServerApi.Hooks.NetSendBytes.Deregister(this, OnNetSendBytes);
                ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
            }

            base.Dispose(disposing);
        }

        [UsedImplicitly]
        [Command("clans.admin", "Allows taking administrative actions over clans.", "aclan")]
        private void AdminClanCommand(CommandArgs e)
        {
            var parameters = e.Parameters;
            var player = e.Player;
            if (parameters.Count < 1)
            {
                player.SendErrorMessage("Invalid syntax! Proper syntax:");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}aclan addperm <clan name> <permissions...>");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}aclan delperm <clan name> <permissions...>");
                player.SendErrorMessage($"{TShock.Config.CommandSpecifier}aclan listperm <clan name>");
                return;
            }

            var subcommand = parameters[0].ToLowerInvariant();
            if (subcommand.Equals("addperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 3)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}aclan addperm <clan name> <permissions>");
                    return;
                }

                var clanName = parameters[1];
                var clan = _clanManager.Get(clanName);
                if (clan == null)
                {
                    player.SendErrorMessage($"Invalid clan '{clanName}'.");
                    return;
                }

                parameters.RemoveRange(0, 2);
                parameters.ForEach(p => clan.Permissions.Add(p));
                _clanManager.Update(clan);
                player.SendSuccessMessage($"Clan '{clanName}' has been modified successfully.");
            }
            else if (subcommand.Equals("delperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 3)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}aclan delperm <clan name> <permissions>");
                    return;
                }

                var clanName = parameters[1];
                var clan = _clanManager.Get(clanName);
                if (clan == null)
                {
                    player.SendErrorMessage($"Invalid clan '{clanName}'.");
                    return;
                }

                parameters.RemoveRange(0, 2);
                parameters.ForEach(p => clan.Permissions.Remove(p));
                _clanManager.Update(clan);
                player.SendSuccessMessage($"Clan '{clanName}' has been modified successfully.");
            }
            else if (subcommand.Equals("listperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 2)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}aclan listperm <clan name>");
                    return;
                }

                parameters.RemoveAt(0);
                var clanName = string.Join(" ", parameters);
                var clan = _clanManager.Get(clanName);
                if (clan == null)
                {
                    player.SendErrorMessage($"Invalid clan '{clanName}'.");
                    return;
                }

                player.SendInfoMessage(
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
                player.SendErrorMessage("You are not in a clan!");
                return;
            }
            if (!playerMetadata.Rank.HasPermission(ClansPermissions.SendClanMessages))
            {
                player.SendErrorMessage("You do not have permission to use the clan chat!");
                return;
            }
            if (player.mute)
            {
                player.SendErrorMessage("You are muted!");
                return;
            }
            if (parameters.Count < 1)
            {
                player.SendErrorMessage($"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}c <message>");
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
                player.SendErrorMessage($"Invalid syntax! Use {TShock.Config.CommandSpecifier}clan help for help.");
                return;
            }

            switch (parameters[0].ToLowerInvariant())
            {
                case "accept":
                {
                    if (playerMetadata != null)
                    {
                        player.SendErrorMessage("You are already in a clan!");
                        return;
                    }
                    if (invitationData == null)
                    {
                        player.SendErrorMessage("You do not have a pending invitation.");
                        return;
                    }

                    var metadata = _memberManager.Add(invitationData, ClanRank.DefaultRank, player.User.Name);
                    player.RemoveData(InvitationKey);
                    player.SetData(DataKey, metadata);
                    player.SendInfoMessage($"You have joined clan '{metadata.Clan.Name}'!");
                    metadata.Clan.SendMessage($"(Clan) {player.User.Name} has joined the clan!", player.Index);
                }
                    break;
                case "ban":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.BanPlayers))
                    {
                        player.SendErrorMessage("You do not have permission to ban players!");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan ban <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var username = string.Join(" ", parameters);
                    var players = TShock.Users.GetUsersByName(username);
                    if (players.Count == 0)
                    {
                        player.SendErrorMessage($"Invalid player '{username}'.");
                        return;
                    }
                    if (players.Count > 1)
                    {
                        TShock.Utils.SendMultipleMatchError(player, players.Select(p => p.Name));
                        return;
                    }
                    if (playerMetadata.Clan.BannedUsers.Contains(players[0].Name))
                    {
                        player.SendInfoMessage($"Player '{players[0].Name}' is already banned.");
                        return;
                    }

                    var targetPlayer = TShock.Players.Single(p => p?.User?.Name == players[0].Name);
                    var targetMetadata = targetPlayer?.GetData<PlayerMetadata>(DataKey);
                    if (targetMetadata?.Clan.Name == playerMetadata.Clan.Name &&
                        targetMetadata.Rank.HasPermission(ClansPermissions.ImmuneToKick))
                    {
                        player.SendErrorMessage("You cannot ban this player!");
                        return;
                    }

                    playerMetadata.Clan.BannedUsers.Add(players[0].Name);
                    _clanManager.Update(playerMetadata.Clan);
                    targetPlayer.RemoveData(DataKey);
                    targetPlayer.SendInfoMessage("You have been kicked from the clan!");
                    player.SendInfoMessage($"{players[0].Name} is now banned from the clan.");
                }
                    break;
                case "color":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.SetClanChatColor))
                    {
                        player.SendErrorMessage("You do not have permission to change the clan's chat color.");
                        return;
                    }
                    if (parameters.Count != 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan color <rrr,ggg,bbb>");
                        return;
                    }

                    var colorString = parameters[1].Split(',');
                    if (colorString.Length != 3 || !byte.TryParse(colorString[0], out var _) ||
                        !byte.TryParse(colorString[1], out var _) || !byte.TryParse(colorString[2], out var _))
                    {
                        player.SendErrorMessage("Invalid color format.");
                        return;
                    }

                    playerMetadata.Clan.ChatColor = parameters[1];
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendInfoMessage($"Set clan chat color to '{parameters[1]}'.");
                }
                    break;
                case "create":
                {
                    if (!player.IsLoggedIn)
                    {
                        player.SendErrorMessage("You must be logged in to do that.");
                        return;
                    }
                    if (!player.HasPermission(ClansPermissions.CreatePermission))
                    {
                        player.SendErrorMessage("You do not have permission to create clans.");
                        return;
                    }
                    if (playerMetadata != null)
                    {
                        player.SendErrorMessage("You are already in a clan!");
                        return;
                    }
                    if (_clanManager.GetAll().Count == _configuration.ClanLimit)
                    {
                        player.SendErrorMessage("The clan limit has been reached.");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan create <clan name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var clanName = string.Join(" ", parameters);
                    if (clanName.Length > _configuration.MaximumNameLength)
                    {
                        player.SendErrorMessage(
                            $"Clan name must not be longer than {_configuration.MaximumNameLength} characters.");
                        return;
                    }

                    var clan = _clanManager.Get(clanName);
                    if (clan != null)
                    {
                        player.SendErrorMessage($"Clan '{clanName}' already exists.");
                        return;
                    }

                    clan = _clanManager.Add(clanName, player.User.Name);
                    var metadata = _memberManager.Add(clan, ClanRank.OwnerRank, player.User.Name);
                    player.SetData(DataKey, metadata);
                    player.SendInfoMessage($"You have created clan '{clanName}'.");
                    if (!e.Silent)
                    {
                        TSPlayer.All.SendInfoMessage($"Clan '{clanName}' has been established!");
                    }
                }
                    break;
                case "deny":
                case "decline":
                {
                    if (playerMetadata != null)
                    {
                        player.SendErrorMessage("You are already in a clan!");
                        return;
                    }
                    if (invitationData == null)
                    {
                        player.SendErrorMessage("You do not have a pending invitation.");
                        return;
                    }

                    player.RemoveData(InvitationKey);
                    player.SendSuccessMessage("You have declined the invitation.");
                }
                    break;
                case "disband":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (playerMetadata.Clan.Owner != player.User.Name)
                    {
                        player.SendErrorMessage("You are not the clan's owner!");
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

                    TSPlayer.All.SendInfoMessage($"Clan '{playerMetadata.Clan.Name}' has been disbanded!");
                }
                    break;
                case "ff":
                case "friendlyfire":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.ToggleFriendlyFire))
                    {
                        player.SendErrorMessage(
                            "You do not have permission to change the clan's friendly fire status!");
                        return;
                    }

                    playerMetadata.Clan.IsFriendlyFire = !playerMetadata.Clan.IsFriendlyFire;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendInfoMessage(
                        $"Friendly fire is now {(playerMetadata.Clan.IsFriendlyFire ? "ON" : "OFF")}.");
                }
                    break;
                case "help":
                {
                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendErrorMessage("Invalid page number!");
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
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.InvitePlayers))
                    {
                        player.SendErrorMessage("You do not have permission to invite players.");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan invite <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var playerName = string.Join(" ", parameters);
                    var matches = TShock.Utils.FindPlayer(playerName);
                    if (matches.Count == 0)
                    {
                        player.SendErrorMessage("Invalid player!");
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
                        player.SendErrorMessage("The player is not logged in.");
                        return;
                    }
                    if (match.GetData<PlayerMetadata>(DataKey) != null)
                    {
                        player.SendErrorMessage("This player is already in a clan!");
                        return;
                    }
                    if (match.GetData<Clan>(InvitationKey) != null)
                    {
                        player.SendErrorMessage("This player already has a pending invitation.");
                        return;
                    }

                    match.SetData(InvitationKey, playerMetadata.Clan);
                    match.SendInfoMessage($"You have been invited to join clan '{playerMetadata.Clan.Name}'!");
                    match.SendInfoMessage(
                        $"Type {TShock.Config.CommandSpecifier}clan accept to accept the invitation.");
                    match.SendInfoMessage(
                        $"Type {TShock.Config.CommandSpecifier}clan decline to decline the invitation.");
                    player.SendSuccessMessage($"'{match.Name}' has been invited to join your clan!");
                }
                    break;
                case "join":
                {
                    if (!player.IsLoggedIn)
                    {
                        player.SendErrorMessage("You must be logged in to do that.");
                        return;
                    }
                    if (playerMetadata != null)
                    {
                        player.SendErrorMessage("You are already in a clan!");
                        return;
                    }
                    if (invitationData != null)
                    {
                        player.SendInfoMessage(
                            "You have a pending clan invitation. In order to join a clan you must first decline the current invitation.");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan join <clan name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var clanName = string.Join(" ", parameters);
                    var clan = _clanManager.Get(clanName);
                    if (clan == null)
                    {
                        player.SendErrorMessage($"Invalid clan '{clanName}'.");
                        return;
                    }
                    if (clan.IsPrivate)
                    {
                        player.SendInfoMessage("This clan is set to invite-only.");
                        return;
                    }
                    if (clan.BannedUsers.Contains(player.User.Name))
                    {
                        player.SendInfoMessage("You have been banned from this clan.");
                        return;
                    }

                    _memberManager.Add(clan, ClanRank.DefaultRank, player.User.Name);
                    clan.SendMessage($"(Clan) {player.User.Name} has joined the clan!", player.Index);
                    player.SendInfoMessage($"You have joined clan '{clan.Name}'!");
                }
                    break;
                case "kick":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.KickMembers))
                    {
                        player.SendErrorMessage("You do not have permission to kick members!");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan kick <player name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var username = string.Join(" ", parameters);
                    var users = TShock.Users.GetUsersByName(username);
                    if (users.Count == 0)
                    {
                        player.SendErrorMessage("Invalid player!");
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
                        player.SendErrorMessage("This player is not in your clan!");
                        return;
                    }
                    if (userMetadata.Rank.HasPermission(ClansPermissions.ImmuneToKick))
                    {
                        player.SendErrorMessage("You cannot kick this player!");
                        return;
                    }

                    _memberManager.Remove(user.Name);
                    var kickedPlayer = TShock.Players.Single(p => p?.User?.Name == user.Name);
                    if (kickedPlayer != null)
                    {
                        kickedPlayer.RemoveData(DataKey);
                        kickedPlayer.SendInfoMessage("You have been kicked from the clan!");
                    }

                    player.SendInfoMessage($"'{user.Name}' has been kicked from the clan!");
                }
                    break;
                case "list":
                {
                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendErrorMessage("Invalid page number!");
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
                case "members":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!PaginationTools.TryParsePageNumber(parameters, 1, player, out pageNumber))
                    {
                        player.SendErrorMessage("Invalid page number!");
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
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        if (string.IsNullOrWhiteSpace(playerMetadata.Clan.Motd))
                        {
                            player.SendInfoMessage("Your clan does not have a message of the day set.");
                            return;
                        }

                        player.SendMessage(
                            $"[Clan '{playerMetadata.Clan.Name}' Message of the Day] {playerMetadata.Clan.Motd}",
                            playerMetadata.Clan.ChatColor.GetColor());
                    }
                    else
                    {
                        if (!playerMetadata.Rank.HasPermission(ClansPermissions.SetClanMotd))
                        {
                            player.SendErrorMessage(
                                "You do not have permission to change the clan's message of the day!");
                            return;
                        }

                        parameters.RemoveAt(0);
                        var newMotd = string.Join(" ", parameters);
                        playerMetadata.Clan.Motd = newMotd;
                        _clanManager.Update(playerMetadata.Clan);
                        player.SendInfoMessage($"The clan's message of the day has been set to '{newMotd}'.");
                    }
                    break;
                }
                case "prefix":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.SetClanPrefix))
                    {
                        player.SendErrorMessage("You do not have permission to change the clan's prefix.");
                        return;
                    }
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan prefix <prefix>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var prefix = string.Join(" ", parameters);
                    if (prefix.Length > _configuration.MaximumPrefixLength)
                    {
                        player.SendErrorMessage(
                            $"Clan prefix must not be longer than {_configuration.MaximumPrefixLength} characters.");
                        return;
                    }

                    playerMetadata.Clan.Prefix = prefix;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendInfoMessage($"Set clan prefix to '{prefix}'.");
                }
                    break;
                case "private":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.TogglePrivateStatus))
                    {
                        player.SendErrorMessage("You do not have permission to change the clan's private flag!");
                        return;
                    }

                    playerMetadata.Clan.IsPrivate = !playerMetadata.Clan.IsPrivate;
                    player.SendInfoMessage(
                        $"The clan is {(playerMetadata.Clan.IsPrivate ? "now" : "no longer")} private.");
                }
                    break;
                case "setrank":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (!playerMetadata.Rank.HasPermission(ClansPermissions.SetMemberRank))
                    {
                        player.SendErrorMessage("You do not have permission to modify ranks!");
                        return;
                    }
                    if (parameters.Count != 3)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan setrank <player name> <rank>");
                        return;
                    }

                    var username = e.Parameters[1];
                    var users = TShock.Users.GetUsersByName(username);
                    if (users.Count == 0)
                    {
                        player.SendErrorMessage("Invalid player!");
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
                        player.SendErrorMessage("This player is not in your clan!");
                        return;
                    }

                    var rankName = e.Parameters[2];
                    var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                    if (rank == null)
                    {
                        player.SendErrorMessage($"Invalid rank '{rankName}'.");
                        return;
                    }

                    userMetadata.Rank = rank;
                    _memberManager.Update(user.Name, rank.Name);
                    player.SendInfoMessage($"Set {user.Name}'s rank to '{rank.Name}'.");
                }
                    break;
                case "quit":
                case "leave":
                {
                    if (playerMetadata == null)
                    {
                        player.SendErrorMessage("You are not in a clan!");
                        return;
                    }
                    if (playerMetadata.Clan.Owner == player.User.Name)
                    {
                        goto case "disband";
                    }

                    player.RemoveData(DataKey);
                    player.SendSuccessMessage("You have left the clan!");
                }
                    break;
                default:
                    player.SendErrorMessage(
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
                player.SendErrorMessage("You are not in a clan!");
                return;
            }
            if (playerMetadata.Clan.Owner != player.User.Name)
            {
                player.SendErrorMessage("You are not the clan's owner!");
                return;
            }

            var command = parameters[0];
            parameters.RemoveAt(0);
            if (command.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank add <rank name>");
                    return;
                }

                var rankName = string.Join(" ", parameters);
                if (playerMetadata.Clan.Ranks.Any(r => r.Name == rankName))
                {
                    player.SendErrorMessage($"Rank '{rankName}' already exists.");
                    return;
                }

                playerMetadata.Clan.Ranks.Add(new ClanRank(rankName));
                _clanManager.Update(playerMetadata.Clan);
                player.SendInfoMessage($"You have created a new rank '{rankName}'.");
            }
            else if (command.Equals("addperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank addperm <rank name> <permissions>");
                    return;
                }

                var rankName = parameters[0];
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                parameters.RemoveAt(0);
                parameters.ForEach(p => rank.Permissions.Add(p));
                _clanManager.Update(playerMetadata.Clan);
                player.SendSuccessMessage($"Rank '{rankName}' has been modified successfully.");
            }
            else if (command.Equals("del", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank del <rank name>");
                    return;
                }

                var rankName = string.Join(" ", parameters);
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendErrorMessage($"Rank '{rankName}' does not exist.");
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
                player.SendInfoMessage($"You have deleted rank '{rankName}'.");
            }
            else if (command.Equals("delperm", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank delperm <rank name> <permissions>");
                    return;
                }

                var rankName = parameters[0];
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                parameters.RemoveAt(0);
                parameters.ForEach(p => rank.Permissions.Remove(p));
                _clanManager.Update(playerMetadata.Clan);
                player.SendSuccessMessage($"Rank '{rankName}' has been modified successfully.");
            }
            else if (command.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                var ranks = string.Join(", ", playerMetadata.Clan.Ranks.Select(r => r.Name));
                player.SendInfoMessage($"Ranks: {(ranks.Length > 0 ? ranks : "none")}");
            }
            else if (command.Equals("permissions", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 1)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank permissions <rank name>");
                    return;
                }

                var rankName = string.Join(" ", parameters);
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                var permissions = string.Join(", ", rank.Permissions);
                player.SendInfoMessage(
                    $"Permissions for rank '{rankName}': {(permissions.Length > 0 ? permissions : "none")}");
            }
            else if (command.Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                if (parameters.Count < 2)
                {
                    player.SendErrorMessage(
                        $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clanrank tag <rank name> <tag>");
                    return;
                }

                var rankName = parameters[0];
                var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                if (rank == null)
                {
                    player.SendErrorMessage($"Rank '{rankName}' does not exist.");
                    return;
                }

                parameters.RemoveAt(0);
                rank.Tag = string.Join(" ", parameters);
                player.SendSuccessMessage($"Rank '{rankName}' has been modified successfully.");
            }
        }

        private void OnNetSendBytes(SendBytesEventArgs e)
        {
            if (!_configuration.ToggleFriendlyFire)
            {
                return;
            }

            var packetType = e.Buffer[2];
            if (e.Handled && packetType == (int) PacketTypes.PlayerHurtV2)
            {
                return;
            }

            var playerMetadata = TShock.Players[e.Socket.Id]?.GetData<PlayerMetadata>(DataKey);
            if (playerMetadata == null || playerMetadata.Clan.IsFriendlyFire)
            {
                return;
            }

            var dealerIndex = -1;
            var playerDeathReason = (BitsByte) e.Buffer[4];
            if (playerDeathReason[0])
            {
                dealerIndex = e.Buffer[5];
            }
            if (dealerIndex != -1)
            {
                var dealer = TShock.Players[dealerIndex];
                var dealerMetadata = dealer?.GetData<PlayerMetadata>(DataKey);
                if (dealerMetadata?.Clan.Name != playerMetadata.Clan.Name)
                {
                    return;
                }

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
            if (!_configuration.ToggleClanPermissions)
            {
                return;
            }

            var clan = e.Player.GetData<PlayerMetadata>(DataKey)?.Clan;
            if (clan == null)
            {
                return;
            }

            e.Handled = clan.Permissions.Contains(e.Permission);
        }

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var metadata = _memberManager.Get(e.Player.User.Name);
            if (metadata == null)
            {
                return;
            }

            e.Player.SetData(DataKey, metadata);
        }

        private void OnReload(ReloadEventArgs e)
        {
            _configuration = JsonConvert.DeserializeObject<ClansConfig>(File.ReadAllText(ConfigPath));
            e.Player.SendSuccessMessage("Clans configuration file reloaded!");
        }

        private void OnServerChat(ServerChatEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(_configuration.ChatFormat))
            {
                return;
            }
            if (e.Text.StartsWith(TShock.Config.CommandSpecifier) ||
                e.Text.StartsWith(TShock.Config.CommandSilentSpecifier))
            {
                return;
            }

            var player = TShock.Players[e.Who];
            var playerMetadata = player?.GetData<PlayerMetadata>(DataKey);
            if (playerMetadata == null)
            {
                return;
            }
            if (!player.HasPermission(Permissions.canchat) || player.mute)
            {
                return;
            }

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