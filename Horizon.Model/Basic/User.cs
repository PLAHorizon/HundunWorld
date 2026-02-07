using Horizon.Core.Abstract;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model
{

    /// <summary>
    /// 用户，泛类
    /// </summary>
    [Table("Basic_Sys_User"), Serializable]
    [EntityStorage("Basic")]
    public class User : UserModel<Guid>
    {
        public User()
        {
            Id = Guid.NewGuid();
            CreateDate = DateTime.Now;
        }

    }
}
