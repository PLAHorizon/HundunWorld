#if FLAX_EDITOR
using System;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Gasp
{
    /// <summary>
    /// 编辑器内一次性烘焙：把运行时二进制库包成 RawDataAsset（Content 资产，随包分发）。
    /// 仅编辑器构建可用；由夹具标记触发。
    /// </summary>
    public static class GaspDataAssetBaker
    {
        public static bool Bake(string binProjectPath, string assetProjectPath)
        {
            try
            {
                string project = Globals.ProjectFolder;
                string binFull = Path.IsPathRooted(binProjectPath)
                    ? binProjectPath
                    : Path.Combine(project, binProjectPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(binFull))
                {
                    Debug.LogError("[GaspAssetBake] 缺少 bin: " + binFull);
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(binFull);
                var asset = Content.CreateVirtualAsset<RawDataAsset>();
                if (asset == null)
                {
                    Debug.LogError("[GaspAssetBake] 创建虚拟 RawDataAsset 失败");
                    return false;
                }
                asset.Data = bytes;
                bool failed = asset.Save(assetProjectPath);
                Debug.Log($"[GaspAssetBake] asset={assetProjectPath} bytes={bytes.Length} failed={failed}");
                return !failed;
            }
            catch (Exception ex)
            {
                Debug.LogError("[GaspAssetBake] 异常: " + ex);
                return false;
            }
        }
    }
}
#endif
