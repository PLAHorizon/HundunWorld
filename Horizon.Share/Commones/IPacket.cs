using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Commones
{
    public interface IPacket
    {
        byte[] Serialize();

        void Deserialize(byte[] data);
    }
}
