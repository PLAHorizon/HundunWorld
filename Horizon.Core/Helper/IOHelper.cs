using System;
using System.Configuration;
using System.IO;
using System.Web;

namespace Horizon.Core.Helper
{
    public class IOHelper
    {
        /// <summary>
        /// 复制指定的文件到指定目录
        /// </summary>
        /// <param name="fileFullPath">源文件的全路径</param>
        /// <param name="destination">目标目录</param>
        /// <param name="isDeleteSourceFile">是否删除源文件</param>
        /// <param name="fileName">目标文件名称,默认是原名称</param>
        /// <exception cref="T:System.ArgumentNullException">源文件全路径为空</exception>
        /// <exception cref="T:System.IO.FileNotFoundException">找不到源文件</exception>
        /// <exception cref="T:System.IO.DirectoryNotFoundException">找不到目标目录</exception>
        /// <exception cref="T:System.Exception">复制文件异常</exception>
        public static void CopyFile(string fileFullPath, string destination, bool isDeleteSourceFile = false, string fileName = "")
        {
            if (string.IsNullOrWhiteSpace(fileFullPath))
            {
                throw new ArgumentNullException("fileFullPath", "源文件全路径不能为空");
            }
            if (!File.Exists(fileFullPath))
            {
                throw new FileNotFoundException("找不到源文件", fileFullPath);
            }
            if (!Directory.Exists(destination))
            {
                throw new DirectoryNotFoundException(string.Concat("找不到目标目录 ", destination));
            }
            try
            {
                fileName = string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(fileFullPath) : fileName;
                File.Copy(fileFullPath, Path.Combine(destination, fileName), true);
                if (isDeleteSourceFile)
                {
                    File.Delete(fileFullPath);
                }
            }
            catch (Exception exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 获取文件夹大小单位KB
        /// </summary>
        /// <param name="dirPath">文件夹路径</param>
        /// <returns></returns>
        public static long GetDirectoryLength(string dirPath)
        {
            if (Directory.Exists(dirPath))
            {
                long length = 0;
                DirectoryInfo directoryInfo = new DirectoryInfo(dirPath);
                FileInfo[] files = directoryInfo.GetFiles();
                for (int i = 0; i < files.Length; i++)
                {
                    length = length + files[i].Length;
                }
                DirectoryInfo[] directories = directoryInfo.GetDirectories();
                if (directories.Length > 0)
                {
                    for (int j = 0; j < directories.Length; j++)
                    {
                        length = length + GetDirectoryLength(directories[j].FullName);
                    }
                }
                return length;
            }
            return 0;
        }

        public static string GetMapPath(string filePath)
        {
            string path = Directory.GetCurrentDirectory();
            return $@"{path}\{filePath}";
        }
    }
}