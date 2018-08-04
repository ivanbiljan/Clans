using System.Diagnostics.CodeAnalysis;

namespace Clans
{
    /// <summary>
    ///     Represents the Clans plugin configuration file.
    /// </summary>
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class ClansConfig
    {
        /// <summary>
        ///     Gets a value indicating whether clan colors will be displayed in game chat.
        /// </summary>
        public bool ChatColorsEnabled { get; set; } = true;

        /// <summary>
        ///     Gets the in-game chat format.
        ///     {0} = player name, {1} = group name, {2} = group prefix,
        ///     {3} = group suffix, {4} = clan prefix, {5} = message
        /// </summary>
        public string ChatFormat { get; set; } = "({4}) {0}: {5}";

        /// <summary>
        ///     Gets the clan chat format.
        ///     {0} = player name, {1} = rank tag, {2} = message
        /// </summary>
        public string ClanChatFormat { get; set; } = "(Clan) ({1}) {0}: {2}";

        /// <summary>
        ///     Gets the clan limit.
        /// </summary>
        public int ClanLimit { get; set; } = 25;

        /// <summary>
        ///     Gets the maximum name length.
        /// </summary>
        public int MaximumNameLength { get; set; } = 15;

        /// <summary>
        ///     Gets the maximum prefix length.
        /// </summary>
        public int MaximumPrefixLength { get; set; } = 15;

        /// <summary>
        ///     Gets a value indicating whether permissions can be defined at a clan level.
        /// </summary>
        public bool ToggleClanPermissions { get; set; } = false;

        /// <summary>
        ///     Gets a value indicating whether friendly fire can be toggled off.
        /// </summary>
        public bool ToggleFriendlyFire { get; set; } = false;
    }
}