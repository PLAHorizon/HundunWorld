using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Entities
{
    /// <summary>
    /// 数据上下文扩展类
    /// </summary>
    public static class DbContextExtension
    {
        /// <summary>
        /// 根据条件动态添加筛选条件。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="condition">条件是否成立</param>
        /// <param name="expression">筛选条件表达式</param>
        /// <returns>筛选后的数据源</returns>
        public static IQueryable<T> WhereIf<T>(this IQueryable<T> source, bool condition, Expression<Func<T, bool>> expression) where T : class, new()
        {
            if (condition)
                return source.Where(expression);
            else return source;
        }

        /// <summary>
        /// 根据条件动态添加导航属性的加载。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <typeparam name="TInclude">导航属性类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="condition">条件是否成立</param>
        /// <param name="include">导航属性表达式</param>
        /// <returns>包含导航属性的数据源</returns>
        public static IQueryable<T> IncludeIf<T, TInclude>(this IQueryable<T> source, bool condition, Expression<Func<T, TInclude>> include) where T : class, new()
        {
            if (condition)
                return source.Include(include);
            else return source;
        }

        /// <summary>
        /// 根据条件动态添加导航属性的二级加载。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <typeparam name="TInclude">一级导航属性类型</typeparam>
        /// <typeparam name="TProperty">二级导航属性类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="condition">条件是否成立</param>
        /// <param name="include">二级导航属性表达式</param>
        /// <returns>包含二级导航属性的数据源</returns>
        public static IQueryable<T> ThenIncludeIf<T, TInclude, TProperty>(this IIncludableQueryable<T, TInclude> source, bool condition, Expression<Func<TInclude, TProperty>> include) where T : class, new()
        {
            if (condition)
                return source.ThenInclude(include);
            else return source;
        }

        /// <summary>
        /// 根据条件动态排序。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <typeparam name="TOrder">排序字段类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="isdesc">是否降序</param>
        /// <param name="keySeletor">排序字段表达式</param>
        /// <returns>排序后的数据源</returns>
        public static IQueryable<T> OrderBy<T, TOrder>(this IQueryable<T> source, bool isdesc, Expression<Func<T, TOrder>> keySeletor) where T : class, new()
        {
            if (!isdesc)
                return source.OrderBy(keySeletor);
            else return source.OrderByDescending(keySeletor);
        }

        /// <summary>
        /// 分页查询。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <typeparam name="Q">分页查询参数类型</typeparam>
        /// <param name="source">数据源</param>
        /// <param name="query">分页查询参数</param>
        /// <param name="action">分页结果处理函数</param>
        /// <returns>分页结果</returns>
        public static IPageItems<T> PageBy<T, Q>(this IQueryable<T> source, Q query, Func<int, List<T>, IPageItems<T>> action) where T : class, new() where Q : IPageQuery
        {
            SetPageQueryDefault(query);
            int total = source.Count();
            var result = source.Skip((query.PageNumber - 1) * query.PageSize)?.Take(query.PageSize)?.ToList() ?? new List<T>();
            return action(total, result);
        }

        /// <summary>
        /// 设置分页查询的默认值。
        /// </summary>
        /// <typeparam name="T">分页查询参数类型</typeparam>
        /// <param name="query">分页查询参数</param>
        /// <returns>设置默认值后的分页查询参数</returns>
        private static T SetPageQueryDefault<T>(T query) where T : IPageQuery
        {
            query.PageNumber = Math.Max(1, query.PageNumber);
            query.PageSize = Math.Max(5, query.PageSize);
            query.PageSize = Math.Min(1000, query.PageSize);
            return query;
        }
    }
}
