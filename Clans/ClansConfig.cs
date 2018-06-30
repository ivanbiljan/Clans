using JetBrains.Annotations;
using Newtonsoft.Json;

namespace Clans
{
    /// <summary>
    ///     Represents the Clans plugin configuration file.
    /// </summary>
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public sealed class ClansConfig
    {
        /// <summary>
        ///     Gets a value indicating whether clan colors will be displayed in game chat.
        /// </summary>
        [JsonProperty(Order = 4)]
        public bool ChatColorsEnabled { get; } = true;

        /// <summary>
        ///     Gets the in-game chat format.
        ///     {0} = player name, {1} = group name, {2} = group prefix,
        ///     {3} = group suffix, {4} = clan prefix, {5} = message
        /// </summary>
        [JsonProperty(Order = 2)]
        public string ChatFormat { get; } = "({4}) {0}: {5}";

        /// <summary>
        ///     Gets the clan chat format.
        ///     {0} = player name, {1} = rank tag, {2} = message
        /// </summary>
        [JsonProperty(Order = 3)]
        public string ClanChatFormat { get; } = "(Clan) ({1}) {0}: {2}";

        /// <summary>
        ///     Gets the Clans plugin configuration file instance.
        /// </summary>
        [NotNull]
        public static ClansConfig Instance { get; internal set; } = new ClansConfig();

        /// <summary>
        ///     Gets the maximum name length.
        /// </summary>
        [JsonProperty(Order = 0)]
        public int MaximumNameLength { get; } = 15;

        /// <summary>
        ///     Gets the maximum prefix length.
        /// </summary>
        [JsonProperty(Order = 1)]
        public int MaximumPrefixLength { get; } = 15;

        /// <summary>
        ///     Gets a value indicating whether permissions can be defined at a clan level.
        /// </summary>
        public bool ToggleClanPermissions { get; }
    }
}