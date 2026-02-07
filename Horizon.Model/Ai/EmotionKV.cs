using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Model
{
    /// <summary>
    /// 扫脸情绪键值对
    /// </summary>
    [Table("Ai_EmotionKV")]
    public class EmotionKV : FacesKV
    {
        public EmotionKV()
        {
            Id = Guid.NewGuid();
        }
    }
}
