IF OBJECT_ID(N'[dbo].[SP_Flower_QueryDevicesByGreenhouse]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_QueryDevicesByGreenhouse]
GO

CREATE PROCEDURE [dbo].[SP_Flower_QueryDevicesByGreenhouse]
    @GreenhouseId VARCHAR(64),
    @OnlineStatus VARCHAR(16) = NULL,
    @DeviceType VARCHAR(32) = NULL,
    @GroupId VARCHAR(64) = NULL,
    @Skip INT = 0,
    @Take INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        d.Id,
        d.DeviceCode,
        d.DeviceName,
        d.DeviceType,
        d.GreenhouseId,
        d.GroupId,
        d.Protocol,
        d.MqttTopic,
        d.ApiKey,
        d.OnlineStatus,
        d.FirmwareVersion,
        d.LastHeartbeatTime,
        d.IsEnabled,
        d.IsDeleted,
        g.GroupName,
        g.Description AS GroupDescription
    FROM Flower_IoTDevice d
    LEFT JOIN Flower_DeviceGroup g ON d.GroupId = CAST(g.Id AS VARCHAR(64)) AND g.IsDeleted = 0
    WHERE d.GreenhouseId = @GreenhouseId
      AND d.IsDeleted = 0
      AND (@OnlineStatus IS NULL OR d.OnlineStatus = @OnlineStatus)
      AND (@DeviceType IS NULL OR d.DeviceType = @DeviceType)
      AND (@GroupId IS NULL OR d.GroupId = @GroupId)
    ORDER BY d.OnlineStatus DESC, d.DeviceName
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

    SELECT COUNT(*) AS TotalCount
    FROM Flower_IoTDevice
    WHERE GreenhouseId = @GreenhouseId
      AND IsDeleted = 0
      AND (@OnlineStatus IS NULL OR OnlineStatus = @OnlineStatus)
      AND (@DeviceType IS NULL OR DeviceType = @DeviceType)
      AND (@GroupId IS NULL OR GroupId = @GroupId);
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_QueryOfflineDevices]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_QueryOfflineDevices]
GO

CREATE PROCEDURE [dbo].[SP_Flower_QueryOfflineDevices]
    @TimeoutSeconds INT = 60
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Flower_IoTDevice
    SET OnlineStatus = 'Offline'
    WHERE OnlineStatus = 'Online'
      AND IsDeleted = 0
      AND IsEnabled = 1
      AND LastHeartbeatTime IS NOT NULL
      AND DATEDIFF(SECOND, LastHeartbeatTime, GETUTCDATE()) > @TimeoutSeconds;

    SELECT
        Id,
        DeviceCode,
        DeviceName,
        DeviceType,
        GreenhouseId,
        GroupId,
        OnlineStatus,
        LastHeartbeatTime
    FROM Flower_IoTDevice
    WHERE OnlineStatus = 'Offline'
      AND IsDeleted = 0
      AND IsEnabled = 1
    ORDER BY LastHeartbeatTime ASC;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_AggregateSensorDataHourly]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_AggregateSensorDataHourly]
GO

CREATE PROCEDURE [dbo].[SP_Flower_AggregateSensorDataHourly]
    @GreenhouseId VARCHAR(64),
    @StartDate DATETIME,
    @EndDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        FORMAT(ReadingTime, 'yyyy-MM-dd HH:00') AS HourBucket,
        AVG(Temperature) AS AvgTemperature,
        MIN(Temperature) AS MinTemperature,
        MAX(Temperature) AS MaxTemperature,
        AVG(Humidity) AS AvgHumidity,
        MIN(Humidity) AS MinHumidity,
        MAX(Humidity) AS MaxHumidity,
        AVG(LightIntensity) AS AvgLightIntensity,
        AVG(Co2Level) AS AvgCo2Level,
        AVG(SoilMoisture) AS AvgSoilMoisture,
        COUNT(*) AS ReadingCount,
        SUM(CASE WHEN DataQuality = 'Abnormal' THEN 1 ELSE 0 END) AS AbnormalCount
    FROM Flower_SensorReading
    WHERE GreenhouseId = @GreenhouseId
      AND ReadingTime >= @StartDate
      AND ReadingTime < @EndDate
      AND DataQuality = 'Normal'
    GROUP BY FORMAT(ReadingTime, 'yyyy-MM-dd HH:00')
    ORDER BY HourBucket;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_AggregateSensorDataDaily]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_AggregateSensorDataDaily]
