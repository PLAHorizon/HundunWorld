

using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Core.Abstract;

namespace Horizon.Core
{
    public static class HorizonIO
    {
        private static IHorizonIO _horizonIO = null;

        public static IHorizonIO Current
        {
            get { return _horizonIO; }
            set { _horizonIO = value; }
        }
        static HorizonIO()
        {
            //Load();
        }

        /// <summary>
        /// 指定的文件下追加内容（如果文件不存在，则创建可追加文件）
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="stream"></param>
        public static void AppendFile(string fileName, Stream stream)
        {
            _horizonIO.AppendFile(fileName, stream);
        }

        /// <summary>
        /// 指定的文件下追加内容（如果文件不存在，则创建可追加文件）
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="content"></param>
        public static void AppendFile(string fileName, string content)
        {
            _horizonIO.AppendFile(fileName, content);
        }

        /// <summary>
        /// 复制文件到新目录
        /// </summary>
        /// <param name="sourceFileName">原路径</param>
        /// <param name="destFileName">目标路径</param>
        /// <param name="overwrite">是否覆盖</param>
        public static void CopyFile(string sourceFileName, string destFileName, bool overwrite = false)
        {
            _horizonIO.CopyFile(sourceFileName, destFileName, overwrite);
        }

        /// <summary>
        /// 创建一个目录
        /// </summary>
        /// <param name="dirName"></param>
        public static void CreateDir(string dirName)
        {
            _horizonIO.CreateDir(dirName);
        }

        /// <summary>
        /// 创建普通文件
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="stream">文件流</param>
        /// <param name="fileCreateType"></param>
        public static void CreateFile(string fileName, Stream stream, FileCreateType fileCreateType = FileCreateType.CreateNew)
        {
            _horizonIO.CreateFile(fileName, stream, fileCreateType);
        }

        /// <summary>
        /// 创建普通文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="content">文件内容</param>
        /// <param name="fileCreateType"></param>
        public static void CreateFile(string fileName, string content, FileCreateType fileCreateType = FileCreateType.CreateNew)
        {
            _horizonIO.CreateFile(fileName, content, fileCreateType);
        }

        /// <summary>
        /// 创建缩略图
        /// </summary>
        /// <param name="sourceFilename"></param>
        /// <param name="destFilename"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public static void CreateThumbnail(string sourceFilename, string destFilename, int width, int height)
        {
            _horizonIO.CreateThumbnail(sourceFilename, destFilename, width, height);
        }

        /// <summary>
        /// 删除目录
        /// </summary>
        /// <param name="dirName"></param>
        /// <param name="recursive">要移除 路径中的目录、子目录和文件，则为 true；否则为 false</param>
        public static void DeleteDir(string dirName, bool recursive = false)
        {
            _horizonIO.DeleteDir(dirName, recursive);
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="fileName"></param>
        public static void DeleteFile(string fileName)
        {
            _horizonIO.DeleteFile(fileName);
        }

        /// <summary>
        /// 批量删除文件
        /// </summary>
        /// <param name="fileNames"></param>
        public static void DeleteFiles(List<string> fileNames)
        {
            _horizonIO.DeleteFiles(fileNames);
        }

        /// <summary>
        /// 是否存在该目录
        /// </summary>
        /// <param name="dirName"></param>
        /// <returns></returns>
        public static bool ExistDir(string dirName)
        {
            return _horizonIO.ExistDir(dirName);
        }

        /// <summary>
        /// 是否存在该文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static bool ExistFile(string fileName)
        {
            if (fileName.Equals(""))
            {
                return false;
            }
            return _horizonIO.ExistFile(fileName);
        }

        /// <summary>
        /// 列出目录下的文件和子目录
        /// </summary>
        /// <param name="dirName"></param>
        /// <param name="self">是否包含本身 默认为false</param>
        /// <returns></returns>
        public static List<string> GetDirAndFiles(string dirName, bool self = false)
        {
            return _horizonIO.GetDirAndFiles(dirName, self);
        }

        /// <summary>
        ///  获取目录基本信息
        /// </summary>
        /// <param name="dirName"></param>
        /// <returns></returns>
        public static MetaInfo GetDirMetaInfo(string dirName)
        {
            return _horizonIO.GetDirMetaInfo(dirName);
        }

        /// <summary>
        /// 获取文件内容
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <returns></returns>
        public static byte[] GetFileContent(string fileName)
        {
            return _horizonIO.GetFileContent(fileName);
        }

        /// <summary>
        /// 获取文件基本信息
        /// </summary>
        /// <param name="fileName">文件名称</param>
        /// <returns></returns>
        public static MetaInfo GetFileMetaInfo(string fileName)
        {
            return _horizonIO.GetFileMetaInfo(fileName);
        }

        /// <summary>
        /// 获取文件的绝对路径
        /// </summary>
        /// <param name="fileName">文件名称</param>
        /// <returns></returns>
        public static string GetFilePath(string fileName)
        {
            return _horizonIO.GetFilePath(fileName);
        }

        /// <summary>
        /// 列出目录下所有文件
        /// </summary>
        /// <param name="dirName"></param>
        /// <param name="self">是否包含自身</param>
        /// <returns></returns>
        public static List<string> GetFiles(string dirName, bool self = false)
        {
            return _horizonIO.GetFiles(dirName, self);
        }



        /// <summary>
        /// 获取图片的路径
        /// </summary>
        /// <param name="imageName">图片名称</param>
        /// <param name="styleName">样式名称</param>
        /// <returns></returns>
        public static string GetImagePath(string imageName, string styleName = null)
        {
            return _horizonIO.GetImagePath(imageName, styleName);
        }

        /// <summary>
        /// 获取不同尺码的商品图片
        /// </summary>
        /// <param name="productPath"></param>
        /// <param name="index"></param>
        /// <param name="width"></param>
        /// <returns></returns>
        public static string GetProductSizeImage(string productPath, int index, int width = 0)
        {
            return _horizonIO.GetProductSizeImage(productPath, index, width);
        }
        public static string GetSizeImage(string productPath, string name, int width = 0)
        {
            return _horizonIO.GetSizeImage(productPath, name, width);
        }




        /// <summary>
        /// 移动文件到新目录
        /// </summary>
        /// <param name="sourceFileName">原路径</param>
        /// <param name="destFileName">目标路径</param>
        /// <param name="overwrite">是否覆盖</param>
        public static void MoveFile(string sourceFileName, string destFileName, bool overwrite = false)
        {
            _horizonIO.MoveFile(sourceFileName, destFileName, overwrite);
        }
    }
}