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
        ///     Initializes a new instance of the <see cref="CommandAttribute" /> class with the specified permission, description
        ///     and aliases.
        /// </summary>
        /// <param name="permission">The permission.</param>
        /// <param name="description">The description.</param>
        /// <param name="names">The names.</param>
        public CommandAttribute(string permission, string description, params string[] names)
        {
            Permission = permission;
            Description = description;
            Names = names;
        }

        /// <summary>
        ///     Gets the description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        ///     Gets the names.
        /// </summary>
        public string[] Names { get; }

        /// <summary>
        ///     Gets the permission required for executing the command.
        /// </summary>
        public string Permission { get; }
    }
}