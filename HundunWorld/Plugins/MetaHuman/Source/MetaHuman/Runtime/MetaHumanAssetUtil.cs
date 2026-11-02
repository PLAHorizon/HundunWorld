using System;
using FlaxEngine;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// Flax 的托管 Asset 只暴露 Path，资产的 Guid 与展示名要从 AssetInfo 取；
    /// 这两个访问集中在这里，免得每处各自猜 API 名字。
    /// </summary>
    public static class MetaHumanAssetUtil
    {
        /// <summary>
        /// 把项目内相对路径补成**内容库登记时的那一种绝对拼写**再交给 Content API。
        ///
        /// 引擎源码事实（`AssetsCache::FindAsset`）：注册表按**路径字符串精确比对**；
        /// 查不到时 `Content::GetAssetInfo` / `ContentStorageManager::GetStorage` 会打开存储并
        /// `RegisterAssets(storage)`，于是同一文件以第二种拼写再登记一次 → 重复 ID 仲裁 →
        /// 改名 → 重打包被占用（0x20）→ 改名不落盘 → 下次启动再换一个 ID。
        ///
        /// 内容库的拼写是 `StringUtils.CombinePaths(Globals.ProjectFolder, 相对路径)`，本机
        /// ProjectFolder 本身是混用的（`C:\Works/GitHubProjects/...`），因此**不能**过
        /// `Path.GetFullPath`（会变成全反斜杠，查不到就触发上面的循环）。
        /// 2026-10-25 实测：改成全反斜杠后，每次运行期加载头资产都会新增一条登记。
        /// </summary>
        public static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            try
            {
                // 调用方给的就是绝对路径时原样返回：不要再规范化，否则会改掉拼写。
                if (System.IO.Path.IsPathRooted(path))
                {
                    return path;
                }

                return StringUtils.CombinePaths(Globals.ProjectFolder, path.Replace('\\', '/'));
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }

        public static Guid IdOf(Asset asset)
        {
            if (asset == null)
            {
                return Guid.Empty;
            }

            // 直接用资产自带的 ID。不要走 Content.GetAssetInfo(asset.Path)：
            // Asset.Path 是**绝对路径**，按绝对路径查库会在内容库里再登记一条与
            // 扫描得到的相对路径并存的重复条目，编辑器随后会给同一个文件换 ID
            // （日志：Founded duplicated asset … 'Content/…' and 'C:\…'），
            // 引用它的动画图/预制体因此悬空（2026-10-25 实测）。
            return asset.ID;
        }

        public static string DisplayName(Asset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.Path))
            {
                return "<空资产>";
            }

            return System.IO.Path.GetFileNameWithoutExtension(asset.Path);
        }

        /// <summary>
        /// 按路径装载蒙皮模型：带超时、要求真加载完、并覆盖两种路径写法。
        ///
        /// 为什么要这么厚一层：模板里存的资产路径不带扩展名（`Content/Character/Models/Ada_FaceMesh`），
        /// 而引擎侧按路径查资产还有第二把尺子（同一个路径按名字查不到、按 Guid 查得到），
        /// 刚导入的资产更是还没进内容库。一句 `Content.Load&lt;T&gt;(path)` 读空时不报错，
        /// 于是在导入流程里会同时出现「导入成功」与「模板 0 个 morph」这两句真话。
        /// </summary>
        public static SkinnedModel LoadSkinnedModel(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string normalized = ResolveProjectPath(assetPath);
            SkinnedModel model = TryLoadOne(normalized);
            if (model != null || normalized.EndsWith(".flax", StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }

            return TryLoadOne(normalized + ".flax");
        }

        private static SkinnedModel TryLoadOne(string path)
        {
            SkinnedModel model = Content.Load<SkinnedModel>(path, 10000.0f);
            if (model != null && model.IsLoaded)
            {
                return model;
            }

            AssetInfo info;
            if (Content.GetAssetInfo(path, out info) && info.ID != Guid.Empty)
            {
                model = Content.Load<SkinnedModel>(info.ID, 10000.0f);
                if (model != null && model.IsLoaded)
                {
                    return model;
                }
            }

            return null;
        }
    }
}
