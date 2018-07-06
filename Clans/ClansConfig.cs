using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace Clans
{
    /// <summary>
    ///     Represents the Clans plugin configuration file.
    /// </summary>
    public sealed class ClansConfig
    {
        private static readonly string ConfigPath = Path.Combine(TShock.SavePath, "clans.json");

        /// <summary>
        ///     Gets a value indicating whether clan colors will be displayed in game chat.
        /// </summary>
        public bool ChatColorsEnabled { get; private set; } = true;

        /// <summary>
        ///     Gets the in-game chat format.
        ///     {0} = player name, {1} = group name, {2} = group prefix,
        ///     {3} = group suffix, {4} = clan prefix, {5} = message
        /// </summary>
        public string ChatFormat { get; private set; } = "({4}) {0}: {5}";

        /// <summary>
        ///     Gets the clan chat format.
        ///     {0} = player name, {1} = rank tag, {2} = message
        /// </summary>
        public string ClanChatFormat { get; private set; } = "(Clan) ({1}) {0}: {2}";

        /// <summary>
        ///     Gets the maximum name length.
        /// </summary>
        public int MaximumNameLength { get; private set; } = 15;

        /// <summary>
        ///     Gets the maximum prefix length.
        /// </summary>
        public int MaximumPrefixLength { get; private set; } = 15;

        /// <summary>
        ///     Gets a value indicating whether permissions can be defined at a clan level.
        /// </summary>
        public bool ToggleClanPermissions { get; private set; } = true;

        /// <summary>
        ///     Gets a value indicating whether friendly fire can be toggled off.
        /// </summary>
        public bool ToggleFriendlyFire { get; private set; }

        /// <summary>
        ///     Loads the configuration file.
        /// </summary>
        public void Load()
        {
            if (!File.Exists(ConfigPath))
            {
                Save();
            }
            else
            {
                var serializer = new JsonSerializer {Formatting = Formatting.Indented};
                using (var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(stream))
                {
                    var config = serializer.Deserialize(reader, typeof(ClansConfig)) as ClansConfig;
                    Debug.Assert(config != null, "config != null");

                    ChatColorsEnabled = config.ChatColorsEnabled;
                    ChatFormat = config.ChatFormat;
                    ClanChatFormat = config.ClanChatFormat;
                    MaximumNameLength = config.MaximumNameLength;
                    MaximumPrefixLength = config.MaximumPrefixLength;
                    ToggleClanPermissions = config.ToggleClanPermissions;
                    ToggleFriendlyFire = config.ToggleFriendlyFire;
                }
            }
        }

        /// <summary>
        ///     Saves the contents of the configuration file.
        /// </summary>
        public void Save()
        {
            var serializer = new JsonSerializer {Formatting = Formatting.Indented};
            using (var stream = new FileStream(ConfigPath, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
            {
                serializer.Serialize(writer, this);
            }
        }
    }
}