GO

CREATE PROCEDURE [dbo].[SP_Flower_AggregateSensorDataDaily]
    @GreenhouseId VARCHAR(64),
    @StartDate DATETIME,
    @EndDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CAST(ReadingTime AS DATE) AS DayBucket,
        AVG(Temperature) AS AvgTemperature,
        MIN(Temperature) AS MinTemperature,
        MAX(Temperature) AS MaxTemperature,
        AVG(Humidity) AS AvgHumidity,
        MIN(Humidity) AS MinHumidity,
        MAX(Humidity) AS MaxHumidity,
        AVG(LightIntensity) AS AvgLightIntensity,
        AVG(Co2Level) AS AvgCo2Level,
        AVG(SoilMoisture) AS AvgSoilMoisture,
        COUNT(*) AS ReadingCount,
        SUM(CASE WHEN DataQuality = 'Abnormal' THEN 1 ELSE 0 END) AS AbnormalCount
    FROM Flower_SensorReading
    WHERE GreenhouseId = @GreenhouseId
      AND ReadingTime >= @StartDate
      AND ReadingTime < @EndDate
      AND DataQuality = 'Normal'
    GROUP BY CAST(ReadingTime AS DATE)
    ORDER BY DayBucket;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_SensorDistributionStats]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_SensorDistributionStats]
GO

CREATE PROCEDURE [dbo].[SP_Flower_SensorDistributionStats]
    @GreenhouseId VARCHAR(64),
    @StartDate DATETIME,
    @EndDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        'Temperature' AS Metric,
        SUM(CASE WHEN Temperature < 15 THEN 1 ELSE 0 END) AS RangeBelow15,
        SUM(CASE WHEN Temperature >= 15 AND Temperature < 25 THEN 1 ELSE 0 END) AS Range15To25,
        SUM(CASE WHEN Temperature >= 25 AND Temperature < 35 THEN 1 ELSE 0 END) AS Range25To35,
        SUM(CASE WHEN Temperature >= 35 THEN 1 ELSE 0 END) AS RangeAbove35,
        COUNT(*) AS TotalReadings
    FROM Flower_SensorReading
    WHERE GreenhouseId = @GreenhouseId
      AND ReadingTime >= @StartDate
      AND ReadingTime < @EndDate
      AND DataQuality = 'Normal';

    SELECT
        'LightIntensity' AS Metric,
        SUM(CASE WHEN LightIntensity < 500 THEN 1 ELSE 0 END) AS RangeNoLight,
        SUM(CASE WHEN LightIntensity >= 500 AND LightIntensity < 5000 THEN 1 ELSE 0 END) AS RangeWeak,
        SUM(CASE WHEN LightIntensity >= 5000 AND LightIntensity < 20000 THEN 1 ELSE 0 END) AS RangeModerate,
        SUM(CASE WHEN LightIntensity >= 20000 THEN 1 ELSE 0 END) AS RangeStrong,
        COUNT(*) AS TotalReadings
    FROM Flower_SensorReading
    WHERE GreenhouseId = @GreenhouseId
      AND ReadingTime >= @StartDate
      AND ReadingTime < @EndDate
      AND DataQuality = 'Normal';

    SELECT
        'Humidity' AS Metric,
        SUM(CASE WHEN Humidity < 40 THEN 1 ELSE 0 END) AS RangeBelow40,
        SUM(CASE WHEN Humidity >= 40 AND Humidity < 60 THEN 1 ELSE 0 END) AS Range40To60,
        SUM(CASE WHEN Humidity >= 60 AND Humidity < 80 THEN 1 ELSE 0 END) AS Range60To80,
        SUM(CASE WHEN Humidity >= 80 THEN 1 ELSE 0 END) AS RangeAbove80,
        COUNT(*) AS TotalReadings
    FROM Flower_SensorReading
    WHERE GreenhouseId = @GreenhouseId
      AND ReadingTime >= @StartDate
      AND ReadingTime < @EndDate
      AND DataQuality = 'Normal';
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_CostCategoryStatsByBatch]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_CostCategoryStatsByBatch]
GO

