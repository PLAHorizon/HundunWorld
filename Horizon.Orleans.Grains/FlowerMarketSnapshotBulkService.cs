using Horizon.Model.Flower;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public static class FlowerMarketSnapshotBulkService
    {
        public static async Task BulkInsertAsync(IEnumerable<FlowerMarketSnapshot> snapshots, string connectionString)
        {
            if (snapshots == null) return;

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var bulkCopy = new SqlBulkCopy(connection)
            {
                DestinationTableName = "Flower_MarketSnapshot",
                BatchSize = 1000,
                BulkCopyTimeout = 60
            };

            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.SpeciesId), "SpeciesId");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.MarketId), "MarketId");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.AvgPrice), "AvgPrice");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.MinPrice), "MinPrice");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.MaxPrice), "MaxPrice");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.Volume), "Volume");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.TradeCount), "TradeCount");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.SnapshotTime), "SnapshotTime");
            bulkCopy.ColumnMappings.Add(nameof(FlowerMarketSnapshot.DataSource), "DataSource");

            var table = new System.Data.DataTable();
            table.Columns.Add("SpeciesId", typeof(long));
            table.Columns.Add("MarketId", typeof(long));
            table.Columns.Add("AvgPrice", typeof(decimal));
            table.Columns.Add("MinPrice", typeof(decimal));
            table.Columns.Add("MaxPrice", typeof(decimal));
            table.Columns.Add("Volume", typeof(int));
            table.Columns.Add("TradeCount", typeof(int));
            table.Columns.Add("SnapshotTime", typeof(DateTime));
            table.Columns.Add("DataSource", typeof(int));

            foreach (var s in snapshots)
            {
                table.Rows.Add(s.SpeciesId, s.MarketId, s.AvgPrice, s.MinPrice, s.MaxPrice,
                    s.Volume, s.TradeCount, s.SnapshotTime, s.DataSource);
            }

            await bulkCopy.WriteToServerAsync(table);
        }
    }
}
