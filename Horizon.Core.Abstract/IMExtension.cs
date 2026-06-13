using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core.Abstract
{
    /// <summary>
    ///聊天扩展类
    /// </summary>
    public static class IMExtension
    {
        private static readonly int _messageOffset = MessageFormat.HeadLength + MessageFormat.GUID + MessageFormat.Length;
        public static int MessageOffset => _messageOffset;
        /// <summary>
        /// 设置聊天消息字节数组
        /// </summary>
        /// <param name="message">聊天消息实例</param>
        /// <returns></returns>
        public static byte[] SetIMMessage(this IMMessage message, byte method)
        {
            byte[] head = new byte[4] { 1, 0, 0, method };
            byte[] guid = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString().Replace("-", ""));
            byte[] nullbyte = new byte[64];
            byte[] buff = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            byte[] data = new byte[MessageFormat.HeadLength + MessageFormat.GUID + MessageFormat.Length + buff.Length];
            for (int i = 0; i < data.Length; i++)
            {
                if (head.Length > i)//头
                    data[i] = head[i];
                else
                {
                    if (guid.Length > i - head.Length)//消息转发标识
                        data[i] = guid[i - head.Length];
                    else
                    {
                        if (nullbyte.Length > i - head.Length - guid.Length)//预留空位
                            data[i] = nullbyte[i - head.Length - guid.Length];
                        else // 消息内容
                            data[i] = buff[i - head.Length - guid.Length - nullbyte.Length];
                    }
                }
            }
            return data;
        }

        /// <summary>
        /// 设置聊天消息字节数组
        /// </summary>
        /// <param name="message">聊天消息实例 Id</param>
        /// <returns></returns>
        public static byte[] SetIMMessage(this string message, byte method)
        {
            byte[] head = new byte[4] { 1, 0, 0, method };
            byte[] guid = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString().Replace("-", ""));
            byte[] nullbyte = new byte[64];
            byte[] buff = Encoding.UTF8.GetBytes(message);
            byte[] data = new byte[MessageFormat.HeadLength + MessageFormat.GUID + MessageFormat.Length + buff.Length];
            for (int i = 0; i < data.Length; i++)
            {
                if (head.Length > i)//头
                    data[i] = head[i];
                else
                {
                    if (guid.Length > i - head.Length)//消息转发标识
                        data[i] = guid[i - head.Length];
                    else
                    {
                        if (nullbyte.Length > i - head.Length - guid.Length)//预留空位
                            data[i] = nullbyte[i - head.Length - guid.Length];
                        else // 消息内容
                            data[i] = buff[i - head.Length - guid.Length - nullbyte.Length];
                    }
                }
            }
            return data;
        }

        /// <summary>
        /// 获取聊天消息
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static IMMessage GetIMMessage(this byte[] data, long offset, long size)
        {
            try
            {
                IMMessage iMMessage = JsonConvert.DeserializeObject<IMMessage>(Encoding.UTF8.GetString(data,
                                                                                   (int)offset, (int)size));

                return iMMessage;
            }
            catch (Exception)
            {
                return null;
            }
        }
        /// <summary>
        /// 获取聊天消息状态
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static SimpleMessageState<T> GetIMMessageState<T>(this byte[] data, long offset, long size)
        {
            try
            {
                SimpleMessageState<T> iMMessage = JsonConvert.DeserializeObject<SimpleMessageState<T>>(Encoding.UTF8.GetString(data,
                                                                                   (int)offset, (int)size));
                return iMMessage;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
