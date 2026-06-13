using System;

namespace NarrativePro.Save
{
    public interface ISaveStorageProvider
    {
        void SaveData(string key, string jsonData);
        string LoadData(string key);
        bool HasData(string key);
        void DeleteData(string key);
    }
}
