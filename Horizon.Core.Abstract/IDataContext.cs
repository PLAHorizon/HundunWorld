using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 数据上下文接口
    /// </summary>
    /// <typeparam name="T">数据实体类型类型参数</typeparam>
    /// <typeparam name="K">数据实体类数据主键类型</typeparam>
    public interface IDataContext<Context, T, K> : IDisposable where T : BaseModel<K>
    {
        /// <summary>
        /// DbContext
        /// </summary>
        Context DbCurrent { get; }
        /// <summary>
        /// 与数据源的开放连接对象
        /// </summary>
        IDbConnection DbConnection { get; }
        /// <summary>
        /// 数据库链接名
        /// </summary>
        string ConnectionStr { get; }
        /// <summary>
        /// 数据库类型
        /// </summary>
        DataContextType ContextType { get; }
        /// <summary>
        /// 新建
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        Task<T> AddAsync([NotNull] T entity);
        /// <summary>
        /// 批量新建
        /// </summary>
        /// <param name="entities">待添加的实体集</param>
        /// <returns></returns>
        Task<bool> AddRangeAsync([NotNull] IList<T> entities);
        /// <summary>
        /// 更新
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        Task<bool> UpdateAsync([NotNull] T entity, [NotNull] K id);
        /// <summary>
        /// 批量更新
        /// </summary>
        /// <param name="entities">待更新的实体集</param>
        /// <returns></returns>
        Task<bool> UpdateRangeAsync([NotNull] IList<T> entities);
        /// <summary>
        /// 删除数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="K"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<bool> DeletedAsync<TEntity, TKey>([NotNull] TKey id) where TEntity : BaseModel<TKey>;

        /// <summary>
        /// 批量删除数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="K"></typeparam>
        /// <param name="ids"></param>
        /// <returns></returns>
        Task<bool> DeletedsAsync<TEntity, TKey>([NotNull] IList<TKey> ids) where TEntity : BaseModel<TKey>;
        /// <summary>
        /// 条件筛选的结果集
        /// </summary>
        /// <typeparam name="T">实体类</typeparam>
        /// <param name="condition">筛选条件</param>
        /// <param name="isTracking">是否追踪</param>
        /// <returns></returns>
        Task<IQueryable<T>> QueryAsync([NotNull] Expression<Func<T, bool>> condition, bool isTracking = false);
        /// <summary>
        /// 条件筛选的结果集
        /// </summary>
        /// <typeparam name="DTO">选择返回类型类型参数</typeparam>
        /// <param name="condition">筛选条件</param>
        /// <param name="selecterAction">数据选择器</param>
        /// <returns></returns>
        Task<IList<DTO>> QueryAsync<DTO>([NotNull] Expression<Func<T, bool>> condition, [NotNull] Func<T, DTO> selecterAction);
        /// <summary>
        /// 获取一条数据
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="isTracking">是否追踪</param>
        /// <returns></returns>
        Task<T> QueryFirstOrDefaultAsync([NotNull] Expression<Func<T, bool>> condition, bool isTracking = false);
        /// <summary>
        /// 获取一条数据
        /// </summary>
        /// <typeparam name="TDTO">数据传输类型类型参数</typeparam>
        /// <param name="condition">筛选条件</param>
        /// <param name="selecterAction">数据传输类型数据选择器</param>
        /// <returns></returns>
        Task<TDTO> QueryFirstOrDefaultAsync<TDTO>([NotNull] Expression<Func<T, bool>> condition, [NotNull] Func<T, TDTO> selecterAction);

        /// <summary>
        /// 获取满足条件的记录数量（使用数据库级别的COUNT操作）
        /// </summary>
        /// <param name="condition">筛选条件</param>
        /// <returns>满足条件的记录数</returns>
        Task<int> CountAsync([NotNull] Expression<Func<T, bool>> condition);
    }
}
