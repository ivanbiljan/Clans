namespace Clans
{
    /// <summary>
    ///     Holds clan permissions.
    /// </summary>
    public static class ClanPermissions
    {
        /// <summary>
        ///     The permission required for creating clans.
        /// </summary>
        public static readonly string CreatePermission = "clans.create";

        /// <summary>
        ///     The permission required to mark ranks immune to kicks.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string ImmuneToKick = "clans.ranks.immune";

        /// <summary>
        ///     The permission required for inviting players.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string InvitePlayers = "clans.ranks.invite";

        /// <summary>
        ///     The permission required for kicking members.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string KickMembers = "clans.ranks.kick";

        /// <summary>
        ///     The root /clan and /c permission.
        /// </summary>
        public static readonly string RootPermission = "clans.use";

        /// <summary>
        ///     The permission required for sending clan messages.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string SendClanMessages = "clans.ranks.chat";

        /// <summary>
        ///     The permission required for changing clan colors.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string SetClanChatColor = "clans.ranks.setcolor";

        /// <summary>
        ///     The permission required for changing clan prefixes.
        /// </summary>
        /// <remarks>This permission aplies to clan ranks only.</remarks>
        public static readonly string SetClanPrefix = "clans.ranks.setprefix";

        /// <summary>
        ///     The permission required for setting a member's rank.
        /// </summary>
        /// <remarks>This permission applies to clan ranks only.</remarks>
        public static readonly string SetMemberRank = "clans.ranks.setrank";
    }
}