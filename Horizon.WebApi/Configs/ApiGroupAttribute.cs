using System;

namespace Horizon.WebApi.Configs
{
    public class ApiGroupAttribute : Attribute
    {
        /// <summary>
        /// 分组名
        /// </summary>
        public string Name { get; set; }
        public ApiGroupAttribute(string name)
        {
            Name = name;
        }

    }
}
