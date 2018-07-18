namespace Clans
{
    /// <summary>
    ///     Holds clans permissions.
    /// </summary>
    public static class ClansPermissions
    {
        /// <summary>
        ///     The permission required for teleporting to a clan's base.
        /// </summary>
        public static readonly string PluginPermissionClanTeleport = "clans.teleport";

        /// <summary>
        ///     The permission required for creating clans.
        /// </summary>
        public static readonly string PluginPermissionCreatePermission = "clans.create";

        /// <summary>
        ///     The root /clan and /c permission.
        /// </summary>
        public static readonly string PluginPermissionRootPermission = "clans.use";

        /// <summary>
        ///     The permission required for banning players.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionBanPlayers = "clans.ranks.ban";

        /// <summary>
        ///     The permission required for teleporting to a clan's base.
        /// </summary>
        /// <remarks>This permission applies to clan ranks.</remarks>
        public static readonly string RankPermissionClanTeleport = "clans.ranks.teleport";

        /// <summary>
        ///     The permission required to mark ranks immune to kicks.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionImmuneToKick = "clans.ranks.immune";

        /// <summary>
        ///     The permission required for inviting players.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionInvitePlayers = "clans.ranks.invite";

        /// <summary>
        ///     The permission required for kicking members.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionKickMembers = "clans.ranks.kick";

        /// <summary>
        ///     The permission required for sending clan messages.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionSendClanMessages = "clans.ranks.chat";

        /// <summary>
        ///     The permission required for setting a clan's base.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionSetClanBase = "clans.ranks.setbase";

        /// <summary>
        ///     The permission required for changing clan colors.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionSetClanChatColor = "clans.ranks.setcolor";

        /// <summary>
        ///     The permissions required for chaning the clan's message of the day.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionSetClanMotd = "clans.ranks.setmotd";

        /// <summary>
        ///     The permission required for changing clan prefixes.
        /// </summary>
        /// <remarks>This permission aplies to clan ranks only.</remarks>
        public static readonly string RankPermissionSetClanPrefix = "clans.ranks.setprefix";

        /// <summary>
        ///     The permission required for setting a member's rank.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionSetMemberRank = "clans.ranks.setrank";

        /// <summary>
        ///     The permission required for toggling a clan's friendly fire status.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionToggleFriendlyFire = "clans.ranks.togglefriendlyfire";

        /// <summary>
        ///     The permission required for toggling the clan's 'Private' flag.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string RankPermissionTogglePrivateStatus = "clans.ranks.toggleprivate";
    }
}