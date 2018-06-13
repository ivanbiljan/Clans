using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TerrariaApi.Server;
using TShockAPI;

namespace Clans.Commands
{
    /// <summary>
    ///     Represents a command registry.
    /// </summary>
    internal sealed class CommandRegistry : IDisposable
    {
        private readonly List<Command> _commands = new List<Command>();
        private readonly TerrariaPlugin _plugin;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CommandRegistry" /> class with the specified
        ///     <see cref="TerrariaPlugin" /> registrator.
        /// </summary>
        /// <param name="registrator">The registrator.</param>
        public CommandRegistry(TerrariaPlugin registrator)
        {
            _plugin = registrator;
        }

        /// <summary>
        ///     Disposes the command registry.
        /// </summary>
        public void Dispose()
        {
            foreach (var command in _commands)
            {
                TShockAPI.Commands.ChatCommands.RemoveAll(c => c.CommandDelegate == command.CommandDelegate);
            }

            _commands.Clear();
        }

        /// <summary>
        ///     Registers commands across the assembly.
        /// </summary>
        public void RegisterCommands()
        {
            var commandTypes = from type in Assembly.GetExecutingAssembly().GetExportedTypes()
                where !type.IsAbstract
                select type;
            foreach (var type in commandTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    var commandAttribute = method.GetCustomAttribute<CommandAttribute>();
                    if (commandAttribute == null)
                    {
                        continue;
                    }

                    var commandDelegate =
                        (CommandDelegate) Delegate.CreateDelegate(typeof(CommandDelegate), _plugin, method);
                    var command =
                        new Command(commandAttribute.Permissions.ToList(), commandDelegate, commandAttribute.Name)
                        {
                            HelpText = commandAttribute.Description
                        };

                    _commands.Add(command);
                    TShockAPI.Commands.ChatCommands.Add(command);
                }
            }
        }
    }
}