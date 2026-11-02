# 头部词汇覆盖率（真 MetaHuman 头的名字对得上多少）

数据来源：`docs/metahuman/dna/SKM_Face_names.json`（.dna 里 9 张名字表，1460 个唯一名字）与 `docs/metahuman/gui_to_raw_map.json`（UE5.8 权威表：174 GUI / 251 raw / 259 段）。
重跑：`python Tools/MetaHumanHeadVocabularyReport.py`

| 层 | 问题 | 命中 |
| --- | --- | --- |
| L1 | 174 个 GUI 控制名（带 `.ty` 通道后缀）是否原样出现在资产名字表里 | 174 / 174 |
| L2 | 资产 morph 名（剥宿主网格前缀后 37 个唯一名）是否与 251 个 raw 控制名同名 | 1 / 37 |
| L3 | 再把左右侧标记与下划线归一后能对上多少 | 3 / 37 |

读法：**L1 满命中**说明外部追踪入口那套 GUI 词汇在真资产里是原样存在的，导入后不需要为它做名字表。
**L2 很低不是数据坏了**：UE 的 morph 命名与控制 rig 命名本来就不同族（`head_wm1_blink_L` ↔ `CTRL_expressions_eyeBlinkL`），所以 blendshape 那一侧要靠一张人工名字表把 morph 绑到 raw 控制上，规模就是 L3-L2 与 `len(stems)-L3` 这两块。

L3 命中的 morph 名（可直接归到某个 raw 控制）：

    jawOpen, noseWrinkler_L, noseWrinkler_R

归一后仍找不到 raw 对应的 morph 名（要单独建表或按区域批量绑）：

    blink_L, blink_R, browsDown_L, browsDown_R, browsLateral_L, browsLateral_R, browsRaiseInner_L, browsRaiseInner_R, browsRaiseOuter_L, browsRaiseOuter_R, cheekRaiseInner_L, cheekRaiseInner_R, cheekRaiseOuter_L, cheekRaiseOuter_R, cheekRaiseUpper_L, cheekRaiseUpper_R, chinRaise_L, chinRaise_R, lips_DL, lips_DR, lips_UL, lips_UR, mouthStretch_L, mouthStretch_R, neckStretch_L, neckStretch_R, purse_DL, purse_DR, purse_UL, purse_UR, smile_L, smile_R, squintInner_L, squintInner_R

## morph → GUI 候选清单（**候选，未人工核对**）

共有词数前 2 名。左右侧与前缀对不齐属正常，落表时要逐条判；零候选的那些得靠区域批量绑或补一张对照，不能假装同名。

| morph 名 | 候选 GUI 控制名（共有词数） |
| --- | --- |
| blink_L | CTRL_L_eye_blink.ty(1); CTRL_R_eye_blink.ty(1) |
| blink_R | CTRL_L_eye_blink.ty(1); CTRL_R_eye_blink.ty(1) |
| browsDown_L | CTRL_L_brow_down.ty(1); CTRL_R_brow_down.ty(1) |
| browsDown_R | CTRL_L_brow_down.ty(1); CTRL_R_brow_down.ty(1) |
| browsLateral_L | CTRL_L_brow_lateral.ty(1); CTRL_R_brow_lateral.ty(1) |
| browsLateral_R | CTRL_L_brow_lateral.ty(1); CTRL_R_brow_lateral.ty(1) |
| browsRaiseInner_L | CTRL_L_brow_raiseIn.ty(1); CTRL_L_brow_raiseOut.ty(1) |
| browsRaiseInner_R | CTRL_L_brow_raiseIn.ty(1); CTRL_L_brow_raiseOut.ty(1) |
| browsRaiseOuter_L | CTRL_L_brow_raiseIn.ty(1); CTRL_L_brow_raiseOut.ty(1) |
| browsRaiseOuter_R | CTRL_L_brow_raiseIn.ty(1); CTRL_L_brow_raiseOut.ty(1) |
| cheekRaiseInner_L | CTRL_L_eye_cheekRaise.ty(2); CTRL_R_eye_cheekRaise.ty(2) |
| cheekRaiseInner_R | CTRL_L_eye_cheekRaise.ty(2); CTRL_R_eye_cheekRaise.ty(2) |
| cheekRaiseOuter_L | CTRL_L_eye_cheekRaise.ty(2); CTRL_R_eye_cheekRaise.ty(2) |
| cheekRaiseOuter_R | CTRL_L_eye_cheekRaise.ty(2); CTRL_R_eye_cheekRaise.ty(2) |
| cheekRaiseUpper_L | CTRL_L_eye_cheekRaise.ty(2); CTRL_L_mouth_upperLipRaise.ty(2) |
| cheekRaiseUpper_R | CTRL_L_eye_cheekRaise.ty(2); CTRL_L_mouth_upperLipRaise.ty(2) |
| chinRaise_L | CTRL_L_jaw_ChinRaiseD.ty(2); CTRL_L_jaw_ChinRaiseU.ty(2) |
| chinRaise_R | CTRL_L_jaw_ChinRaiseD.ty(2); CTRL_L_jaw_ChinRaiseU.ty(2) |
| jawOpen | CTRL_C_jaw_openExtreme.ty(2); CTRL_C_jaw.tx(1) |
| lips_DL | CTRL_L_mouth_lipsBlow.ty(1); CTRL_L_mouth_lipsPressU.ty(1) |
| lips_DR | CTRL_L_mouth_lipsBlow.ty(1); CTRL_L_mouth_lipsPressU.ty(1) |
| lips_UL | CTRL_L_mouth_lipsBlow.ty(1); CTRL_L_mouth_lipsPressU.ty(1) |
| lips_UR | CTRL_L_mouth_lipsBlow.ty(1); CTRL_L_mouth_lipsPressU.ty(1) |
| mouthStretch_L | CTRL_L_mouth_stretch.ty(2); CTRL_L_mouth_stretchLipsClose.ty(2) |
| mouthStretch_R | CTRL_L_mouth_stretch.ty(2); CTRL_L_mouth_stretchLipsClose.ty(2) |
| neckStretch_L | CTRL_L_neck_stretch.ty(2); CTRL_R_neck_stretch.ty(2) |
| neckStretch_R | CTRL_L_neck_stretch.ty(2); CTRL_R_neck_stretch.ty(2) |
| noseWrinkler_L | CTRL_L_nose.tx(1); CTRL_L_nose.ty(1) |
| noseWrinkler_R | CTRL_L_nose.tx(1); CTRL_L_nose.ty(1) |
| purse_DL | CTRL_L_mouth_purseD.ty(1); CTRL_L_mouth_purseU.ty(1) |
| purse_DR | CTRL_L_mouth_purseD.ty(1); CTRL_L_mouth_purseU.ty(1) |
| purse_UL | CTRL_L_mouth_purseD.ty(1); CTRL_L_mouth_purseU.ty(1) |
| purse_UR | CTRL_L_mouth_purseD.ty(1); CTRL_L_mouth_purseU.ty(1) |
| smile_L | （零候选） |
| smile_R | （零候选） |
| squintInner_L | CTRL_L_eye_squintInner.ty(2); CTRL_R_eye_squintInner.ty(2) |
| squintInner_R | CTRL_L_eye_squintInner.ty(2); CTRL_R_eye_squintInner.ty(2) |

零候选的 morph：smile_L, smile_R
