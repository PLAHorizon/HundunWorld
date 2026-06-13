using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;
using Xunit;

namespace Horizon.Game.GengDi.Tests.Services;

public class CitySearchServiceTests
{
    [Fact]
    public async Task SearchCities_ProvinceLevel_ReturnsResults()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("河北");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Province == "河北"));
    }

    [Fact]
    public async Task SearchCities_CityLevel_ReturnsExactMatch()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("石家庄");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name == "石家庄"));
        Assert.True(result.HasExactMatch);
    }

    [Fact]
    public async Task SearchCities_CountyLevel_ReturnsCountyData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("正定");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("正定")));
    }

    [Fact]
    public async Task SearchCities_TownSuffix_DetectsTownLevel()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("中关村街道");
        Assert.Equal("乡镇级", result.DetectedLevel);
    }

    [Fact]
    public async Task SearchCities_VillageSuffix_DetectsVillageLevel()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("李家村");
        Assert.Equal("村级", result.DetectedLevel);
    }

    [Fact]
    public async Task SearchCities_CountySuffix_DetectsDistrictLevel()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("正定县");
        Assert.Equal("县级", result.DetectedLevel);
    }

    [Fact]
    public async Task SearchCities_CitySuffix_DetectsCityLevel()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("石家庄市");
        Assert.Equal("市级", result.DetectedLevel);
    }

    [Fact]
    public async Task SearchCities_Beijing_ReturnsDistrictData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("海淀");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("海淀")));
    }

    [Fact]
    public async Task SearchCities_Shanghai_ReturnsPudong()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("浦东");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("浦东")));
    }

    [Fact]
    public async Task SearchCities_SichuanCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("都江堰");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("都江堰")));
    }

    [Fact]
    public async Task SearchCities_GuangdongCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("顺德");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("顺德")));
    }

    [Fact]
    public async Task SearchCities_JiangsuCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("昆山");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("昆山")));
    }

    [Fact]
    public async Task SearchCities_ZhejiangCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("慈溪");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("慈溪")));
    }

    [Fact]
    public async Task SearchCities_ShandongCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("胶州");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("胶州")));
    }

    [Fact]
    public async Task SearchCities_HenanCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("新郑");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("新郑")));
    }

    [Fact]
    public async Task SearchCities_HubeiCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("秭归");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("秭归")));
    }

    [Fact]
    public async Task SearchCities_HunanCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("浏阳");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("浏阳")));
    }

    [Fact]
    public async Task SearchCities_ShaanxiCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("蓝田");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("蓝田")));
    }

    [Fact]
    public async Task SearchCities_GansuCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("榆中");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("榆中")));
    }

    [Fact]
    public async Task SearchCities_XinjiangCity_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("喀什");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("喀什")));
    }

    [Fact]
    public async Task SearchCities_TibetCity_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("日喀则");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("日喀则")));
    }

    [Fact]
    public async Task SearchCities_Sansha_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("三沙");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("三沙")));
    }

    [Fact]
    public async Task SearchCities_FuzzyMatch_ReturnsResults()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("石家");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("石家")));
    }

    [Fact]
    public async Task SearchCities_CompoundQuery_ReturnsResults()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("成都 都江堰");
        Assert.NotEmpty(result.Cities);
    }

    [Fact]
    public async Task SearchCities_EmptyQuery_ReturnsDefaultCities()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("");
        Assert.NotEmpty(result.Cities);
    }

    [Fact]
    public async Task SearchCities_NonExistentLocation_ProvidesFeedback()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("不存在的地方xyz");
        Assert.NotEmpty(result.SearchMessage);
    }

    [Fact]
    public async Task SearchCities_DefaultCity_ReturnsBeijing()
    {
        var city = CitySearchService.GetDefaultCity();
        Assert.NotNull(city);
        Assert.Equal("北京", city.Name);
    }

    [Fact]
    public async Task SearchCities_ResultHasAdminLevel()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("石家庄");
        Assert.NotEmpty(result.Cities);
        foreach (var city in result.Cities)
        {
            Assert.False(string.IsNullOrWhiteSpace(city.AdminLevel));
        }
    }

    [Fact]
    public async Task SearchCities_DisplayName_ShowsHierarchy()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("正定");
        Assert.NotEmpty(result.Cities);
        var zhengding = result.Cities.FirstOrDefault(c => c.Name == "正定");
        if (zhengding != null)
        {
            Assert.Contains("河北", zhengding.DisplayName);
        }
    }

    [Fact]
    public async Task SearchCities_ResponseTime_UnderTwoSeconds()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await CitySearchService.SearchCitiesDetailedAsync("石家庄");
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Response took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    [Fact]
    public async Task SearchCities_FujianCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("晋江");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("晋江")));
    }

    [Fact]
    public async Task SearchCities_YunnanCity_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("大理");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("大理")));
    }

    [Fact]
    public async Task SearchCities_NingxiaCounty_ReturnsData()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("贺兰");
        Assert.NotEmpty(result.Cities);
        Assert.True(result.Cities.Any(c => c.Name.Contains("贺兰")));
    }

    [Fact]
    public async Task SearchCities_CoordinateValidation_RejectsInvalidCoords()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("石家庄");
        Assert.NotEmpty(result.Cities);
        foreach (var city in result.Cities)
        {
            Assert.True(city.Latitude >= 3.0 && city.Latitude <= 55.0,
                $"Lat {city.Latitude} out of China bounds");
            Assert.True(city.Longitude >= 70.0 && city.Longitude <= 140.0,
                $"Lon {city.Longitude} out of China bounds");
        }
    }

    [Fact]
    public async Task SearchCities_TownLevelData_HasCorrectAdminLevel()
    {
        var result = await CitySearchService.SearchCitiesDetailedAsync("张江镇");
        Assert.NotEmpty(result.Cities);
        var zhangjiang = result.Cities.FirstOrDefault(c => c.Name.Contains("张江"));
        if (zhangjiang != null)
        {
            Assert.Equal("乡镇", zhangjiang.AdminLevel);
        }
    }
}
