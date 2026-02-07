using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;
using Horizon.Share.Enums.Game;
using Horizon.Share.Utils;

namespace Horizon.Share.Commones
{
    public class MessagePacket : IPacket
    {
        //static uint _checkbit;
        //public MessagePacket()
        //{
        //    if (_checkbit >= uint.MaxValue) _checkbit = 0;
        //    Checkbit = ++_checkbit;
        //}
        /// <summary>
        /// 包头标志，用于校验 4byte
        /// </summary>
        public uint Checkbit { get; set; }

        /// <summary>
        /// 4个byte表示package长度
        /// </summary>
        public uint Length { get; set; }

        /// <summary>
        /// 4个byte表示commandId
        /// </summary>
        public GameMessageCode CommandId { get; set; }

        /// <summary>
        /// package内容
        /// </summary>
        public required object Body { get; set; }

        public void Deserialize(byte[] data)
        {
            //Checkbit = 0x1F;
            Body = SerializerUtilitys.DeSerialize<MessagePacket>(data).Body;

        }

        public byte[] Serialize()
        {
            // Checkbit = 0x1F;
            Length = 4 * 3;
            var bodyArray = SerializerUtilitys.Serialize(Body);
            Length += (uint)bodyArray.Length;

            return SerializerUtilitys.Serialize(this);
        }
    }
}
