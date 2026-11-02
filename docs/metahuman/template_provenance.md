# 模板生成来源

生成器：`Tools/MetaHumanBuildTemplates.py`

## 脸部 `Template_Face_Archetype.json`

- 关节分群数: 75
- 控制表条目: 174
- 被排除的关节名: 5
- 排除样例: ["AdamsApple", "FacialRoot", "Skull", "Pupil", "Masseter"]
- 形状参数: 441
- morph形状参数: 37
- 表情参数: 0
- 表情命中 morph: "43/174"
- 表情命中关节: "47/174"
- 表情未命中: 84
- 表情未命中(不进模板): 84
- 未命中样例: ["CTRL_L_brow_down.ty", "CTRL_R_brow_down.ty", "CTRL_L_brow_lateral.ty", "CTRL_R_brow_lateral.ty", "CTRL_L_brow_raiseIn.ty", "CTRL_R_brow_raiseIn.ty", "CTRL_L_brow_raiseOut.ty", "CTRL_R_brow_raiseOut.ty", "CTRL_L_eyelashes_tweakerIn.ty", "CTRL_R_eyelashes_tweakerIn.ty", "CTRL_L_eyelashes_tweakerOut.ty", "CTRL_R_eyelashes_tweakerOut.ty", "CTRL_L_mouth_suckBlow.ty", "CTRL_R_mouth_suckBlow.ty", "CTRL_C_mouth.ty"]
- 材质参数: 9

来源：`SKM_Face.dna` 的 `defn` 段（GUI 控制 174 / raw 控制 251 / FACIAL 关节 870，其中 843 个 FACIAL）。

## 身体 `Template_Body_Base.json`


来源：`body_model.dna` desc 段的 30 项围度与量程、`body_joint_mapping.json` 的关节名。
