using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model
{
    /// <summary>
    /// 扫脸结果佩戴眼镜的解析表
    /// </summary>
    [Table("Ai_GlassKV")]
    public class GlassKV : FacesKV
    {
        public GlassKV()
        {
            Id = Guid.NewGuid();
        }
    }
}
