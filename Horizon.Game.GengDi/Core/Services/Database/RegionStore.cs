using LiteDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services.Database
{
    public class RegionRecord
    {
        [BsonId]
        public int Id { get; set; }
        public string Code { get; set; }
        public string ParentCode { get; set; }
        public string Name { get; set; }
        public int Level { get; set; }
    }

    public static class RegionStore
    {
        private const string CollectionName = "regions";
        private static readonly object _lock = new();
        private static bool _isInitialized;
        private static string _dbPath;

        private static List<string> _cachedProvinces = new();
        private static Dictionary<string, List<string>> _cachedChildren = new();

        public static string DbPath
        {
            get => _dbPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HundunWorld", "region.db");
            set => _dbPath = value;
        }

        public static bool IsInitialized => _isInitialized;

        public static async Task<bool> InitializeAsync()
        {
            if (_isInitialized)
                return true;

            try
            {
                var dir = Path.GetDirectoryName(DbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        // Delete old database if it exists and is incomplete
                        var needImport = !File.Exists(DbPath);
                        if (!needImport)
                        {
                            try
                            {
                                using var checkDb = new LiteDatabase(DbPath);
                                var checkCol = checkDb.GetCollection<RegionRecord>(CollectionName);
                                var pCount = checkCol.Count(Query.EQ("Level", 1));
                                Console.WriteLine($"[RegionStore] 数据库中省份数: {pCount}");
                                if (pCount < 30)
                                {
                                    Console.WriteLine($"[RegionStore] 数据不完整，删除旧数据库重新导入");
                                    checkDb.Dispose();
                                    File.Delete(DbPath);
                                    needImport = true;
                                }
                            }
                            catch
                            {
                                File.Delete(DbPath);
                                needImport = true;
                            }
                        }

                        using var db = new LiteDatabase(DbPath);
                        var col = db.GetCollection<RegionRecord>(CollectionName);

                        if (needImport)
                        {
                            ImportFromJson(db, col);
                        }

                        BuildCaches(col);
                    }
                });

                _isInitialized = true;
                Console.WriteLine($"[RegionStore] 初始化完成，省份数: {_cachedProvinces.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RegionStore] 初始化失败: {ex.Message}");
                Console.WriteLine($"[RegionStore] 堆栈: {ex.StackTrace}");
                return false;
            }
        }

        private static void BuildCaches(ILiteCollection<RegionRecord> col)
        {
            try
            {
                var allRecords = col.FindAll().ToList();

                Console.WriteLine($"[RegionStore] 构建缓存，总记录数: {allRecords.Count}");

                _cachedProvinces.Clear();
                _cachedChildren.Clear();

                if (allRecords.Count == 0) return;

                var nameToCodeMap = new Dictionary<string, string>();
                var childrenByParentCode = new Dictionary<string, List<string>>();

                foreach (var record in allRecords)
                {
                    var nameKey = $"{record.Name}_{record.Level}";
                    if (!nameToCodeMap.ContainsKey(nameKey))
                        nameToCodeMap[nameKey] = record.Code;

                    if (!childrenByParentCode.TryGetValue(record.ParentCode, out var list))
                    {
                        list = new List<string>();
                        childrenByParentCode[record.ParentCode] = list;
                    }
                    list.Add(record.Name);
                }

                if (!childrenByParentCode.TryGetValue("0", out var provinces)) return;

                _cachedProvinces = new List<string>(provinces);
                Console.WriteLine($"[RegionStore] 省份数: {provinces.Count}");

                foreach (var provinceName in provinces)
                {
                    if (!nameToCodeMap.TryGetValue($"{provinceName}_1", out var provinceCode)) continue;
                    if (!childrenByParentCode.TryGetValue(provinceCode, out var cities)) continue;

                    _cachedChildren[$"{provinceName}_cities"] = cities;

                    foreach (var cityName in cities)
                    {
                        if (!nameToCodeMap.TryGetValue($"{cityName}_2", out var cityCode)) continue;
                        if (!childrenByParentCode.TryGetValue(cityCode, out var districts)) continue;

                        _cachedChildren[$"{provinceName}_{cityName}_districts"] = districts;

                        foreach (var districtName in districts)
                        {
                            if (!nameToCodeMap.TryGetValue($"{districtName}_3", out var districtCode)) continue;
                            if (!childrenByParentCode.TryGetValue(districtCode, out var streets)) continue;

                            _cachedChildren[$"{provinceName}_{cityName}_{districtName}_streets"] = streets;

                            foreach (var streetName in streets)
                            {
                                if (!nameToCodeMap.TryGetValue($"{streetName}_4", out var streetCode)) continue;
                                if (!childrenByParentCode.TryGetValue(streetCode, out var communities)) continue;

                                _cachedChildren[$"{provinceName}_{cityName}_{districtName}_{streetName}_communities"] = communities;
                            }
                        }
                    }
                }

                Console.WriteLine($"[RegionStore] 缓存构建完成: {_cachedProvinces.Count} 省份, {_cachedChildren.Count} 缓存组");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RegionStore] 缓存构建失败: {ex.Message}");
            }
        }

        public static List<string> GetProvinces()
        {
            if (!_isInitialized) return new List<string>();
            lock (_lock) { return new List<string>(_cachedProvinces); }
        }

        public static List<string> GetCities(string provinceName)
        {
            if (string.IsNullOrEmpty(provinceName) || !_isInitialized) return new List<string>();
            lock (_lock)
            {
                return _cachedChildren.TryGetValue($"{provinceName}_cities", out var cities)
                    ? new List<string>(cities) : new List<string>();
            }
        }

        public static List<string> GetDistricts(string provinceName, string cityName)
        {
            if (string.IsNullOrEmpty(provinceName) || string.IsNullOrEmpty(cityName) || !_isInitialized) return new List<string>();
            lock (_lock)
            {
                return _cachedChildren.TryGetValue($"{provinceName}_{cityName}_districts", out var districts)
                    ? new List<string>(districts) : new List<string>();
            }
        }

        public static List<string> GetStreets(string provinceName, string cityName, string districtName)
        {
            if (string.IsNullOrEmpty(provinceName) || string.IsNullOrEmpty(cityName) ||
                string.IsNullOrEmpty(districtName) || !_isInitialized) return new List<string>();
            lock (_lock)
            {
                return _cachedChildren.TryGetValue($"{provinceName}_{cityName}_{districtName}_streets", out var streets)
                    ? new List<string>(streets) : new List<string>();
            }
        }

        public static List<string> GetCommunities(string provinceName, string cityName, string districtName, string streetName)
        {
            if (string.IsNullOrEmpty(provinceName) || string.IsNullOrEmpty(cityName) ||
                string.IsNullOrEmpty(districtName) || string.IsNullOrEmpty(streetName) || !_isInitialized) return new List<string>();
            lock (_lock)
            {
                return _cachedChildren.TryGetValue($"{provinceName}_{cityName}_{districtName}_{streetName}_communities", out var communities)
                    ? new List<string>(communities) : new List<string>();
            }
        }

        private static void ImportFromJson(LiteDatabase db, ILiteCollection<RegionRecord> col)
        {
            try
            {
                Console.WriteLine($"[RegionStore] === 开始JSON导入 ===");

                var codeSet = new HashSet<string>();
                int totalInserted = 0;

                // Import pcas-code.json - insert incrementally
                var pcasPath = FindDataFile("pcas-code.json");
                if (!string.IsNullOrEmpty(pcasPath) && File.Exists(pcasPath))
                {
                    Console.WriteLine($"[RegionStore] 导入 pcas-code.json ({new FileInfo(pcasPath).Length / 1024} KB)");

                    var jsonContent = File.ReadAllText(pcasPath);
                    var pcasArray = JArray.Parse(jsonContent);
                    Console.WriteLine($"[RegionStore] JSON解析成功，省份数量: {pcasArray.Count}");

                    var batch = new List<RegionRecord>();
                    int provinceCount = 0;

                    foreach (JObject provinceObj in pcasArray)
                    {
                        try
                        {
                            var provinceCode = provinceObj["code"]?.ToString();
                            var provinceName = provinceObj["name"]?.ToString();
                            if (string.IsNullOrEmpty(provinceCode) || codeSet.Contains(provinceCode)) continue;

                            codeSet.Add(provinceCode);
                            provinceCount++;

                            batch.Add(new RegionRecord { Code = provinceCode, ParentCode = "0", Name = provinceName, Level = 1 });

                            var cityArray = provinceObj["children"] as JArray;
                            if (cityArray == null) continue;

                            foreach (JObject cityObj in cityArray)
                            {
                                try
                                {
                                    var cityCode = cityObj["code"]?.ToString();
                                    var cityName = cityObj["name"]?.ToString();
                                    if (string.IsNullOrEmpty(cityCode) || codeSet.Contains(cityCode)) continue;
                                    codeSet.Add(cityCode);

                                    batch.Add(new RegionRecord { Code = cityCode, ParentCode = provinceCode, Name = cityName, Level = 2 });

                                    var districtArray = cityObj["children"] as JArray;
                                    if (districtArray == null) continue;

                                    foreach (JObject districtObj in districtArray)
                                    {
                                        try
                                        {
                                            var districtCode = districtObj["code"]?.ToString();
                                            var districtName = districtObj["name"]?.ToString();
                                            if (string.IsNullOrEmpty(districtCode) || codeSet.Contains(districtCode)) continue;
                                            codeSet.Add(districtCode);

                                            batch.Add(new RegionRecord { Code = districtCode, ParentCode = cityCode, Name = districtName, Level = 3 });

                                            var streetArray = districtObj["children"] as JArray;
                                            if (streetArray == null) continue;

                                            foreach (JObject streetObj in streetArray)
                                            {
                                                try
                                                {
                                                    var streetCode = streetObj["code"]?.ToString();
                                                    var streetName = streetObj["name"]?.ToString();
                                                    if (string.IsNullOrEmpty(streetCode) || codeSet.Contains(streetCode)) continue;
                                                    codeSet.Add(streetCode);

                                                    batch.Add(new RegionRecord { Code = streetCode, ParentCode = districtCode, Name = streetName, Level = 4 });
                                                }
                                                catch { }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            }

                            // Flush batch every province
                            if (batch.Count > 0)
                            {
                                InsertBatchSafe(col, batch);
                                totalInserted += batch.Count;
                                batch.Clear();
                            }

                            if (provinceCount % 5 == 0)
                            {
                                Console.WriteLine($"[RegionStore] 已处理 {provinceCount} 个省份, 已插入 {totalInserted} 条");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RegionStore] 省份解析错误: {ex.Message}");
                        }
                    }

                    // Flush remaining
                    if (batch.Count > 0)
                    {
                        InsertBatchSafe(col, batch);
                        totalInserted += batch.Count;
                        batch.Clear();
                    }

                    Console.WriteLine($"[RegionStore] pcas导入完成: {provinceCount} 省, {totalInserted} 条记录");
                }
                else
                {
                    Console.WriteLine($"[RegionStore] 未找到 pcas-code.json");
                }

                // Import villages.json - insert incrementally
                var villagesPath = FindDataFile("villages.json");
                if (!string.IsNullOrEmpty(villagesPath) && File.Exists(villagesPath))
                {
                    Console.WriteLine($"[RegionStore] 导入 villages.json ({new FileInfo(villagesPath).Length / 1024 / 1024:F1} MB)");

                    int villageCount = 0;
                    var villageBatch = new List<RegionRecord>();

                    using var reader = new JsonTextReader(new StreamReader(villagesPath));
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.StartObject) continue;

                        string code = null, name = null, streetCode = null;
                        while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                        {
                            if (reader.TokenType == JsonToken.PropertyName)
                            {
                                var propName = reader.Value?.ToString();
                                reader.Read();
                                switch (propName)
                                {
                                    case "code": code = reader.Value?.ToString(); break;
                                    case "name": name = reader.Value?.ToString(); break;
                                    case "streetCode": streetCode = reader.Value?.ToString(); break;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(code) || codeSet.Contains(code)) continue;
                        codeSet.Add(code);

                        var cleanName = name?.Replace("居委会", "社区").Replace("村委会", "村").Replace("村民委员会", "村") ?? "";
                        villageBatch.Add(new RegionRecord { Code = code, ParentCode = streetCode ?? "", Name = cleanName, Level = 5 });
                        villageCount++;

                        // Flush every 5000 records
                        if (villageBatch.Count >= 5000)
                        {
                            InsertBatchSafe(col, villageBatch);
                            totalInserted += villageBatch.Count;
                            villageBatch.Clear();
                        }

                        if (villageCount % 50000 == 0)
                        {
                            Console.WriteLine($"[RegionStore] 已处理 {villageCount} 个村, 总插入 {totalInserted}");
                        }
                    }

                    // Flush remaining
                    if (villageBatch.Count > 0)
                    {
                        InsertBatchSafe(col, villageBatch);
                        totalInserted += villageBatch.Count;
                    }

                    Console.WriteLine($"[RegionStore] villages导入完成: {villageCount} 条");
                }
                else
                {
                    Console.WriteLine($"[RegionStore] 未找到 villages.json");
                }

                Console.WriteLine($"[RegionStore] === 导入完成，总记录: {col.Count()} ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RegionStore] 导入失败: {ex.Message}");
                Console.WriteLine($"[RegionStore] 堆栈: {ex.StackTrace}");
            }
        }

        private static void InsertBatchSafe(ILiteCollection<RegionRecord> col, List<RegionRecord> batch)
        {
            try
            {
                col.InsertBulk(batch);
            }
            catch
            {
                // Fallback: insert one by one
                foreach (var record in batch)
                {
                    try { col.Insert(record); }
                    catch { }
                }
            }
        }

        private static string FindDataFile(string fileName)
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Data", fileName),
                Path.Combine(AppContext.BaseDirectory, "Assets", "Data", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Data", fileName),
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    Console.WriteLine($"[RegionStore] 找到文件: {path}");
                    return path;
                }
            }

            Console.WriteLine($"[RegionStore] 未找到: {fileName}");
            Console.WriteLine($"[RegionStore] BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");

            try
            {
                var files = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.json", SearchOption.AllDirectories);
                foreach (var f in files.Take(10))
                    Console.WriteLine($"  - {f}");
            }
            catch { }

            return string.Empty;
        }
    }
}
