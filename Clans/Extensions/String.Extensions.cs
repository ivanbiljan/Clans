using System;
using Microsoft.Xna.Framework;

namespace Clans.Extensions
{
    /// <summary>
    ///     Provides extension methods for the <see cref="string" /> type.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        ///     Returns a color from the specified string.
        /// </summary>
        /// <param name="source">The string.</param>
        /// <returns>The corresponding color.</returns>
        public static Color GetColor(this string source)
        {
            if (source == null) return new Color(255, 255, 255);

            var array = source.Split(',');
            if (array.Length != 3) throw new ArgumentException("Expected rrr,ggg,bbb format.", nameof(source));
            if (!byte.TryParse(array[0], out var r))
                throw new ArgumentException("The color provided was not in the correct format.", nameof(source));

            if (!byte.TryParse(array[1], out var g))
                throw new ArgumentException("The color provided was not in the correct format.", nameof(source));

            if (!byte.TryParse(array[2], out var b))
                throw new ArgumentException("The color provided was not in the correct format.", nameof(source));

            return new Color(r, g, b);
        }
    }
}