CREATE PROCEDURE [dbo].[SP_Flower_CostCategoryStatsByBatch]
    @BatchId BIGINT,
    @StartDate DATETIME = NULL,
    @EndDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalAmount DECIMAL(18,2);

    SELECT @TotalAmount = SUM(Amount)
    FROM Flower_CostRecord
    WHERE BatchId = @BatchId
      AND IsDeleted = 0
      AND (@StartDate IS NULL OR CostDate >= @StartDate)
      AND (@EndDate IS NULL OR CostDate < @EndDate);

    SELECT
        Category,
        SUM(Amount) AS TotalAmount,
        COUNT(*) AS RecordCount,
        CASE WHEN @TotalAmount > 0
             THEN CAST(SUM(Amount) AS FLOAT) / CAST(@TotalAmount AS FLOAT) * 100.0
             ELSE 0.0
        END AS Percentage
    FROM Flower_CostRecord
    WHERE BatchId = @BatchId
      AND IsDeleted = 0
      AND (@StartDate IS NULL OR CostDate >= @StartDate)
      AND (@EndDate IS NULL OR CostDate < @EndDate)
    GROUP BY Category
    ORDER BY TotalAmount DESC;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_CostMonthlyTrend]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_CostMonthlyTrend]
GO

CREATE PROCEDURE [dbo].[SP_Flower_CostMonthlyTrend]
    @GreenhouseId VARCHAR(64),
    @Months INT = 6
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @StartDate DATETIME = DATEADD(MONTH, -@Months, GETUTCDATE());

    SELECT
        FORMAT(CostDate, 'yyyy-MM') AS Month,
        SUM(Amount) AS TotalAmount,
        SUM(CASE WHEN Category = 'Seedling' THEN Amount ELSE 0 END) AS SeedlingCost,
        SUM(CASE WHEN Category = 'Fertilizer' THEN Amount ELSE 0 END) AS FertilizerCost,
        SUM(CASE WHEN Category = 'Pesticide' THEN Amount ELSE 0 END) AS PesticideCost,
        SUM(CASE WHEN Category = 'Labor' THEN Amount ELSE 0 END) AS LaborCost,
        SUM(CASE WHEN Category = 'Utility' THEN Amount ELSE 0 END) AS UtilityCost,
        SUM(CASE WHEN Category = 'Depreciation' THEN Amount ELSE 0 END) AS DepreciationCost,
        SUM(CASE WHEN Category = 'Other' THEN Amount ELSE 0 END) AS OtherCost
    FROM Flower_CostRecord cr
    INNER JOIN Flower_PlantingBatch pb ON cr.BatchId = pb.Id AND pb.IsDeleted = 0
    WHERE pb.GreenhouseId = @GreenhouseId
      AND cr.IsDeleted = 0
      AND cr.CostDate >= @StartDate
    GROUP BY FORMAT(CostDate, 'yyyy-MM')
    ORDER BY Month;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_YieldMonthlyTrend]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_YieldMonthlyTrend]
GO

