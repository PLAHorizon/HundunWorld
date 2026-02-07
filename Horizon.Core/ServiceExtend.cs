using Horizon.Core.Abstract;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Core
{
    /// <summary>
    /// 服务扩展类
    /// </summary>
    public static class ServiceExtend
    {
        /// <summary>
        /// 解析传输的参数
        /// </summary>
        /// <typeparam name="TemplateClass">参数模板类</typeparam>
        /// <typeparam name="Result">参数实体类</typeparam>
        /// <param name="buff">传输的二进制参数</param>
        /// <param name="messageModel">模板参数实例</param>
        /// <param name="instance">参数实体实例</param>
        /// <returns>返回值 Result:false 解析失败,true:解析成功可以继续</returns>
        public static (byte[] Message, bool Result) AnalysisParame<TemplateClass, T>(this byte[] buff, out TemplateClass messageModel, out T instance) where TemplateClass : TransactionMessage<T>
        {
            messageModel = default;
            instance = default;
            bool error = false;
            var header = new Header();
        RESULT:
            if (error) return (new TransactionMessage<T>
            {
                Body = default,
                Header = header,
                Message = new StateMessage
                {
                    Code = ServiceResultCode.StateCode_400,
                    Message = "参数格式错误"
                }
            }.ObjectToBytesForJson(), false);
            try
            {
                messageModel = buff.BytesToObjectForJson<TemplateClass>(); // JsonConvert.DeserializeObject<TemplateClass>(Encoding.UTF8.GetString(buff));                
                if (messageModel == null || messageModel.Header == null)
                {
                    error = true;
                    goto RESULT;
                }
                header.MessageType = messageModel.Header.MessageType == RRPC.Request ? RRPC.Response : RRPC.Push;
                instance = messageModel.Body;
                if (instance == null)
                {
                    error = true;
                    goto RESULT;
                }
                return (null, true);
            }
            catch (Exception ex)
            {
                Log.Error(Log.CommRepository, ex.Message);
                error = true;
                goto RESULT;
            }
        }

        /// <summary>
        /// 改进的参数解析方法，支持更详细的错误处理
        /// </summary>
        public static (byte[] Message, bool Result) ImprovedAnalysisParame<TemplateClass, T>(this byte[] buff, out TemplateClass messageModel, out T instance) where TemplateClass : TransactionMessage<T>
        {
            messageModel = default;
            instance = default;
            bool error = false;
            var header = new Header();
        RESULT:
            if (error) return (new TransactionMessage<T>
            {
                Body = default,
                Header = header,
                Message = new StateMessage
                {
                    Code = ServiceResultCode.StateCode_400,
                    Message = "参数格式错误"
                }
            }.ObjectToBytesForJson(), false);
            try
            {
                messageModel = buff.BytesToObjectForJson<TemplateClass>();
                if (messageModel == null || messageModel.Header == null)
                {
                    error = true;
                    goto RESULT;
                }
                header.MessageType = messageModel.Header.MessageType == RRPC.Request ? RRPC.Response : RRPC.Push;
                instance = messageModel.Body;
                if (instance == null)
                {
                    error = true;
                    goto RESULT;
                }
                return (null, true);
            }
            catch (JsonException jsonEx)
            {
                Log.Error(Log.CommRepository, $"JSON解析错误: {jsonEx.Message}");
                error = true;
                goto RESULT;
            }
            catch (Exception ex)
            {
                Log.Error(Log.CommRepository, $"未知错误: {ex.Message}");
                error = true;
                goto RESULT;
            }
        }
    }
}
