using Horizon.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Horizon.Core.Abstract;

namespace Horizon.Core.Helper
{
    public static class HtmlContentHelper
    {
        /// <summary>   
        /// 取得HTML中所有图片的 URL。   
        /// </summary>   
        /// <param name="htmlText">HTML代码</param>   
        /// <returns>图片的URL列表</returns>   
        private static IEnumerable<string> GetHtmlImageUrlList(string htmlText)
        {
            MatchCollection matchs = new Regex("<img\\b[^<>]*?\\bsrc[\\s\\t\\r\\n]*=[\\s\\t\\r\\n]*[\"']?[\\s\\t\\r\\n]*(?<imgUrl>[^\\s\\t\\r\\n\"'<>]*)[^<>]*?/?[\\s\\t\\r\\n]*>", RegexOptions.IgnoreCase).Matches(htmlText);
            int num = 0;
            string[] strArray = new string[matchs.Count];
            foreach (Match match in matchs)
            {
                strArray[num++] = match.Groups["imgUrl"].Value;
            }
            return strArray;
        }

        /// <summary>
        /// 清除HTML中的JS脚本和style脚本
        /// </summary>
        /// <param name="htmlText"></param>
        /// <returns></returns>
        public static string RemoveScriptsAndStyles(string htmlText)
        {
            htmlText = Regex.Replace(htmlText, @"<\s*script[^>]*?>.*?<\s*/\s*script\s*>", "", RegexOptions.IgnoreCase);
            htmlText = Regex.Replace(htmlText, @"<\s*style[^>]*?>.*?<\s*/\s*style\s*>", "", RegexOptions.IgnoreCase);
            return htmlText;
        }

        /// <summary>
        /// 将HTML中的图片转为本地图片
        /// </summary>
        /// <param name="htmlText">待转换的HTML文本</param>
        /// <param name="desDir">图片要存储到的目录</param>
        /// <param name="relativeRootPath">图片使用相对地址时的根目录</param>
        /// <param name="imgSrcPreText">修改后的图片SRC的前缀</param>
        /// <returns></returns>
        public static string TransferToLocalImage(string htmlText, string relativeRootPath, string desDir, string imgSrcPreText = "")
        {
            if (!relativeRootPath.EndsWith("/"))
            {
                relativeRootPath = string.Concat(relativeRootPath, "/");
            }
            int num = 0;
            List<string> strs = GetHtmlImageUrlList(htmlText).ToList().FindAll(imgurl => !imgurl.StartsWith("data:"));
            WebClient webClient = new WebClient();
            foreach (string str in strs)
            {
                string[] strArrays = str.Split('.');

                string name = string.Concat(Guid.NewGuid().ToString("N"), ".", strArrays[strArrays.Length - 1]);
                string fileName = string.Concat(desDir, "/", name);
                try
                {
                    if (!(!str.StartsWith("http://") && !str.StartsWith("https://") || str.IndexOf("/Storage") >= 0))
                    {
                        byte[] numArray = webClient.DownloadData(str);
                        HorizonIO.CreateFile(fileName, new MemoryStream(numArray), FileCreateType.Create);
                        htmlText = htmlText.Replace(str, string.Concat(imgSrcPreText, name));
                    }
                    else if (str.IndexOf("temp/") > 0)
                    {
                        HorizonIO.CopyFile(string.Concat(relativeRootPath, str), fileName, true);
                        htmlText = htmlText.Replace(str, string.Concat(imgSrcPreText, name));
                    }
                }
                catch
                {
                }
                num++;
            }
            htmlText = htmlText.Replace("<IMG", "<img").Replace("</IMG", "</img");
            return htmlText;
        }


    }
}