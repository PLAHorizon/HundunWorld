using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace Horizon.Share.Utils
{
    public class SerializerUtilitys
    {
        static JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,

        };
        public static byte[] Serialize<T>(T serializeObj)
        {
            try
            {
                return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(serializeObj, _serializerSettings));
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public static T DeSerialize<T>(byte[] bytes)
        {
            try
            {

                return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes), _serializerSettings);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return default(T);
            }
        }

    }
}
