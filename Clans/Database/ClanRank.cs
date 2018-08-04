using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Clans.Database
{
    /// <summary>
    ///     Represents a clan rank.
    /// </summary>
    public sealed class ClanRank
    {
        /// <summary>
        ///     Gets the default clan rank.
        /// </summary>
        public static readonly ClanRank DefaultRank = new ClanRank("Default")
        {
            Permissions = {ClansPermissions.RankPermissionSendClanMessages}
        };

        /// <summary>
        ///     Gets the owner rank.
        /// </summary>
        public static readonly ClanRank OwnerRank = new ClanRank("Owner")
        {
            Permissions = {"*"}
        };

        /// <summary>
        ///     Initializes a new instance of the <see cref="ClanRank" /> class with the specified name and tag.
        /// </summary>
        /// <param name="name">The name, which must not be <c>null</c>.</param>
        /// <param name="tag">The tag, which defaults to rank name if none is provided.</param>
        public ClanRank([NotNull] string name, string tag = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Tag = tag ?? name;
        }

        /// <summary>
        ///     Gets or sets the name.
        /// </summary>
        [NotNull]
        public string Name { get; }

        /// <summary>
        ///     Gets the permissions.
        /// </summary>
        public IList<string> Permissions { get; } = new List<string>();

        /// <summary>
        ///     Gets or sets the tag.
        /// </summary>
        public string Tag { get; set; }

        /// <summary>
        ///     Determines if the rank has a permission.
        /// </summary>
        /// <param name="permission">The permission.</param>
        /// <returns><c>true</c> if the rank has the permission; otherwise, <c>false</c>.</returns>
        public bool HasPermission(string permission)
        {
            if (string.IsNullOrWhiteSpace(permission)) return true;

            if (Permissions.Contains("*") || Permissions.Contains("clans.ranks.*")) return true;

            return Permissions.Contains(permission);
        }
    }
}