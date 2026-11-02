using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MetaHuman.Core
{
    /// <summary>
    /// 表情骨骼表：一个控制值 → 一组骨绕某个支点转多少度。
    ///
    /// 为什么表情不能只靠 morph：Ada 头里 blink 这类形状目标的位移只有 0.1mm 量级
    /// （它们是 rig logic 的修正项），真正让眼皮闭合的是眼睑骨的旋转；
    /// 而带蒙皮权重的是带序号的骨（FACIAL_L_EyelidUpperA1/A2/A3…），
    /// 支点是眼球骨而不是每根骨自己的头——这些量都由 Tools/MetaHumanEyelidClosureProbe.py 实测得出。
    /// </summary>
    public class MetaHumanExpressionRig
    {
        public string Source { get; set; }

        /// <summary>尚未有同口径实测、因此刻意不写的通道（报告里要给读者看见，而不是当成"支持了"）。</summary>
        public List<string> Pending { get; set; }

        public List<MetaHumanExpressionChannel> Channels { get; set; }

        private Dictionary<string, MetaHumanExpressionChannel> _byControl;

        public static MetaHumanExpressionRig FromJson(string json)
        {
            MetaHumanExpressionRig rig = JsonSerializer.Deserialize<MetaHumanExpressionRig>(json, Opts);
            if (rig == null)
            {
                throw new InvalidDataException("表情骨骼表反序列化得到 null");
            }

            rig.BuildIndex();
            return rig;
        }

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private void BuildIndex()
        {
            _byControl = new Dictionary<string, MetaHumanExpressionChannel>(StringComparer.OrdinalIgnoreCase);
            if (Channels == null)
            {
                Channels = new List<MetaHumanExpressionChannel>();
                return;
            }

            for (int i = 0; i < Channels.Count; i++)
            {
                MetaHumanExpressionChannel channel = Channels[i];
                if (channel == null || string.IsNullOrEmpty(channel.Control))
                {
                    continue;
                }

                if (channel.Groups == null)
                {
                    channel.Groups = new List<MetaHumanExpressionGroup>();
                }

                if (channel.Translates == null)
                {
                    channel.Translates = new List<MetaHumanExpressionTranslate>();
                }

                _byControl[channel.Control] = channel;
            }
        }

        public int ChannelCount
        {
            get { return _byControl == null ? 0 : _byControl.Count; }
        }

        public MetaHumanExpressionChannel Find(string controlName)
        {
            if (_byControl == null || string.IsNullOrEmpty(controlName))
            {
                return null;
            }

            MetaHumanExpressionChannel channel;
            return _byControl.TryGetValue(controlName, out channel) ? channel : null;
        }
    }

    public class MetaHumanExpressionChannel
    {
        /// <summary>控制名（Face_Control_Table 里的 ControlName，如 CTRL_L_eye_blink.ty）。</summary>
        public string Control { get; set; }

        /// <summary>旋转支点所在的骨名（眼睑用 FACIAL_L/R_Eye，运行时取它的当前模型空间位置）。</summary>
        public string PivotBone { get; set; }

        /// <summary>模型空间旋转轴（单位向量）。</summary>
        public List<float> Axis { get; set; }

        public List<MetaHumanExpressionGroup> Groups { get; set; }

        /// <summary>
        /// 平移通道（张口这类没有可用旋转支点的动作）：可有多组，各自带骨清单与位移。
        /// 例如下唇组下移 3cm、下齿组下移 1cm —— 同一控制下不同组织走不同幅度。
        /// 与 <see cref="Groups"/> 二选一或并存，最终都按控制值线性缩放。
        /// </summary>
        public List<MetaHumanExpressionTranslate> Translates { get; set; }
    }

    public class MetaHumanExpressionTranslate
    {
        public List<string> Bones { get; set; }

        /// <summary>控制值为 1 时的模型空间位移（厘米，最终会乘控制值）。</summary>
        public List<float> DeltaAtOneCm { get; set; }
    }

    public class MetaHumanExpressionGroup
    {
        public List<string> Bones { get; set; }

        /// <summary>控制值为 1 时的旋转角（度）；控制值为负时按同一系数反向转。</summary>
        public float AngleAtOne { get; set; }
    }
}
