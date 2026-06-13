using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerMerchantGrain : Grain, IMerchantGrain
    {
        private readonly ILogger<FlowerMerchantGrain> _logger;
        private readonly IPersistentState<MerchantState> _merchantState;
        private readonly IDataContext<FlowerEntityContext, FlowerMerchant, long> _merchantContext;
        private readonly IDataContext<FlowerEntityContext, FlowerUser, long> _flowerUserContext;
        private readonly IDataContext<FlowerEntityContext, FlowerShopShipper, long> _shipperContext;

        public FlowerMerchantGrain(
            ILogger<FlowerMerchantGrain> logger,
            [PersistentState("merchant", "FlowerStore")] IPersistentState<MerchantState> merchantState,
            IDataContext<FlowerEntityContext, FlowerMerchant, long> merchantContext,
            IDataContext<FlowerEntityContext, FlowerUser, long> flowerUserContext,
            IDataContext<FlowerEntityContext, FlowerShopShipper, long> shipperContext)
        {
            _logger = logger;
            _merchantState = merchantState;
            _merchantContext = merchantContext;
            _flowerUserContext = flowerUserContext;
            _shipperContext = shipperContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerMerchantGrain {GrainKey} activating.", this.GetPrimaryKeyLong());
            await GetMerchantAsync();
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<MerchantState> GetMerchantAsync()
        {
            if (_merchantState.State.MerchantId == 0)
            {
                var merchantId = this.GetPrimaryKeyLong();
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == merchantId && !e.IsDeleted);
                if (entity != null)
                {
                    _merchantState.State = new MerchantState
                    {
                        MerchantId = entity.Id,
                        UserId = entity.UserId,
                        Passport = entity.Passport ?? "",
                        MerchantType = (MerchantType)entity.MerchantType,
                        ShopName = entity.ShopName ?? "",
                        ShopDescription = entity.ShopDescription ?? "",
                        ContactPhone = entity.ContactPhone ?? "",
                        BusinessLicense = entity.BusinessLicense ?? "",
                        IsVerified = entity.IsVerified,
                        VerifiedAt = entity.VerifiedAt,
                        AuditStatus = entity.AuditStatus
                    };
                    await _merchantState.WriteStateAsync();
                }
            }
            return _merchantState.State;
        }

        public async Task<MerchantState> GetMerchantByPassportAsync(string passport)
        {
            try
            {
                if (string.IsNullOrEmpty(passport))
                {
                    return null;
                }

                var merchant = await _merchantContext.QueryFirstOrDefaultAsync(m => m.Passport == passport && !m.IsDeleted);
                if (merchant == null)
                {
                    return null;
                }

                var state = new MerchantState
                {
                    MerchantId = merchant.Id,
                    UserId = merchant.UserId,
                    Passport = merchant.Passport,
                    MerchantType = (MerchantType)merchant.MerchantType,
                    ShopName = merchant.ShopName ?? "",
                    ShopDescription = merchant.ShopDescription ?? "",
                    ContactPhone = merchant.ContactPhone ?? "",
                    BusinessLicense = merchant.BusinessLicense ?? "",
                    IsVerified = merchant.IsVerified,
                    VerifiedAt = merchant.VerifiedAt,
                    AuditStatus = merchant.AuditStatus
                };

                _merchantState.State = state;
                await _merchantState.WriteStateAsync();

                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通过 Passport 查询商户失败: Passport={Passport}", passport);
                return null;
            }
        }

        public async Task<MerchantState> RegisterMerchantAsync(MerchantState merchant)
        {
            try
            {
                var passport = merchant.Passport;
                if (string.IsNullOrEmpty(passport))
                {
                    _logger.LogError("注册商户失败: Passport 为空");
                    return null;
                }

                var existingMerchant = await _merchantContext.QueryFirstOrDefaultAsync(m => m.Passport == passport && !m.IsDeleted);
                if (existingMerchant != null)
                {
                    _logger.LogWarning("商户已存在: Passport={Passport}, MerchantId={MerchantId}", passport, existingMerchant.Id);
                    merchant.MerchantId = existingMerchant.Id;
                    merchant.UserId = existingMerchant.UserId;
                    merchant.Passport = passport;
                    merchant.ShopName = existingMerchant.ShopName ?? merchant.ShopName;
                    merchant.ShopDescription = existingMerchant.ShopDescription ?? merchant.ShopDescription;
                    merchant.ContactPhone = existingMerchant.ContactPhone ?? merchant.ContactPhone;
                    merchant.IsVerified = existingMerchant.IsVerified;
                    _merchantState.State = merchant;
                    await _merchantState.WriteStateAsync();
                    return _merchantState.State;
                }

                var flowerUser = await _flowerUserContext.QueryFirstOrDefaultAsync(u => u.Passport == passport);

                Guid userId;
                if (flowerUser != null)
                {
                    userId = flowerUser.UserId;
                    _logger.LogInformation("从 Flower_User 找到用户: Passport={Passport}, UserId={UserId}", passport, userId);
                }
                else
                {
                    _logger.LogWarning("Flower_User 中未找到 Passport={Passport} 的用户，自动创建 Flower_User 记录", passport);

                    if (Guid.TryParse(passport, out var parsedGuid))
                    {
                        userId = parsedGuid;
                    }
                    else
                    {
                        userId = Guid.NewGuid();
                    }

                    var newUser = new FlowerUser
                    {
                        Passport = passport,
                        UserId = userId,
                        UserType = (int)FlowerUserType.Merchant,
                        DisplayName = merchant.ShopName ?? "商户",
                        Phone = merchant.ContactPhone ?? "",
                        Region = "默认",
                        SubscriptionLevel = (int)SubscriptionLevel.Free,
                        IsValid = true,
                        IsDeleted = false,
                        CreateTime = DateTime.Now
                    };

                    await _flowerUserContext.AddAsync(newUser);
                    _logger.LogInformation("自动创建 Flower_User: Passport={Passport}, UserId={UserId}", passport, userId);
                }

                var entity = new FlowerMerchant
                {
                    UserId = userId,
                    MerchantType = (int)merchant.MerchantType,
                    ShopName = merchant.ShopName,
                    ShopDescription = merchant.ShopDescription,
                    ContactPhone = merchant.ContactPhone,
                    BusinessLicense = merchant.BusinessLicense,
                    IsVerified = false,
                    Passport = passport,
                    CreateTime = DateTime.Now,
                    IsValid = true,
                    IsDeleted = false,
                    AuditStatus = 1,
                    Stage = 0,
                    //TODO 客户端还未提供实际数据，配合客户端进一步完善后取消该注释
                    IDCardUrl = "",
                    IDCardUrl2 = "",
                    BankAccountName = "",
                    BankAccountNumber = "",
                    BankName = "",
                    BankRegionId = null,
                    BusinessCategory = "",
                    BusinessLicenceNumber = "",
                    CompanyAddress = "",
                    CompanyName = "",
                    CompanyRegionId = 78,
                    IDCard = "",
                    RefuseReason = "",
                    ModifyPassport = "",

                };

                var result = await _merchantContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("注册商户失败: 数据库保存返回null");
                    return null;
                }

                if (flowerUser == null)
                {
                    flowerUser = await _flowerUserContext.QueryFirstOrDefaultAsync(u => u.Passport == passport);
                    if (flowerUser != null)
                    {
                        flowerUser.UserType = (int)FlowerUserType.Merchant;
                        flowerUser.MerchantId = result.Id;
                        await _flowerUserContext.UpdateAsync(flowerUser, flowerUser.Id);
                    }
                }
                else
                {
                    if (flowerUser.UserType == (int)FlowerUserType.Normal)
                    {
                        flowerUser.UserType = (int)FlowerUserType.Merchant;
                    }
                    flowerUser.MerchantId = result.Id;
                    await _flowerUserContext.UpdateAsync(flowerUser, flowerUser.Id);
                }

                merchant.MerchantId = result.Id;
                merchant.UserId = userId;
                merchant.Passport = passport;
                merchant.IsVerified = false;
                _merchantState.State = merchant;
                await _merchantState.WriteStateAsync();

                _logger.LogInformation("注册商户成功: MerchantId={MerchantId}, Passport={Passport}, UserId={UserId}",
                    result.Id, passport, userId);
                return _merchantState.State;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册商户失败");
                throw;
            }
        }

        public async Task<MerchantState> UpdateMerchantAsync(MerchantState merchant)
        {
            try
            {
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == merchant.MerchantId);
                if (entity == null)
                {
                    _logger.LogWarning("商户不存在: MerchantId={MerchantId}", merchant.MerchantId);
                    return null;
                }

                entity.ShopName = merchant.ShopName;
                entity.ShopDescription = merchant.ShopDescription;
                entity.ContactPhone = merchant.ContactPhone;
                await _merchantContext.UpdateAsync(entity, entity.Id);

                _merchantState.State = merchant;
                await _merchantState.WriteStateAsync();

                _logger.LogInformation("更新商户: MerchantId={MerchantId}", merchant.MerchantId);
                return _merchantState.State;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商户失败: MerchantId={MerchantId}", merchant.MerchantId);
                throw;
            }
        }

        public async Task<bool> VerifyMerchantAsync()
        {
            try
            {
                var state = _merchantState.State;
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == state.MerchantId);
                if (entity == null)
                {
                    _logger.LogWarning("商户不存在: MerchantId={MerchantId}", state.MerchantId);
                    return false;
                }

                entity.IsVerified = true;
                entity.VerifiedAt = DateTime.Now;
                await _merchantContext.UpdateAsync(entity, entity.Id);

                state.IsVerified = true;
                state.VerifiedAt = entity.VerifiedAt;
                await _merchantState.WriteStateAsync();

                _logger.LogInformation("认证商户: MerchantId={MerchantId}", state.MerchantId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "认证商户失败: MerchantId={MerchantId}", _merchantState.State.MerchantId);
                throw;
            }
        }

        public async Task<MerchantState> UpdateMerchantStageAsync(long merchantId, int stage, MerchantState merchant)
        {
            try
            {
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == merchantId);
                if (entity == null) return null;
                entity.Stage = stage;
                if (stage == 1)
                {
                    entity.CompanyName = merchant.ShopDescription;
                    entity.BusinessLicenceNumber = merchant.BusinessLicense;
                }
                else if (stage == 3)
                {
                    entity.ShopName = merchant.ShopName;
                    entity.ShopDescription = merchant.ShopDescription;
                    entity.ContactPhone = merchant.ContactPhone;
                }
                await _merchantContext.UpdateAsync(entity, entity.Id);
                _merchantState.State = merchant;
                await _merchantState.WriteStateAsync();
                return _merchantState.State;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "更新商户入驻步骤失败: {MerchantId}", merchantId);
                throw;
            }
        }

        public async Task<MerchantState> AuditMerchantAsync(long merchantId, bool approved, string refuseReason)
        {
            try
            {
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == merchantId);
                if (entity == null) return null;
                if (approved)
                {
                    entity.AuditStatus = (int)Horizon.Game.Message.Enums.ShopAuditStatus.Opened;
                    entity.EndDate = System.DateTime.Now.AddYears(1);
                }
                else
                {
                    entity.AuditStatus = (int)Horizon.Game.Message.Enums.ShopAuditStatus.Refused;
                    entity.RefuseReason = refuseReason;
                }
                await _merchantContext.UpdateAsync(entity, entity.Id);
                _merchantState.State.AuditStatus = entity.AuditStatus;
                await _merchantState.WriteStateAsync();
                return _merchantState.State;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "审核商户失败: {MerchantId}", merchantId);
                throw;
            }
        }

        public async Task<bool> FreezeMerchantAsync(long merchantId)
        {
            try
            {
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == merchantId);
                if (entity == null) return false;
                entity.AuditStatus = (int)Horizon.Game.Message.Enums.ShopAuditStatus.Frozen;
                await _merchantContext.UpdateAsync(entity, entity.Id);
                return true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "冻结商户失败: {MerchantId}", merchantId);
                throw;
            }
        }

        public async Task<bool> UnfreezeMerchantAsync(long merchantId)
        {
            try
            {
                var entity = await _merchantContext.QueryFirstOrDefaultAsync(e => e.Id == merchantId);
                if (entity == null) return false;
                entity.AuditStatus = (int)Horizon.Game.Message.Enums.ShopAuditStatus.Opened;
                await _merchantContext.UpdateAsync(entity, entity.Id);
                return true;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "解冻商户失败: {MerchantId}", merchantId);
                throw;
            }
        }

        public async Task<System.Collections.Generic.List<ShopShipperState>> GetShippersAsync(long merchantId)
        {
            var entities = await _shipperContext.QueryAsync(e => e.ShopId == merchantId && e.IsValid);
            return entities.Select(e => new ShopShipperState
            {
                Id = e.Id,
                ShopId = e.ShopId,
                ShipperTag = e.ShipperTag ?? "",
                ShipperName = e.ShipperName ?? "",
                RegionId = e.RegionId,
                Address = e.Address ?? "",
                TelPhone = e.TelPhone ?? "",
                IsDefaultSendGoods = e.IsDefaultSendGoods,
                Longitude = e.Longitude,
                Latitude = e.Latitude
            }).ToList();
        }

        public async Task<ShopShipperState> AddShipperAsync(long merchantId, ShopShipperState shipper)
        {
            var entity = new Horizon.Model.Flower.FlowerShopShipper
            {
                ShopId = merchantId,
                ShipperTag = shipper.ShipperTag,
                ShipperName = shipper.ShipperName,
                RegionId = shipper.RegionId,
                Address = shipper.Address,
                TelPhone = shipper.TelPhone,
                IsDefaultSendGoods = shipper.IsDefaultSendGoods,
                Longitude = shipper.Longitude,
                Latitude = shipper.Latitude
            };
            var result = await _shipperContext.AddAsync(entity);
            return result != null ? new ShopShipperState
            {
                Id = result.Id,
                ShopId = result.ShopId,
                ShipperTag = result.ShipperTag ?? "",
                ShipperName = result.ShipperName ?? "",
                RegionId = result.RegionId,
                Address = result.Address ?? "",
                TelPhone = result.TelPhone ?? "",
                IsDefaultSendGoods = result.IsDefaultSendGoods,
                Longitude = result.Longitude,
                Latitude = result.Latitude
            } : null;
        }

        public async Task<ShopShipperState> UpdateShipperAsync(ShopShipperState shipper)
        {
            var entity = await _shipperContext.QueryFirstOrDefaultAsync(e => e.Id == shipper.Id);
            if (entity == null) return null;
            entity.ShipperTag = shipper.ShipperTag;
            entity.ShipperName = shipper.ShipperName;
            entity.RegionId = shipper.RegionId;
            entity.Address = shipper.Address;
            entity.TelPhone = shipper.TelPhone;
            entity.IsDefaultSendGoods = shipper.IsDefaultSendGoods;
            entity.Longitude = shipper.Longitude;
            entity.Latitude = shipper.Latitude;
            await _shipperContext.UpdateAsync(entity, entity.Id);
            return shipper;
        }

        public async Task<bool> DeleteShipperAsync(long merchantId, long shipperId)
        {
            var entity = await _shipperContext.QueryFirstOrDefaultAsync(e => e.Id == shipperId && e.ShopId == merchantId);
            if (entity == null) return false;
            return await _shipperContext.DeletedAsync<FlowerShopShipper, long>(shipperId);
        }
    }
}
