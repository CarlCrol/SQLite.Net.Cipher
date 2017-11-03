using SQLite.Net.Async;
using SQLite.Net.Cipher.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SQLite.Net.Attributes;
using SQLite.Net.Cipher.Security;
using System.Reflection;
using SQLite.Net.Cipher.Model;
using SQLiteNetExtensions.Extensions;
using SQLiteNetExtensionsAsync.Extensions;
using SQLite.Net.Cipher.Utility;

namespace SQLite.Net.Cipher.Data
{
    /// <summary>
    /// This will be the secure database async
    /// </summary>
    public abstract class SecureDatabaseAsync<K> : Async.SQLiteAsyncConnection, ISecureDatabaseAsync<K>
    {
        /// <summary>
        /// This will be the crypto service for the secure database async
        /// </summary>
        private readonly ICryptoService _cryptoService;

        /// <summary>
        /// Constructor accepts salt key
        /// </summary>
        /// <param name="sqliteConnectionFunc"></param>
        /// <param name="saltText"></param>
        protected SecureDatabaseAsync(Func<SQLiteConnectionWithLock> sqliteConnectionFunc, string saltText) :
            this(sqliteConnectionFunc,new CryptoService(saltText))
        {

        }
        /// <summary>
        /// Constructor accepts ICryptoService
        /// </summary>
        /// <param name="sqliteConnectionFunc"></param>
        /// <param name="cryptoService"></param>
        /// <param name="taskScheduler"></param>
        /// <param name="taskCreationOptions"></param>
        protected SecureDatabaseAsync(Func<SQLiteConnectionWithLock> sqliteConnectionFunc, ICryptoService cryptoService, TaskScheduler taskScheduler = null, TaskCreationOptions taskCreationOptions = TaskCreationOptions.None) :
            base(sqliteConnectionFunc, taskScheduler, taskCreationOptions)
        {
            _cryptoService = cryptoService??throw new ArgumentNullException(nameof(cryptoService));
        }

        #region ISecureDatabaseAsync Methods
        /// <summary>
        /// Override this method to create your tables 
        /// </summary>
        protected abstract Task CreateTables();
        /// <summary>
        /// Deletes an item based on Id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        async Task<int> ISecureDatabaseAsync<K>.SecureDelete<T>(K id)
        {
            var result = 0;
            await RunInTransactionAsync(x =>
            {
                result = x.Execute(string.Format("Delete from {0} where id = ? ", typeof(T).Name), id);
            });
            return result;
        }
        /// <summary>
        /// Gets an Item securely
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="keySeed"></param>
        /// <returns></returns>
        async Task<T> ISecureDatabaseAsync<K>.SecureGet<T>(K id, string keySeed)
        {
            var item = await GetAsync<T>(x => x.Id.Equals(id));
            if (item == null)
                return null;
            Decrypt(item, keySeed);
            return item;
        }
        /// <summary>
        /// Gets Items securely
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="keySeed"></param>
        /// <returns></returns>
        async Task<List<T>> ISecureDatabaseAsync<K>.SecureGetAll<T>(string keySeed)
        {
            var items = await this.GetAllWithChildrenAsync<T>();
            if (items == null)
                return null;

            DecryptList<T>(items, keySeed);
            return items;
        }
        /// <summary>
        /// Gets the count of T in database
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        async Task<int> ISecureDatabaseAsync<K>.SecureGetCount<T>()
        {
            return await ExecuteAsync(string.Format("SELECT COUNT(*) FROM {0} ", typeof(T).Name));
        }
        /// <summary>
        /// This inserts securely
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="keySeed"></param>
        /// <returns></returns>
        async Task<int> ISecureDatabaseAsync<K>.SecureInsert<T>(T obj, string keySeed)
        {
            Guard.CheckForNull(obj, "obj cannot be null");
            Encrypt(obj, keySeed);

            var result = 0;
            await RunInTransactionAsync(x =>
            {
                result = x.Insert(obj);
            });

            return result;
        }
        /// <summary>
        /// This will query securely
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="keySeed"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        async Task<List<T>> ISecureDatabaseAsync<K>.SecureQuery<T>(string query, string keySeed, params object[] args)
        {
            var items = await QueryAsync<T>(query, args);
            if (items == null)
                return null;

            DecryptList<T>(items, keySeed);
            return items;
        }
        /// <summary>
        /// This will update an object securely
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="keySeed"></param>
        /// <returns></returns>
        async Task<int> ISecureDatabaseAsync<K>.SecureUpdate<T>(T obj, string keySeed)
        {
            Guard.CheckForNull(obj, "obj cannot be null");
            Encrypt(obj, keySeed);

            var result = 0;
            await RunInTransactionAsync(x =>
            {
                result = x.Update(obj);
            });

            return result;
        } 
        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls
        /// <summary>
        /// Dispose method
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    GetConnection()?.Dispose();
                }                

                disposedValue = true;
            }
        }
        
        /// <summary>
        /// Diposes method
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
             GC.SuppressFinalize(this);
        }
        #endregion


        #region Implementation
        /// <summary>
        /// This encrypts the object model properties
        /// </summary>
        /// <param name="model"></param>
        /// <param name="keySeed"></param>
        private void Encrypt(object model, string keySeed)
        {
            if (model == null) return;

            IEnumerable<PropertyInfo> secureProperties = GetSecureProperties(model);

            foreach (var propertyInfo in secureProperties)
            {
                var rawPropertyValue = (string)propertyInfo.GetValue(model);
                var encrypted = _cryptoService.Encrypt(rawPropertyValue, keySeed, null);
                propertyInfo.SetValue(model, encrypted);
            }
        }
        /// <summary>
        /// This decrypts the object model properties
        /// </summary>
        /// <param name="model"></param>
        /// <param name="keySeed"></param>
        private void Decrypt(object model, string keySeed)
        {
            if (model == null) return;

            IEnumerable<PropertyInfo> secureProperties = GetSecureProperties(model);

            foreach (var propertyInfo in secureProperties)
            {
                var rawPropertyValue = (string)propertyInfo.GetValue(model);
                var decrypted = _cryptoService.Decrypt(rawPropertyValue, keySeed, null);
                propertyInfo.SetValue(model, decrypted);
            }
        }
        /// <summary>
        /// Get Secure properties
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        private static IEnumerable<PropertyInfo> GetSecureProperties(object model)
        {
            var type = model.GetType();

            var secureProperties = type.GetRuntimeProperties()
                            .Where(pi => pi.PropertyType == typeof(string) && pi.GetCustomAttributes<Secure>(true).Any());
            return secureProperties;
        }
        /// <summary>
        /// Decrypts the list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="keySeed"></param>
        private void DecryptList<T>(List<T> list, string keySeed)
        {
            foreach (var item in list)
                Decrypt(item, keySeed);
        }

        #endregion

    }
}
