using System;
using System.IO;
using LiteDB;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Data.Storage
{
    public class DatabaseManager
    {
        private const string DatabaseName = "gengdi_litedb.db";
        private static LiteDatabase _database;

        public static LiteDatabase Database
        {
            get
            {
                if (_database == null)
                {
                    Initialize();
                }
                return _database;
            }
        }

        public static void Initialize()
        {
            if (_database != null) return;

            var databasePath = Path.Combine(ClientRuntimeContext.ResolveProductDataDirectory("GengDi"), DatabaseName);
            var directory = Path.GetDirectoryName(databasePath);
            
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _database = new LiteDatabase(new ConnectionString
            {
                Filename = databasePath,
                Connection = ConnectionType.Shared
            });
            EnsureIndexes();
        }

        private static void EnsureIndexes()
        {
            var messages = _database.GetCollection<Horizon.Game.GengDi.Models.IMMessage>();
            messages.EnsureIndex(m => m.SenderId);
            messages.EnsureIndex(m => m.ReceiverId);
            messages.EnsureIndex(m => m.IsGroupConversation);
        }

        public static void CloseConnection()
        {
            if (_database != null)
            {
                _database.Dispose();
                _database = null;
            }
        }
    }
}