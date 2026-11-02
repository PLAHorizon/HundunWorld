using FlaxEngine;
using MetaHuman.Runtime;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 把模板参数 hair.style 落到发卡模型上（换 StaticModel.Model）。
    ///
    /// 为什么不是写回端：发型既不是材质参数也不是骨骼/morph，MetaHumanAppearanceApplier 管不到模型引用。
    /// 这里挂在发卡 StaticModel 上，向上找 MetaHumanCharacter，值变了才换模型；
    /// 换模型会重置材质槽，所以换之前先记下当前槽材质（宿主设过的发色虚拟实例），换完再指回去。
    /// </summary>
    public class MetaHumanHairBinder : Script
    {
        [Tooltip("宿主角色；留空则沿父级链自动找 MetaHumanCharacter")]
        [Serialize]
        public MetaHumanCharacter Owner;

        private StaticModel _hair;
        private int _appliedStyle = -1;
        private bool _resolved;
        private bool _warnedMask;

        public override void OnEnable()
        {
            _hair = Actor as StaticModel;
            if (_hair == null)
            {
                Debug.LogWarning("[MetaHumanHairBinder] 只能挂在 StaticModel 上：" + (Actor != null ? Actor.Name : "<null>"));
            }
        }

        public override void OnUpdate()
        {
            if (_hair == null)
            {
                return;
            }

            if (!_resolved || Owner == null)
            {
                ResolveOwner();
            }

            if (Owner == null)
            {
                return;
            }

            int style = HairStyleCatalog.Clamp(Owner.HairStyle);
            if (style == _appliedStyle)
            {
                return;
            }

            Apply(style);
        }

        private void Apply(int style)
        {
            string path = HairStyleCatalog.PathFor(style);
            Model asset = Content.Load<Model>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(path), 10000.0);
            if (asset == null || !asset.IsLoaded)
            {
                if (_appliedStyle != style)
                {
                    Debug.LogWarning("[MetaHumanHairBinder] model-not-ready style=" + style + " path=" + path);
                }

                return;
            }

            MaterialBase colorInstance = _hair.GetMaterial(0);
            _hair.Model = asset;
            if (colorInstance != null)
            {
                _hair.SetMaterial(0, colorInstance);
            }

            // 换发型同时换剪切掩码：卡片是 Opaque+Mask（MaskThreshold）实现的切边，
            // 掩码留在上一套就会用错的卡片形状。只改实例，不改基础材质（那会污染共享资产）。
            if (colorInstance is MaterialInstance instance)
            {
                Texture mask = Content.Load<Texture>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(HairStyleCatalog.MaskPathFor(style)), 10000.0);
                if (mask != null && mask.IsLoaded)
                {
                    instance.SetParameterValue("MaskTex", mask);
                }
                else if (!_warnedMask)
                {
                    _warnedMask = true;
                    Debug.LogWarning("[MetaHumanHairBinder] mask-not-ready style=" + style
                        + " path=" + HairStyleCatalog.MaskPathFor(style));
                }
            }
            else if (colorInstance != null && !_warnedMask)
            {
                _warnedMask = true;
                Debug.LogWarning("[MetaHumanHairBinder] hair material 不是实例，跳过换掩码：" + colorInstance.GetType().Name);
            }

            _appliedStyle = style;
            // ASCII 键 + 路径：磁盘日志里的中文会被 NUL 打断，只有这两样能可靠 grep。
            Debug.Log("[MetaHumanHairBinder] style=" + style + " model=" + path);
        }

        private void ResolveOwner()
        {
            _resolved = true;
            if (Owner != null)
            {
                return;
            }

            Actor current = Actor != null ? Actor.Parent : null;
            while (current != null)
            {
                MetaHumanCharacter character = current.GetScript<MetaHumanCharacter>();
                if (character != null)
                {
                    Owner = character;
                    return;
                }

                current = current.Parent;
            }
        }
    }
}