CREATE PROCEDURE [dbo].[SP_Flower_YieldMonthlyTrend]
    @GreenhouseId VARCHAR(64),
    @Months INT = 6
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @StartDate DATETIME = DATEADD(MONTH, -@Months, GETUTCDATE());
    DECLARE @LastYearStart DATETIME = DATEADD(YEAR, -1, @StartDate);
    DECLARE @LastYearEnd DATETIME = DATEADD(YEAR, -1, GETUTCDATE());

    SELECT
        FORMAT(yr.HarvestDate, 'yyyy-MM') AS Month,
        yr.SpeciesName,
        SUM(yr.Quantity) AS TotalQuantity,
        SUM(CASE WHEN yr.Grade = 'A' THEN yr.Quantity ELSE 0 END) AS GradeAQuantity,
        SUM(CASE WHEN yr.Grade = 'B' THEN yr.Quantity ELSE 0 END) AS GradeBQuantity,
        SUM(CASE WHEN yr.Grade = 'C' THEN yr.Quantity ELSE 0 END) AS GradeCQuantity
    FROM Flower_YieldRecord yr
    INNER JOIN Flower_PlantingBatch pb ON yr.BatchId = pb.Id AND pb.IsDeleted = 0
    WHERE pb.GreenhouseId = @GreenhouseId
      AND yr.IsDeleted = 0
      AND yr.HarvestDate >= @StartDate
    GROUP BY FORMAT(yr.HarvestDate, 'yyyy-MM'), yr.SpeciesName
    ORDER BY Month, yr.SpeciesName;

    SELECT
        FORMAT(HarvestDate, 'yyyy-MM') AS Month,
        SUM(Quantity) AS LastYearTotalQuantity
    FROM Flower_YieldRecord yr
    INNER JOIN Flower_PlantingBatch pb ON yr.BatchId = pb.Id AND pb.IsDeleted = 0
    WHERE pb.GreenhouseId = @GreenhouseId
      AND yr.IsDeleted = 0
      AND yr.HarvestDate >= @LastYearStart
      AND yr.HarvestDate < @LastYearEnd
    GROUP BY FORMAT(HarvestDate, 'yyyy-MM')
    ORDER BY Month;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_YieldGradeStatsByBatch]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_YieldGradeStatsByBatch]
GO

CREATE PROCEDURE [dbo].[SP_Flower_YieldGradeStatsByBatch]
    @BatchId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalQuantity DECIMAL(18,2);

    SELECT @TotalQuantity = SUM(Quantity)
    FROM Flower_YieldRecord
    WHERE BatchId = @BatchId
      AND IsDeleted = 0;

    SELECT
        Grade,
        SpeciesName,
        SUM(Quantity) AS TotalQuantity,
        COUNT(*) AS RecordCount,
        CASE WHEN @TotalQuantity > 0
             THEN CAST(SUM(Quantity) AS FLOAT) / CAST(@TotalQuantity AS FLOAT) * 100.0
             ELSE 0.0
        END AS Percentage
    FROM Flower_YieldRecord
    WHERE BatchId = @BatchId
      AND IsDeleted = 0
    GROUP BY Grade, SpeciesName
    ORDER BY Grade, SpeciesName;
END
GO

IF OBJECT_ID(N'[dbo].[SP_Flower_PlantingBatchOverview]') IS NOT NULL
    DROP PROCEDURE [dbo].[SP_Flower_PlantingBatchOverview]
GO

CREATE PROCEDURE [dbo].[SP_Flower_PlantingBatchOverview]
    @GreenhouseId VARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        pb.Id,
        pb.BatchName,
        pb.SpeciesName,
        pb.PlantingDate,
        pb.ExpectedHarvestDate,
        pb.ActualHarvestDate,
        pb.Status,
        pb.PlantingQuantity,
        ISNULL(cost.TotalCost, 0) AS TotalCost,
        ISNULL(yield.TotalYield, 0) AS TotalYield,
        ISNULL(device.DeviceCount, 0) AS DeviceCount,
        ISNULL(device.OnlineDeviceCount, 0) AS OnlineDeviceCount
    FROM Flower_PlantingBatch pb
    LEFT JOIN (
        SELECT BatchId, SUM(Amount) AS TotalCost
        FROM Flower_CostRecord
        WHERE IsDeleted = 0
        GROUP BY BatchId
    ) cost ON pb.Id = cost.BatchId
    LEFT JOIN (
        SELECT BatchId, SUM(Quantity) AS TotalYield
        FROM Flower_YieldRecord
        WHERE IsDeleted = 0
        GROUP BY BatchId
    ) yield ON pb.Id = yield.BatchId
    LEFT JOIN (
        SELECT
            GreenhouseId AS DeviceGreenhouseId,
            COUNT(*) AS DeviceCount,
            SUM(CASE WHEN OnlineStatus = 'Online' THEN 1 ELSE 0 END) AS OnlineDeviceCount
        FROM Flower_IoTDevice
        WHERE IsDeleted = 0 AND IsEnabled = 1
        GROUP BY GreenhouseId
    ) device ON pb.GreenhouseId = device.DeviceGreenhouseId
    WHERE pb.GreenhouseId = @GreenhouseId
      AND pb.IsDeleted = 0
    ORDER BY pb.PlantingDate DESC;
END
GO
