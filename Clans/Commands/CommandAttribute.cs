using System;

namespace Clans.Commands
{
    /// <summary>
    ///     Describes a command.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class CommandAttribute : Attribute
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="CommandAttribute" /> class with the specified name, description and
        ///     permissions.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="description">The description.</param>
        /// <param name="permissions">The permissions.</param>
        public CommandAttribute(string name, string description = null, params string[] permissions)
        {
            Name = name;
            Description = description ?? "N/A";
            Permissions = permissions;
        }

        /// <summary>
        ///     Gets the description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        ///     Gets the name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Gets the permissions.
        /// </summary>
        public string[] Permissions { get; }
    }
}
