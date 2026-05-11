using System.Threading.Tasks;

namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Interface for a generic key-value data store with typed serialization support.
    /// Decouples data persistence consumers from any specific storage implementation.
    /// </summary>
    public interface IDataStore
    {
        /// <summary>
        /// Saves data of type T under the specified key.
        /// </summary>
        /// <typeparam name="T">The type of data to save.</typeparam>
        /// <param name="key">The unique key to store the data under.</param>
        /// <param name="data">The data object to save.</param>
        Task SaveAsync<T>(string key, T data);

        /// <summary>
        /// Loads data of type T from the specified key.
        /// </summary>
        /// <typeparam name="T">The expected type of the stored data.</typeparam>
        /// <param name="key">The unique key the data was stored under.</param>
        /// <returns>The deserialized data object, or default(T) if not found.</returns>
        Task<T> LoadAsync<T>(string key);

        /// <summary>
        /// Checks whether data exists for the specified key.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if data exists for the key.</returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// Deletes the data associated with the specified key.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        Task DeleteAsync(string key);

        /// <summary>
        /// Clears all stored data.
        /// </summary>
        Task ClearAsync();
    }
}
