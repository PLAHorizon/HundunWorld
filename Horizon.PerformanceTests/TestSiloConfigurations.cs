using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model;
using Horizon.Model.Basic;
using Horizon.Model.GameModel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Horizon.PerformanceTests;

public class TestSiloConfigurations : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("GameStore")
            .ConfigureServices(services =>
            {
                services.AddAutoMapper(cfg => cfg.AddMaps("Horizon.Mapper"));

                services.AddScoped<IDataContext<BasicEntityContext, Passport, string>, InMemoryDataContext<BasicEntityContext, Passport, string>>();
                services.AddScoped<IDataContext<BasicEntityContext, PassportIds, string>, InMemoryDataContext<BasicEntityContext, PassportIds, string>>();
                services.AddScoped<IDataContext<BasicEntityContext, User, Guid>, InMemoryDataContext<BasicEntityContext, User, Guid>>();
                services.AddScoped<IDataContext<BasicEntityContext, PassportFlag, int>, InMemoryDataContext<BasicEntityContext, PassportFlag, int>>();
                services.AddScoped<IDataContext<GameEntityContext, UserEntity, long>, InMemoryDataContext<GameEntityContext, UserEntity, long>>();
                services.AddScoped<IDataContext<GameEntityContext, CharacterEntity, long>, InMemoryDataContext<GameEntityContext, CharacterEntity, long>>();
            });
    }
}

internal sealed class InMemoryDataContext<TContext, TEntity, TKey> : IDataContext<TContext, TEntity, TKey>
    where TEntity : BaseModel<TKey>
    where TKey : notnull
{
    private static readonly ConcurrentDictionary<TKey, TEntity> Store = new();

    public TContext DbCurrent => throw new NotSupportedException();

    public IDbConnection DbConnection => throw new NotSupportedException();

    public string ConnectionStr => "in-memory";

    public DataContextType ContextType => DataContextType.SqlServer;

    public Task<TEntity> AddAsync(TEntity entity)
    {
        EnsureEntityId(entity);
        Store[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<bool> AddRangeAsync(IList<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            EnsureEntityId(entity);
            Store[entity.Id] = entity;
        }

        return Task.FromResult(true);
    }

    public Task<bool> UpdateAsync(TEntity entity, TKey id)
    {
        entity.Id = id;
        Store[id] = entity;
        return Task.FromResult(true);
    }

    public Task<bool> UpdateRangeAsync(IList<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            EnsureEntityId(entity);
            Store[entity.Id] = entity;
        }

        return Task.FromResult(true);
    }

    public Task<bool> DeletedAsync<TEntity, TKey>(TKey id) where TEntity : BaseModel<TKey>
    {
        return Task.FromResult(true);
    }

    public Task<bool> DeletedsAsync<TEntity, TKey>(IList<TKey> ids) where TEntity : BaseModel<TKey>
    {
        return Task.FromResult(true);
    }

    public Task<IQueryable<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> condition, bool isTracking = false)
    {
        var predicate = condition.Compile();
        var result = Store.Values.Where(predicate).AsQueryable();
        return Task.FromResult(result);
    }

    public Task<IList<TDto>> QueryAsync<TDto>(Expression<Func<TEntity, bool>> condition, Func<TEntity, TDto> selecterAction)
    {
        var predicate = condition.Compile();
        var result = Store.Values.Where(predicate).Select(selecterAction).ToList();
        return Task.FromResult<IList<TDto>>(result);
    }

    public Task<TEntity> QueryFirstOrDefaultAsync(Expression<Func<TEntity, bool>> condition, bool isTracking = false)
    {
        var predicate = condition.Compile();
        var result = Store.Values.FirstOrDefault(predicate);
        return Task.FromResult(result!);
    }

    public Task<TDto> QueryFirstOrDefaultAsync<TDto>(Expression<Func<TEntity, bool>> condition, Func<TEntity, TDto> selecterAction)
    {
        var predicate = condition.Compile();
        var entity = Store.Values.FirstOrDefault(predicate);
        var result = entity == null ? default : selecterAction(entity);
        return Task.FromResult(result!);
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> condition)
    {
        var predicate = condition.Compile();
        var result = Store.Values.Count(predicate);
        return Task.FromResult(result);
    }

    public void Dispose()
    {
    }

    private static void EnsureEntityId(TEntity entity)
    {
        if (!EqualityComparer<TKey>.Default.Equals(entity.Id, default!))
        {
            return;
        }

        object generated = typeof(TKey) switch
        {
            var t when t == typeof(Guid) => Guid.NewGuid(),
            var t when t == typeof(long) => DateTime.UtcNow.Ticks,
            var t when t == typeof(int) => Random.Shared.Next(1, int.MaxValue),
            var t when t == typeof(string) => Guid.NewGuid().ToString("N"),
            _ => throw new InvalidOperationException($"Unsupported key type: {typeof(TKey).FullName}")
        };

        entity.Id = (TKey)generated;
    }
}
