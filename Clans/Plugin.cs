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
using Terraria.DataStructures;
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

        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "clans.json");
        private readonly CommandRegistry _commandRegistry;

        private ClanManager _clanManager;
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
            if (File.Exists(ConfigPath))
            {
                ClansConfig.Instance = JsonConvert.DeserializeObject<ClansConfig>(File.ReadAllText(ConfigPath));
            }
            else
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(ClansConfig.Instance, Formatting.Indented));
            }

            var databaseConnection =
                new SqliteConnection($"uri=file://{Path.Combine(TShock.SavePath, "tshock.sqlite")},Version=3");
            (_clanManager = new ClanManager(databaseConnection)).Load();
            (_memberManager = new MemberManager(databaseConnection, _clanManager)).Load();
            _commandRegistry.RegisterCommands();

            GeneralHooks.ReloadEvent += OnReload;
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            ServerApi.Hooks.NetSendBytes.Register(this, OnNetSendBytes);
            ServerApi.Hooks.ServerChat.Register(this, OnServerChat);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(ClansConfig.Instance, Formatting.Indented));

                _clanManager.Dispose();
                _memberManager.Dispose();
                _commandRegistry.Dispose();

                GeneralHooks.ReloadEvent -= OnReload;
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                ServerApi.Hooks.NetSendBytes.Deregister(this, OnNetSendBytes);
                ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
            }

            base.Dispose(disposing);
        }

        private static void OnNetSendBytes(SendBytesEventArgs e)
        {
            var packetType = e.Buffer[2];
            var player = TShock.Players[e.Socket.Id];
            if (e.Handled)
            {
                return;
            }
            if (packetType != (int)PacketTypes.PlayerHurtV2)
            {
                return;
            }

            var playerMetadata = player?.GetData<PlayerMetadata>(DataKey);
            if (playerMetadata == null)
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
                if (dealerMetadata == null)
                {
                    return;
                }
                if (dealerMetadata.Clan.Name != playerMetadata.Clan.Name)
                {
                    return;
                }
                if (dealerMetadata.Clan.IsFriendlyFire)
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

        private static void OnReload(ReloadEventArgs e)
        {
            ClansConfig.Instance = JsonConvert.DeserializeObject<ClansConfig>(File.ReadAllText(ConfigPath));
        }

        private static void OnServerChat(ServerChatEventArgs e)
        {
            var player = TShock.Players[e.Who];
            var playerMetadata = player?.GetData<PlayerMetadata>(DataKey);
            if (e.Handled)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(ClansConfig.Instance.ChatFormat))
            {
                return;
            }
            if (player == null)
            {
                return;
            }
            if (!player.HasPermission(Permissions.canchat) || player.mute)
            {
                return;
            }
            if (playerMetadata == null)
            {
                return;
            }
            if (e.Text.StartsWith(TShock.Config.CommandSpecifier) ||
                e.Text.StartsWith(TShock.Config.CommandSilentSpecifier))
            {
                return;
            }

            var chatColor = playerMetadata.Clan.ChatColor.GetColor();
            if (!TShock.Config.EnableChatAboveHeads)
            {
                var message = string.Format(ClansConfig.Instance.ChatFormat, player.Name, player.Group.Name,
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

        [UsedImplicitly]
        [Command("c", permissions: "clans.use")]
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
            playerMetadata.Clan.SendMessage(string.Format(ClansConfig.Instance.ClanChatFormat, player.Name,
                playerMetadata.Rank.Tag, message));
        }

        [UsedImplicitly]
        [Command("clan", permissions: "clans.use")]
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
                    player.SendSuccessMessage($"You have joined clan '{metadata.Clan.Name}'!");
                    metadata.Clan.SendMessage($"(Clan) {player.User.Name} has joined the clan!", player.Index);
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
                    player.SendSuccessMessage($"Set clan chat color to '{parameters[1]}'.");
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
                    if (parameters.Count < 2)
                    {
                        player.SendErrorMessage(
                            $"Invalid syntax! Proper syntax: {TShock.Config.CommandSpecifier}clan create <clan name>");
                        return;
                    }

                    parameters.RemoveAt(0);
                    var clanName = string.Join(" ", parameters);
                    var clan = _clanManager.Get(clanName);
                    if (clan != null)
                    {
                        player.SendErrorMessage($"Clan '{clanName}' already exists.");
                        return;
                    }

                    clan = _clanManager.Add(clanName, player.User.Name);
                    var metadata = _memberManager.Add(clan, ClanRank.OwnerRank, player.User.Name);
                    player.SetData(DataKey, metadata);
                    player.SendSuccessMessage($"Successfully created clan '{clanName}'.");
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
                    player.SendSuccessMessage(
                        $"Friendly fire has been {(playerMetadata.Clan.IsFriendlyFire ? "en" : "dis")}abled!");
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
                    var match = matches[0];
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
                    var user = users[0];
                    var userMetadata = _memberManager.Get(user.Name);
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
                    TShock.Players.Single(p => p?.User.Name == user.Name)?.RemoveData(DataKey);
                    player.SendSuccessMessage($"'{user.Name}' has been kicked from the clan!");
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
                            player.SendErrorMessage("Your clan does not have a message of the day set.");
                            return;
                        }

                        player.SendInfoMessage("The clan's message of the day is:");
                        player.SendMessage(playerMetadata.Clan.Motd, playerMetadata.Clan.ChatColor.GetColor());
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
                        player.SendSuccessMessage($"The clan's message of the day has been set to '{newMotd}'.");
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
                    playerMetadata.Clan.Prefix = prefix;
                    _clanManager.Update(playerMetadata.Clan);
                    player.SendSuccessMessage($"Set clan prefix to '{prefix}'.");
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

                    var rankName = e.Parameters[2];
                    var rank = playerMetadata.Clan.Ranks.SingleOrDefault(r => r.Name == rankName);
                    var username = e.Parameters[1];
                    var users = TShock.Users.GetUsersByName(username);
                    var user = users[0];
                    var userMetadata = _memberManager.Get(user.Name);
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
                    if (userMetadata?.Clan.Name != playerMetadata.Clan.Name)
                    {
                        player.SendErrorMessage("This player is not in your clan!");
                        return;
                    }
                    if (rank == null)
                    {
                        player.SendErrorMessage($"Invalid rank '{rankName}'.");
                        return;
                    }

                    userMetadata.Rank = rank;
                    _memberManager.Update(user.Name, rank.Name);
                    player.SendSuccessMessage($"Set {user.Name}'s rank to '{rank.Name}'.");
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
        [Command("clanrank", permissions: "clans.use")]
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
                player.SendSuccessMessage($"Successfully created new rank '{rankName}'!");
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
                player.SendSuccessMessage($"Successfully removed rank '{rankName}'!");
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

                player.SendSuccessMessage($"Permissions for rank '{rankName}':");
                player.SendInfoMessage(string.Join(", ", rank.Permissions));
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

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs e)
        {
            var metadata = _memberManager.Get(e.Player.User.Name);
            if (metadata == null)
            {
                return;
            }

            e.Player.SetData(DataKey, metadata);
        }
    }
}