using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Clans.Extensions
{
    /// <summary>
    ///     Provides extension methods for the <see cref="IDictionary{TKey,TValue}" /> type.
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        ///     Returns a value associated with the specified key, or a default value if the key is not present in the dictionary.
        /// </summary>
        /// <param name="keyValuePairs">The dictionary.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The default value.</param>
        /// <typeparam name="TKey">The type of key.</typeparam>
        /// <typeparam name="TValue">The type of value.</typeparam>
        /// <returns>The result value.</returns>
        /// <exception cref="Exception"><paramref name="keyValuePairs" /> is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public static TValue GetValueOrDefault<TKey, TValue>([NotNull] this IDictionary<TKey, TValue> keyValuePairs,
            [NotNull] TKey key, TValue value = default(TValue))
        {
            if (keyValuePairs == null) throw new Exception("Dictionary must not be null.");
            if (key == null) throw new ArgumentNullException(nameof(key));

            return keyValuePairs.TryGetValue(key, out value) ? value : default(TValue);
        }
    }
}