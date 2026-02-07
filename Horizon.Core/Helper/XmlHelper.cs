using System;
using System.IO;
using System.Xml.Serialization;

namespace Horizon.Core.Helper
{
    public class XmlHelper
    {
        /// <summary>
        /// XML反序列化
        /// </summary>
        /// <param name="type">目标类型(Type类型)</param>
        /// <param name="filePath">XML文件路径</param>
        /// <returns>序列对象</returns>
        public static object DeserializeFromXML(Type type, string filePath)
        {
            object obj;
            FileStream fileStream = null;
            try
            {
                try
                {
                    fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    obj = (new XmlSerializer(type)).Deserialize(fileStream);
                }
                catch (Exception exception)
                {
                    throw;
                }
            }
            finally
            {
                if (fileStream != null)
                {
                    fileStream.Close();
                }
            }
            return obj;
        }

        /// <summary>
        /// XML序列化
        /// </summary>
        /// <param name="obj">序列对象</param>
        /// <param name="filePath">XML文件路径</param>
        /// <returns>是否成功</returns>
        public static bool SerializeToXml(object obj, string filePath)
        {
            bool flag = false;
            FileStream fileStream = null;
            try
            {
                try
                {
                    fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    (new XmlSerializer(obj.GetType())).Serialize(fileStream, obj);
                    flag = true;
                }
                catch (Exception exception)
                {
                    throw;
                }
            }
            finally
            {
                if (fileStream != null)
                {
                    fileStream.Close();
                }
            }
            return flag;
        }
    }
}