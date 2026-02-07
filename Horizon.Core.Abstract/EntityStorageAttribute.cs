using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 实体存储位置标识
    /// </summary>
    [System.AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class EntityStorageAttribute : Attribute
    {
        /// <summary>
        /// 存储库名称
        /// </summary>
        readonly string storageName;


        public EntityStorageAttribute(string positionalString)
        {
            this.storageName = positionalString;
        }

        public string StorageName
        {
            get { return storageName; }
        }
    }
}
