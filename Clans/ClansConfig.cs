using JetBrains.Annotations;

namespace Clans
{
    /// <summary>
    ///     Represents the Clans plugin configuration file.
    /// </summary>
    public sealed class ClansConfig
    {
        /// <summary>
        ///     Gets the in-game chat format.
        ///     {0} = player name, {1} = group prefix, {2} = group suffix,
        ///     {3} = clan prefix, {4} = message
        /// </summary>
        public string ChatFormat { get; } = "({3}) {0}: {4}";

        /// <summary>
        ///     Gets the clan chat format.
        ///     {0} = player name, {1} = player rank, {2} = message
        /// </summary>
        public string ClanChatFormat { get; } = "(Clan) ({1}) {0}: {2}";

        /// <summary>
        ///     Gets or sets the Clans plugin configuration file instance.
        /// </summary>
        [NotNull]
        public static ClansConfig Instance { get; internal set; } = new ClansConfig();

        /// <summary>
        ///     Gets the value indicating whether permissions can be defined at a clan level.
        /// </summary>
        public bool ToggleClanPermissions { get; } = false;
    }
}