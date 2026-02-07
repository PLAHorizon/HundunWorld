using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Game.Core.Database
{
   public  class LiteDbBaseModel<K>
    {
        public K Id { get; set; }
    }
}
