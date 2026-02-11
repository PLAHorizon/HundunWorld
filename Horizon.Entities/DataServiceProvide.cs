using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Entities
{
    public class DataServiceProvide<Context, T, K> : IDataContext<Context, T, K> where T : BaseModel<K> where Context : DbContext, IDisposable
    {
        private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _transactionSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(3, 3); // 允许最多3个并发连接
        private readonly SemaphoreSlim _saveChangesSemaphore = new SemaphoreSlim(1, 1); // 保护 SaveChanges 操作
        private readonly SemaphoreSlim _qurySemaphore = new SemaphoreSlim(1, 1); // 
        private readonly object _disposeLock = new object();
        private volatile bool _disposed = false;
        private readonly object _healthCheckLock = new object();
        public DataServiceProvide(Context context)
        {
            DbCurrent = context;
            // Do not cache the DbConnection instance here. Use DbCurrent.Database.GetDbConnection() when needed.
            context.SaveChangesFailed += Context_SaveChangesFailed;
            context.ChangeTracker.StateChanged += ChangeTracker_StateChanged;
            context.ChangeTracker.Tracked += ChangeTracker_Tracked;
        }
        private void ChangeTracker_Tracked(object? sender, Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs e)
        {
            // 检查对象是否已释放
            if (_disposed) return;

            // 不再在事件处理程序中执行数据库操作，避免并发问题
            // 实体状态变更应该由调用方显式调用 SaveChanges
        }

        private void ChangeTracker_StateChanged(object? sender, Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangedEventArgs e)
        {
            // 检查对象是否已释放
            if (_disposed) return;

            if (e.NewState == EntityState.Modified)
            {
                try
                {
                    if (e.Entry.Entity is BaseNoneAggregateRootModel<K> entity)
                    {
                        e.Entry.Property(nameof(entity.ModifyTime)).CurrentValue = DateTime.Now;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 记录异常但不抛出
                }
            }
        }


        private void Context_SaveChangesFailed(object? sender, SaveChangesFailedEventArgs e)
        {
            // Do not dispose the connection held by the DbContext here. Disposing the shared connection
            // can leave the DbContext in an unusable state for subsequent operations (e.g. ReloadAsync).
            try
            {
                var conn = DbCurrent.Database.GetDbConnection();
                if (conn.State != ConnectionState.Closed)
                {
                    // Close the connection instead of disposing it. Let the DbContext manage the underlying connection lifecycle.
                    DbCurrent.Database.CloseConnection();
                }
            }
            catch
            {
                // swallow to avoid throwing from the event handler; logging can be added if desired
            }
        }

        /// <summary>
        /// DbContext
        /// </summary>
        public Context DbCurrent { get; }
        // Return the current DbConnection from the DbContext instead of caching an instance.
        public IDbConnection DbConnection => DbCurrent.Database.GetDbConnection();

        public string ConnectionStr => DbConnection.ConnectionString;

        public DataContextType ContextType => DataContextType.SqlServer;

        public async Task<T> AddAsync([NotNull] T entity)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                var model = await DbCurrent.Set<T>().AddAsync(entity);
                try
                {
                    await DbCurrent.SaveChangesAsync();
                    return model.Entity;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // 处理添加时的并发冲突（虽然罕见，但可能发生）
                    Log.Error(Log.CommRepository, $"添加操作并发冲突: {ex.Message}");
                    throw; // 重新抛出，因为添加操作的并发冲突通常需要特殊处理
                }
            }
            finally
            {
                // 获取写操作信号量
                _writeSemaphore.Release();
            }
        }

        public async Task<bool> AddRangeAsync([NotNull] IList<T> entities)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取事务信号量
            await _transactionSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                using var scope = DbCurrent.Database.GetDbConnection().BeginTransaction();
                try
                {
                    await DbCurrent.Set<T>().AddRangeAsync(entities);
                    try
                    {
                        await DbCurrent.SaveChangesAsync();
                        scope.Commit();
                        return true;
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        scope.Rollback();
                        // 处理批量添加时的并发冲突
                        Log.Error(Log.CommRepository, $"批量添加操作并发冲突: {ex.Message}");
                        throw; // 重新抛出，因为批量添加的并发冲突需要特殊处理
                    }
                }
                catch (Exception)
                {
                    scope.Rollback();
                    throw; // 重新抛出异常以保持原有行为
                }
                finally
                {
                    try
                    {
                        var conn = DbCurrent.Database.GetDbConnection();
                        if (conn.State != ConnectionState.Closed)
                            DbCurrent.Database.CloseConnection();
                    }
                    catch { }
                }
            }
            finally
            {
                _transactionSemaphore.Release();
            }
        }

        /// <summary>
        /// 禁用数据（软删除/标记无效）
        /// </summary>
        public async Task<bool> DisableAsync([NotNull] T entity, [NotNull] K id)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                var model = await DbCurrent.Set<T>().FindAsync(id);
                if (model == null) return false;

                model.IsValid = false;
                if (model is ISoftDeleted softDeleted)
                {
                    softDeleted.IsDeleted = true;
                }

                DbCurrent.Entry(model).Property(x => x.Id).IsModified = false;
                DbCurrent.Entry(model).State = EntityState.Modified;

                try
                {
                    return (await DbCurrent.SaveChangesAsync()) > 0;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    Log.Error(Log.CommRepository, $"禁用操作并发冲突: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(Log.CommRepository, ex.Message);
                return false;
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// 批量禁用数据（软删除/标记无效）
        /// </summary>
        public async Task<bool> DisableRangeAsync([NotNull] IList<T> entities)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                foreach (var entity in entities)
                {
                    var model = await DbCurrent.Set<T>().FindAsync(entity.Id);
                    if (model == null) continue;

                    model.IsValid = false;
                    if (model is ISoftDeleted softDeleted)
                    {
                        softDeleted.IsDeleted = true;
                    }

                    DbCurrent.Entry(model).Property(x => x.Id).IsModified = false;
                    DbCurrent.Entry(model).State = EntityState.Modified;
                }

                try
                {
                    return (await DbCurrent.SaveChangesAsync()) > 0;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    Log.Error(Log.CommRepository, $"批量禁用操作并发冲突: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(Log.CommRepository, ex.Message);
                return false;
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task<IQueryable<T>> QueryAsync([NotNull] Expression<Func<T, bool>> condition, bool isTracking = false)
        {
            DbContextHealthCheck();
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取事务信号量
            await _qurySemaphore.WaitAsync();
            try
            {
                return await Task.FromResult((isTracking ? DbCurrent.Set<T>().AsQueryable().AsTracking() :
                                          DbCurrent.Set<T>().AsQueryable().AsNoTracking())
                                         .Where(condition));
            }
            finally
            {
                _qurySemaphore.Release();
            }
        }
        public async Task<IList<DTO>> QueryAsync<DTO>([NotNull] Expression<Func<T, bool>> condition, [NotNull] Func<T, DTO> selecterAction)
        {
            DbContextHealthCheck();
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取事务信号量
            await _qurySemaphore.WaitAsync();
            try
            {
                return await Task.FromResult(DbCurrent.Set<T>().AsQueryable().AsNoTracking()
                                        .Where(condition).Select(selecterAction).ToList());
            }
            finally
            {
                _qurySemaphore.Release();
            }
        }

        /// <summary>
        /// 数据上下文链接状态主动检测
        /// </summary>
        private void DbContextHealthCheck()
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_healthCheckLock)
            {
                var conn = DbCurrent.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                {
                    DbCurrent.Database.OpenConnection();
                }
            }
        }

        public async Task<T> QueryFirstOrDefaultAsync([NotNull] Expression<Func<T, bool>> condition, bool isTracking = false)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取事务信号量
            await _qurySemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
#pragma warning disable CS8603 // 可能返回 null 引用。
                return await (isTracking ? DbCurrent.Set<T>().AsQueryable().AsTracking(QueryTrackingBehavior.TrackAll) :
                                           DbCurrent.Set<T>().AsQueryable().AsNoTracking())
                                          .FirstOrDefaultAsync(condition);
#pragma warning restore CS8603 // 可能返回 null 引用。
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
            finally
            {
                _qurySemaphore.Release();
            }
        }

        public async Task<TDTO> QueryFirstOrDefaultAsync<TDTO>([NotNull] Expression<Func<T, bool>> condition, [NotNull] Func<T, TDTO> selecterAction)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取事务信号量
            await _qurySemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
#pragma warning disable CS8603 // 可能返回 null 引用。
                return await Task.FromResult(DbCurrent.Set<T>().AsQueryable().AsNoTracking()
                                            .Where(condition)
                                            .Select(selecterAction).AsQueryable()
                                            .FirstOrDefault());
#pragma warning restore CS8603 // 可能返回 null 引用。
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
            finally
            {
                _qurySemaphore.Release();
            }
        }

        /// <summary>
        /// 获取满足条件的记录数量（在数据库端执行COUNT）
        /// </summary>
        public async Task<int> CountAsync([NotNull] Expression<Func<T, bool>> condition)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取查询信号量
            await _qurySemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                return await DbCurrent.Set<T>().AsQueryable().AsNoTracking()
                                      .CountAsync(condition);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
            finally
            {
                _qurySemaphore.Release();
            }
        }

        /// <summary>
        /// 物理删除单条数据
        /// </summary>
        public async Task<bool> RemoveAsync([NotNull] T entity, [NotNull] K id)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                var model = await DbCurrent.Set<T>().FindAsync(id);
                if (model == null) return false;

                DbCurrent.Remove(model);

                try
                {
                    return (await DbCurrent.SaveChangesAsync()) > 0;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    Log.Error(Log.CommRepository, $"删除操作并发冲突: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(Log.CommRepository, ex.Message);
                return false;
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// 批量物理删除数据
        /// </summary>
        public async Task<bool> RemoveRangeAsync([NotNull] IList<T> entities)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取事务信号量
            await _transactionSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                using var scope = DbCurrent.Database.GetDbConnection().BeginTransaction();
                try
                {
                    foreach (var entity in entities)
                    {
                        var model = await DbCurrent.Set<T>().FindAsync(entity.Id);
                        if (model != null)
                        {
                            DbCurrent.Remove(model);
                        }
                    }

                    try
                    {
                        await DbCurrent.SaveChangesAsync();
                        scope.Commit();
                        return true;
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        scope.Rollback();
                        Log.Error(Log.CommRepository, $"批量删除操作并发冲突: {ex.Message}");
                        return false;
                    }
                }
                catch (Exception)
                {
                    scope.Rollback();
                    throw;
                }
                finally
                {
                    try
                    {
                        var conn = DbCurrent.Database.GetDbConnection();
                        if (conn.State != ConnectionState.Closed)
                            DbCurrent.Database.CloseConnection();
                    }
                    catch { }
                }
            }
            finally
            {
                _transactionSemaphore.Release();
            }
        }


        public async Task<bool> UpdateAsync([NotNull] T entity, [NotNull] K id)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                var model = await DbCurrent.Set<T>().FindAsync(id);
                if (model == null) return false;

                DbCurrent.Entry<T>(model).State = EntityState.Modified;
                DbCurrent.Entry<T>(model).CurrentValues.SetValues(entity);
                DbCurrent.Entry(model).Property(x => x.Id).IsModified = false;
                DbCurrent.Entry(model).State = EntityState.Modified;

                try
                {
                    await DbCurrent.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // 处理并发冲突：重新加载实体并重试更新
                    foreach (var entry in ex.Entries)
                    {
                        if (entry.Entity is T)
                        {
                            await entry.ReloadAsync();
                            // 重新应用更改（除了并发令牌字段）
                            var databaseValues = entry.CurrentValues;
                            var originalValues = entry.OriginalValues;
                            var proposedValues = entry.CurrentValues.Clone();

                            foreach (var property in proposedValues.Properties)
                            {
                                var proposedValue = proposedValues[property];
                                var originalValue = originalValues[property];
                                var databaseValue = databaseValues[property];

                                // 只更新非并发令牌字段
                                if (!property.IsConcurrencyToken || 
                                    Equals(originalValue, databaseValue))
                                {
                                    proposedValues[property] = proposedValue;
                                }
                            }

                            entry.CurrentValues.SetValues(proposedValues);
                        }
                    }

                    // 重试保存
                    await DbCurrent.SaveChangesAsync();
                    return true;
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }
        public async Task<bool> UpdateRangeAsync([NotNull] IList<T> entities)
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                using (var scope = DbCurrent.Database.GetDbConnection().BeginTransaction())
                {
                    try
                    {
                        foreach (var item in entities)
                        {
                            var model = await DbCurrent.Set<T>().FindAsync(item.Id);
                            if (model == null) continue;
                            DbCurrent.Entry<T>(model).State = EntityState.Modified;
                            DbCurrent.Entry<T>(model).CurrentValues.SetValues(item);
                            DbCurrent.Entry(model).Property(x => x.Id).IsModified = false;
                            DbCurrent.Entry(model).State = EntityState.Modified;
                        }
                        
                        try
                        {
                            await DbCurrent.SaveChangesAsync();
                            scope.Commit();
                            return true;
                        }
                        catch (DbUpdateConcurrencyException ex)
                        {
                            scope.Rollback();
                            // 对于批量操作，并发冲突处理更复杂，通常需要重试整个操作或记录失败
                            Log.Error(Log.CommRepository, $"并发冲突在批量更新中: {ex.Message}");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        scope.Rollback();
                        Log.Error(Log.CommRepository, $"批量更新失败: {ex.Message}");
                        return false;
                    }
                    finally
                    {
                        try
                        {
                            var conn = DbCurrent.Database.GetDbConnection();
                            if (conn.State != ConnectionState.Closed)
                                DbCurrent.Database.CloseConnection();
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// 删除数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="K"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<bool> DeletedAsync<T, K>([NotNull] K id) where T : BaseModel<K>
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                try
                {
                    var model = await DbCurrent.Set<T>().FindAsync(id);
                    if (model == null) return false;

                    if (model is ISoftDeleted)
                    {
                        DbCurrent.Entry(model).Property(x => x.Id).IsModified = false;
                        DbCurrent.Entry(model).State = EntityState.Modified;
                        ((ISoftDeleted)model).IsDeleted = true;
                    }
                    else
                    {
                        DbCurrent.Remove(model);
                    }
                    
                    try
                    {
                        return (await DbCurrent.SaveChangesAsync()) > 0;
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        // 处理删除时的并发冲突
                        Log.Error(Log.CommRepository, $"删除操作并发冲突: {ex.Message}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Log.CommRepository, ex.Message);
                    return false;
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }
        /// <summary>
        /// 批量删除数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="K"></typeparam>
        /// <param name="ids"></param>
        /// <returns></returns>
        public async Task<bool> DeletedsAsync<T, K>([NotNull] IList<K> ids) where T : BaseModel<K>
        {
            // 检查对象是否已释放
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 获取写操作信号量
            await _writeSemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                try
                {
                    var models = DbCurrent.Set<T>().Where(m => ids.Contains(m.Id));
                    if (models == null || models.Count() == 0) return false;

                    if (models.First() is ISoftDeleted)
                    {
                        foreach (var model in models)
                        {
                            DbCurrent.Entry(model).Property(x => x.Id).IsModified = false;
                            DbCurrent.Entry(model).State = EntityState.Modified;
                            model.IsValid = false;
                            ((ISoftDeleted)model).IsDeleted = true;
                        }

                    }
                    else
                    {
                        DbCurrent.RemoveRange(models);
                    }
                    try
                    {
                        return (await DbCurrent.SaveChangesAsync()) > 0;
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        // 处理批量删除时的并发冲突
                        Log.Error(Log.CommRepository, $"批量删除操作并发冲突: {ex.Message}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Log.CommRepository, ex.Message);
                    return false;
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// 获取满足条件的记录数量（使用数据库级别的COUNT操作）
        /// </summary>
        public async Task<int> CountAsync([NotNull] Expression<Func<T, bool>> condition)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _qurySemaphore.WaitAsync();
            try
            {
                DbContextHealthCheck();
                return await DbCurrent.Set<T>().AsQueryable().AsNoTracking().CountAsync(condition);
            }
            finally
            {
                _qurySemaphore.Release();
            }
        }

        public void Dispose()
        {
            // 双重检查锁定模式
            if (_disposed)
                return;

            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                try
                {
                    // Dispose only the DbContext. Do not dispose the underlying connection instance directly here.
                    try
                    {
                        DbCurrent?.Dispose();
                    }
                    catch { }
                }
                finally
                {
                    // 释放信号量资源
                    _writeSemaphore?.Dispose();
                    _transactionSemaphore?.Dispose();
                    _connectionSemaphore?.Dispose();
                    _saveChangesSemaphore?.Dispose();

                    _disposed = true;
                }
            }
        }
    }
}
