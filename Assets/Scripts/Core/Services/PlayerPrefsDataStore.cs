using System;
using System.Threading.Tasks;
using UnityEngine;
using CubiWare.Core.Interfaces;
using CubiWare.Core.Logging;

namespace CubiWare.Core.Services
{
    /// <summary>
    /// Implementation of <see cref="IDataStore"/> that uses Unity's
    /// <see cref="PlayerPrefs"/> as the backing store. Data is serialized
    /// using <see cref="JsonUtility"/> for type-safe persistence.
    /// </summary>
    public class PlayerPrefsDataStore : IDataStore
    {
        private readonly ServiceLogger _logger = ServiceLogger.Instance;

        /// <summary>
        /// Saves data of type T under the specified key using JSON serialization.
        /// </summary>
        /// <typeparam name="T">The type of data to save.</typeparam>
        /// <param name="key">The unique key to store the data under.</param>
        /// <param name="data">The data object to save.</param>
        public Task SaveAsync<T>(string key, T data)
        {
            try
            {
                string json = JsonUtility.ToJson(data);
                PlayerPrefs.SetString(key, json);
                PlayerPrefs.Save();
                _logger.LogInfo(nameof(PlayerPrefsDataStore), $"Saved data under key: {key}");
            }
            catch (Exception ex)
            {
                _logger.LogError(nameof(PlayerPrefsDataStore), $"Failed to save data under key '{key}': {ex.Message}", ServiceErrorCode.DataStoreWriteFailed);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Loads data of type T from the specified key using JSON deserialization.
        /// </summary>
        /// <typeparam name="T">The expected type of the stored data.</typeparam>
        /// <param name="key">The unique key the data was stored under.</param>
        /// <returns>The deserialized data object, or default(T) if not found or on error.</returns>
        public Task<T> LoadAsync<T>(string key)
        {
            try
            {
                if (!PlayerPrefs.HasKey(key))
                {
                    _logger.LogInfo(nameof(PlayerPrefsDataStore), $"Key '{key}' not found in PlayerPrefs. Returning default.");
                    return Task.FromResult(default(T));
                }

                string json = PlayerPrefs.GetString(key);
                if (string.IsNullOrEmpty(json))
                {
                    _logger.LogWarning(nameof(PlayerPrefsDataStore), $"Empty JSON found for key '{key}'.");
                    return Task.FromResult(default(T));
                }

                T data = JsonUtility.FromJson<T>(json);
                _logger.LogInfo(nameof(PlayerPrefsDataStore), $"Loaded data under key: {key}");
                return Task.FromResult(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(nameof(PlayerPrefsDataStore), $"Failed to load data under key '{key}': {ex.Message}", ServiceErrorCode.DataStoreReadFailed);
                return Task.FromResult(default(T));
            }
        }

        /// <summary>
        /// Checks whether data exists for the specified key.
        /// </summary>
        /// <param name="key">The key to check.</param>
        /// <returns>True if data exists for the key.</returns>
        public Task<bool> ExistsAsync(string key)
        {
            bool exists = PlayerPrefs.HasKey(key);
            return Task.FromResult(exists);
        }

        /// <summary>
        /// Deletes the data associated with the specified key.
        /// </summary>
        /// <param name="key">The key to delete.</param>
        public Task DeleteAsync(string key)
        {
            PlayerPrefs.DeleteKey(key);
            _logger.LogInfo(nameof(PlayerPrefsDataStore), $"Deleted key: {key}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Clears all stored data.
        /// </summary>
        public Task ClearAsync()
        {
            PlayerPrefs.DeleteAll();
            _logger.LogInfo(nameof(PlayerPrefsDataStore), "All PlayerPrefs data cleared.");
            return Task.CompletedTask;
        }
    }
}
