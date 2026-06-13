using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerAddressViewModel : ViewModelBase
    {
        private readonly FlowerShopService _shopService;
        private Guid _userId;
        private bool _isLoading;
        private ObservableCollection<ShippingAddressInfo> _addresses = new();
        private ShippingAddressInfo _selectedAddress;
        private bool _isEditing;
        private long _editingAddressId;
        private string _editShipTo = "";
        private string _editPhone = "";
        private string _editProvinceName = "";
        private string _editCityName = "";
        private string _editDistrictName = "";
        private string _editStreetName = "";
        private string _editCommunityName = "";
        private string _editAddress = "";
        private bool _editIsDefault;

        private ObservableCollection<string> _provinces = new();
        private ObservableCollection<string> _cities = new();
        private ObservableCollection<string> _districts = new();
        private ObservableCollection<string> _streets = new();
        private ObservableCollection<string> _communities = new();

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public ObservableCollection<ShippingAddressInfo> Addresses { get => _addresses; set => SetProperty(ref _addresses, value); }
        public ShippingAddressInfo SelectedAddress { get => _selectedAddress; set => SetProperty(ref _selectedAddress, value); }
        public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }
        public long EditingAddressId { get => _editingAddressId; set => SetProperty(ref _editingAddressId, value); }
        public string EditShipTo { get => _editShipTo; set => SetProperty(ref _editShipTo, value); }
        public string EditPhone { get => _editPhone; set => SetProperty(ref _editPhone, value); }
        public string EditProvinceName { get => _editProvinceName; set { SetProperty(ref _editProvinceName, value); OnProvinceChanged(value); } }
        public string EditCityName { get => _editCityName; set { SetProperty(ref _editCityName, value); OnCityChanged(value); } }
        public string EditDistrictName { get => _editDistrictName; set { SetProperty(ref _editDistrictName, value); OnDistrictChanged(value); } }
        public string EditStreetName { get => _editStreetName; set { SetProperty(ref _editStreetName, value); OnStreetChanged(value); } }
        public string EditCommunityName { get => _editCommunityName; set => SetProperty(ref _editCommunityName, value); }
        public string EditAddress { get => _editAddress; set => SetProperty(ref _editAddress, value); }
        public bool EditIsDefault { get => _editIsDefault; set => SetProperty(ref _editIsDefault, value); }
        public bool HasAddresses => Addresses.Count > 0;

        public ObservableCollection<string> Provinces { get => _provinces; }
        public ObservableCollection<string> Cities { get => _cities; }
        public ObservableCollection<string> Districts { get => _districts; }
        public ObservableCollection<string> Streets { get => _streets; }
        public ObservableCollection<string> Communities { get => _communities; }

        public async Task InitializeRegionsAsync()
        {
            var loaded = await ChinaRegions.LoadAsync();
            if (loaded)
            {
                _provinces.Clear();
                foreach (var p in ChinaRegions.GetProvinces())
                    _provinces.Add(p);
                OnPropertyChanged(nameof(Provinces));
            }
        }

        public FlowerAddressViewModel()
        {
            _shopService = new FlowerShopService();
        }

        public void SetUserId(Guid userId)
        {
            _userId = userId;
            _ = LoadAddressesAsync();
        }

        public async Task LoadAddressesAsync()
        {
            IsLoading = true;
            try
            {
                if (_userId == Guid.Empty)
                {
                    Addresses = new ObservableCollection<ShippingAddressInfo>();
                    return;
                }

                var addresses = await _shopService.GetUserAddressesAsync(_userId);
                Addresses = addresses != null
                    ? new ObservableCollection<ShippingAddressInfo>(addresses)
                    : new ObservableCollection<ShippingAddressInfo>();
                OnPropertyChanged(nameof(HasAddresses));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerAddress] 加载地址失败: {ex.Message}");
                ToastService.Instance.Error($"加载地址失败: {ex.Message}");
                Addresses = new ObservableCollection<ShippingAddressInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void StartAddNew()
        {
            EditingAddressId = 0;
            EditShipTo = "";
            EditPhone = "";
            EditProvinceName = "";
            EditCityName = "";
            EditDistrictName = "";
            EditStreetName = "";
            EditCommunityName = "";
            EditAddress = "";
            EditIsDefault = Addresses.Count == 0;
            IsEditing = true;
        }

        public void StartEdit(ShippingAddressInfo address)
        {
            if (address == null) return;
            EditingAddressId = address.Id;
            EditShipTo = address.ShipTo;
            EditPhone = address.Phone;
            EditProvinceName = address.ProvinceName;
            EditCityName = address.CityName;
            EditDistrictName = address.DistrictName;
            EditStreetName = "";
            EditCommunityName = "";
            EditAddress = address.Address;
            EditIsDefault = address.IsDefault;
            IsEditing = true;

            LoadCities(EditProvinceName);
            LoadDistricts(EditCityName);
            LoadStreets(EditDistrictName);
        }

        public void CancelEdit()
        {
            IsEditing = false;
        }

        public async Task SaveAddressAsync()
        {
            if (string.IsNullOrWhiteSpace(EditShipTo))
            {
                ToastService.Instance.Warning("请输入收货人姓名");
                return;
            }
            if (string.IsNullOrWhiteSpace(EditPhone))
            {
                ToastService.Instance.Warning("请输入联系电话");
                return;
            }
            if (string.IsNullOrWhiteSpace(EditProvinceName) || string.IsNullOrWhiteSpace(EditCityName) || string.IsNullOrWhiteSpace(EditDistrictName))
            {
                ToastService.Instance.Warning("请选择省/市/区");
                return;
            }
            if (string.IsNullOrWhiteSpace(EditAddress))
            {
                ToastService.Instance.Warning("请输入详细地址");
                return;
            }

            var fullAddress = EditAddress;
            if (!string.IsNullOrWhiteSpace(EditStreetName))
            {
                fullAddress = EditStreetName + (string.IsNullOrWhiteSpace(EditCommunityName) ? "" : EditCommunityName) + fullAddress;
            }

            try
            {
                ShippingAddressInfo result = null;

                if (EditingAddressId > 0)
                {
                    result = await _shopService.UpdateShippingAddressAsync(
                        EditingAddressId, _userId, EditShipTo, EditPhone,
                        EditProvinceName, EditCityName, EditDistrictName,
                        fullAddress, EditIsDefault);
                }
                else
                {
                    result = await _shopService.AddShippingAddressAsync(
                        _userId, EditShipTo, EditPhone,
                        EditProvinceName, EditCityName, EditDistrictName,
                        fullAddress, EditIsDefault);
                }

                if (result != null)
                {
                    ToastService.Instance.Success(EditingAddressId > 0 ? "地址已更新" : "地址已添加");
                    IsEditing = false;
                    await LoadAddressesAsync();
                }
                else
                {
                    ToastService.Instance.Error("保存地址失败，请检查服务器连接");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerAddress] 保存地址失败: {ex.Message}");
                ToastService.Instance.Error($"保存地址失败: {ex.Message}");
            }
        }

        public async Task DeleteAddressAsync(long addressId)
        {
            try
            {
                var success = await _shopService.DeleteShippingAddressAsync(_userId, addressId);
                if (success)
                {
                    ToastService.Instance.Success("地址已删除");
                    await LoadAddressesAsync();
                }
                else
                {
                    ToastService.Instance.Error("删除地址失败，请检查服务器连接");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerAddress] 删除地址失败: {ex.Message}");
                ToastService.Instance.Error($"删除地址失败: {ex.Message}");
            }
        }

        public async Task SetDefaultAsync(long addressId)
        {
            try
            {
                var success = await _shopService.SetDefaultShippingAddressAsync(_userId, addressId);
                if (success)
                {
                    ToastService.Instance.Success("已设为默认地址");
                    await LoadAddressesAsync();
                }
                else
                {
                    ToastService.Instance.Error("设置默认地址失败，请检查服务器连接");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlowerAddress] 设置默认地址失败: {ex.Message}");
                ToastService.Instance.Error($"设置默认地址失败: {ex.Message}");
            }
        }

        private void LoadCities(string provinceName)
        {
            _cities.Clear();
            _districts.Clear();
            _streets.Clear();
            _communities.Clear();
            if (!string.IsNullOrEmpty(provinceName))
            {
                var cities = ChinaRegions.GetCities(provinceName);
                foreach (var city in cities)
                    _cities.Add(city);
            }
        }

        private void LoadDistricts(string cityName)
        {
            _districts.Clear();
            _streets.Clear();
            _communities.Clear();
            if (!string.IsNullOrEmpty(cityName))
            {
                var districts = ChinaRegions.GetDistricts(EditProvinceName, cityName);
                foreach (var district in districts)
                    _districts.Add(district);
            }
        }

        private void LoadStreets(string districtName)
        {
            _streets.Clear();
            _communities.Clear();
            if (!string.IsNullOrEmpty(districtName))
            {
                var streets = ChinaRegions.GetStreets(EditProvinceName, EditCityName, districtName);
                foreach (var street in streets)
                    _streets.Add(street);
            }
        }

        private void LoadCommunities(string streetName)
        {
            _communities.Clear();
            if (!string.IsNullOrEmpty(streetName))
            {
                var communities = ChinaRegions.GetCommunities(EditProvinceName, EditCityName, EditDistrictName, streetName);
                foreach (var community in communities)
                    _communities.Add(community);
            }
        }

        private void OnProvinceChanged(string value)
        {
            EditCityName = "";
            EditDistrictName = "";
            EditStreetName = "";
            EditCommunityName = "";
            LoadCities(value);
        }

        private void OnCityChanged(string value)
        {
            EditDistrictName = "";
            EditStreetName = "";
            EditCommunityName = "";
            LoadDistricts(value);
        }

        private void OnDistrictChanged(string value)
        {
            EditStreetName = "";
            EditCommunityName = "";
            LoadStreets(value);
        }

        private void OnStreetChanged(string value)
        {
            EditCommunityName = "";
            LoadCommunities(value);
        }
    }
}
