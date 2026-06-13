using System;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Model;
using Horizon.Model.Flower;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Grains
{
    public class FlowerUserDataSyncService
    {
        private readonly BasicEntityContext _basicContext;
        private readonly FlowerEntityContext _flowerContext;
        private readonly ILogger<FlowerUserDataSyncService> _logger;

        public FlowerUserDataSyncService(
            BasicEntityContext basicContext,
            FlowerEntityContext flowerContext,
            ILogger<FlowerUserDataSyncService> logger)
        {
            _basicContext = basicContext;
            _flowerContext = flowerContext;
            _logger = logger;
        }

        public async Task<int> SyncUsersAsync(string passport = null)
        {
            var synced = 0;

            try
            {
                var basicUsers = _basicContext.Users.AsQueryable();

                if (!string.IsNullOrEmpty(passport))
                {
                    var basicUser = await basicUsers.FirstOrDefaultAsync(u => u.PassportId == passport);
                    if (basicUser == null)
                    {
                        _logger.LogWarning("Basic 中未找到 Passport={Passport} 的用户", passport);
                        return 0;
                    }
                    await SyncSingleUserAsync(basicUser);
                    return 1;
                }
                else
                {
                    var allBasicUsers = await basicUsers.Where(u => u.IsValid).ToListAsync();

                    foreach (var basicUser in allBasicUsers)
                    {
                        try
                        {
                            await SyncSingleUserAsync(basicUser);
                            synced++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "同步用户 {Passport} 失败", basicUser.PassportId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "用户数据同步失败");
                throw;
            }

            _logger.LogInformation("用户数据同步完成，共同步 {Count} 个用户", synced);
            return synced;
        }

        private async Task SyncSingleUserAsync(User basicUser)
        {
            var existing = await _flowerContext.FlowerUsers
                .FirstOrDefaultAsync(u => u.Passport == basicUser.PassportId || u.UserId == basicUser.Id);

            if (existing != null)
            {
                existing.DisplayName = basicUser.NickName ?? basicUser.Name ?? "用户";
                existing.Phone = basicUser.Phone ?? "";
                existing.ModifyTime = DateTime.Now;
                existing.ModifyPassport = "SYSTEM_SYNC";
            }
            else
            {
                var flowerUser = new FlowerUser
                {
                    Passport = basicUser.PassportId,
                    UserId = basicUser.Id,
                    UserType = (int)FlowerUserType.Normal,
                    DisplayName = basicUser.NickName ?? basicUser.Name ?? "用户",
                    Phone = basicUser.Phone ?? "",
                    Region = "默认",
                    SubscriptionLevel = (int)SubscriptionLevel.Free,
                    IsValid = true,
                    IsDeleted = false,
                    CreateTime = basicUser.CreateDate ?? DateTime.Now
                };

                _flowerContext.FlowerUsers.Add(flowerUser);
            }

            await _flowerContext.SaveChangesAsync();
        }

        public async Task<long> EnsureUserExistsAsync(Guid userId, string passport)
        {
            var existing = await _flowerContext.FlowerUsers
                .FirstOrDefaultAsync(u => u.UserId == userId || u.Passport == passport);

            if (existing != null)
            {
                return existing.Id;
            }

            var basicUser = await _basicContext.Users.FirstOrDefaultAsync(u => u.Id == userId);

            var flowerUser = new FlowerUser
            {
                Passport = passport ?? basicUser?.PassportId ?? "SYSTEM",
                UserId = userId,
                UserType = (int)FlowerUserType.Normal,
                DisplayName = basicUser?.NickName ?? basicUser?.Name ?? "用户",
                Phone = basicUser?.Phone ?? "",
                Region = "默认",
                SubscriptionLevel = (int)SubscriptionLevel.Free,
                IsValid = true,
                IsDeleted = false,
                CreateTime = DateTime.Now
            };

            _flowerContext.FlowerUsers.Add(flowerUser);
            await _flowerContext.SaveChangesAsync();

            _logger.LogInformation("自动创建 Flower_User 记录: UserId={UserId}, Passport={Passport}", userId, passport);
            return flowerUser.Id;
        }

        public async Task<int> GetFlowerUserCountAsync()
        {
            return await _flowerContext.FlowerUsers.CountAsync(u => u.IsValid && !u.IsDeleted);
        }

        public async Task<int> GetBasicUserCountAsync()
        {
            return await _basicContext.Users.CountAsync(u => u.IsValid);
        }
    }
}
