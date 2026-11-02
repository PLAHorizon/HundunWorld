# MetaHuman `.dna` container — decoded layout and section inventory

Produced by `Tools/MetaHumanDnaExtract.py` against the read-only UE 5.8 install.
Every number below was printed by that tool; every name carries the absolute byte
offset of its first character in the file. Command used:

```
python Tools/MetaHumanDnaExtract.py "C:/Program Files/Epic Games/UE_5.8/Engine/Plugins/MetaHuman" \
       --quiet --names --dump-json --out-dir docs/metahuman/dna \
       --index-json docs/metahuman/dna/_index.json
```

Full raw console output: `docs/metahuman/dna/_sweep_log.txt` (22 files, all parsed,
0 failures).

## 1. Header (cracked, self-verifying)

| bytes | meaning |
|---|---|
| `0x00`–`0x03` | magic `DNA` `0x00` |
| `0x04`–`0x05` | u16 LE = **2** in all 22 files |
| `0x06` | schema byte: **4** (only `body_model.dna`), **5** (all 21 other files) |
| `0x07`–`0x09` | `00 00 00` in all 22 files |
| `0x0A` | u8 `sectionCount` (8 for `body_model.dna`, 9 for the other 21) |
| `0x0B`– | `sectionCount` records: `<name bytes><0x00><3 flag bytes><u32 BE offset><u32 BE size>` |

* Name strings are **NUL terminated but not length prefixed**; the byte that looks
  like a length in front of `desc` is the section count.
* The 3 flag bytes are `01 00 01` for `desc`/`defn`/`bhvr`/`geom` and `01 00 00` for
  `mlbh`/`rbfb`/`rbfe`/`jbmd`/`twsw` in every one of the 22 files.
* Proof the offset/size fields are big-endian and complete: for all 22 files the
  sections are strictly ascending, every gap between consecutive sections is **0
  bytes**, and the last section ends exactly at EOF (`unexplained tail=0`, true 22/22).
  Header size equals the first section offset (139 for `body_model.dna`, 155 for the
  9-section files).

Inside section payloads the two primitives are:

```
column  : <u32 BE count> <count * u16 BE>
name    : <u32 BE count> <count * ( <u32 BE nameLen> <ascii, no NUL> )>
```

## 2. `body_model.dna` (65,123,071 bytes, 8 sections, header ends at 139)

| section | flags | offset | size | what it decodes to |
|---|---|---|---|---|
| `desc` | `010001` | 139 | 45,316,546 | 45 MB blob: 252-byte index + one JSON object + one base64 skin model |
| `defn` | `010001` | 45,316,685 | 38,842 | 5 verified name tables (1,595 names) + ~598 u16 index columns |
| `bhvr` | `010001` | 45,355,527 | 202,030 | numeric only, **no names** |
| `geom` | `010001` | 45,557,557 | 19,565,462 | big-endian float32 vertex data, **no names** |
| `mlbh` | `010000` | 65,123,019 | 24 | 24 zero bytes (empty in every file seen) |
| `rbfb` | `010000` | 65,123,043 | 16 | 16 bytes |
| `jbmd` | `010000` | 65,123,059 | 4 | 4 bytes (`00000000`) |
| `twsw` | `010000` | 65,123,063 | 8 | 8 bytes (`0000000000000000`) |

### `desc` in detail

| offset | content |
|---|---|
| 139 | `<u32 26>` `d:\modelRel\body_model.dna` — root table name (the authoring path), zero padded |
| 175 | `<u32 9>` `pca_model` — nested table name |
| 196 | JSON object, **5,331,467 bytes**, parses cleanly. Dumped to `dna/body_model_desc_0.json` |
| 5,331,659 | `<u32 16>` `skin_model-0.4.6` |
| 5,331,679 | `<u32 BE 39,984,976>` = length of a **base64** payload (pure `[A-Za-z0-9+/]`), which decodes to **29,988,732 bytes**; its first big-endian integers are `342`, `54412`, `243038`, `1`, `2`. 342 is exactly the joint-name count found in `defn`, so this is the skin/PCA model over those joints and 54,412 verts. |
| 5,331,679 + 39,984,976 … 45,316,685 | 22 leftover bytes (`00…00 01 00…`) |

JSON top-level keys and the counts inside them (all read back with `json.loads`):

`bsft_version` = `0.4.7`, `model_version` = `0.5.0`, `parent_model_version` = `0.4.6`,
`dna_file` = `body_model.dna`, `floor_joint` = 1,
`contours` = **30** measurement definitions — Height, Bust, Bust Span, Underbust,
Waist, Rise, Inseam, Hip, High Hip, Neck Base, Neck, Thigh, Knee, Calf,
Front Interscye Length, Upper Arm Length, Lower Arm Length, Bicep, Elbow, Wrist,
Neck to Waist, Across Shoulder, Shoulder Height, Shoulder to Apex, Neck Length,
Forearm, Hand Circumference, Fat, Masculine/Feminine, Muscularity;
`keypoints` = **102** (head1…); `pose_logic` = **57** rules
(e.g. `root.tx [-99999999996.0, 100000000000.0, 1.0] -> 100.0 * root.tx`);
`solve_hierarchy` = 7 groups; `body_blend` = 5,830 `[vertexIndex, weight]` pairs;
`body_to_combined` = 4 vertex-index arrays; `joint_estimator`, `part_weights` present.

### `defn` name tables (verified: the leading u32 equals the record count)

| count field @ | names @ | count | content (first / last) |
|---|---|---|---|
| 45,318,179 | 45,318,183 | **444** | `local_back_0` … `global_14` (429 `local_*` + 15 `global_*` behaviour names) |
| 45,326,841 | 45,326,845 | **429** | `local_back_0` … `local_joint_upperarm_r_0` (subset of the above) |
| 45,335,318 | 45,335,322 | **342** | `root`, `pelvis`, `spine_01` … `thigh_fwd_lwr_l` — the full body skeleton |
| 45,341,762 | 45,341,766 | **379** | `Shape_0` … `Shape_378` — body blendshape targets, **unnamed** (index-only) |
| 45,346,587 | 45,346,591 | **1** | `body` |

Totals for this file: 5 verified name tables, 1,601 names incl. the 3 `desc` strings,
1 JSON payload. The 342-joint table matches the `342` inside the base64 skin model.

Note `Shape_0..Shape_378`: the body blendshapes carry **no human-readable names** in
`body_model.dna`, so body morph identity cannot be recovered from this file alone.

## 3. `SKM_Face.dna` — the head file (11,200,323 bytes, 9 sections)

| section | offset | size | names found |
|---|---|---|---|
| `desc` | 155 | 70 | strings `Archetype`, `archetype`, class codes `A1`, `MH.6` (the tool's `desc` string scan also reports one 2-byte false positive, `Eu` at 195, from a 2-length record read out of the binary header — only the >=3-byte, count-confirmed strings above should be trusted) |
| `defn` | 225 | 69,207 | **5 tables: 174 + 263 + 870 + 82 + 50** |
| `bhvr` | 69,432 | 4,010,982 | none (pure numeric) |
| `geom` | 4,080,414 | 7,115,154 | none (leading u32 = 50 == mesh table, then u16 42, u16 3868, u32 24,049 then big-endian f32 triples ~-1.77,-1.95,-2.09; per-mesh framing not decoded) |
| `mlbh` | 11,195,568 | 24 | none — all zero bytes |
| `rbfb` | 11,195,592 | 2,947 | `neckCorr_f_med_nrw_UERBFSolver`, pose list `default`,`turnUp_55`,… |
| `rbfe` | 11,198,539 | 1,772 | **18** head-pose names (`headTurnUpU` … `headTiltRightD`) |
| `jbmd` | 11,200,311 | 4 | none |
| `twsw` | 11,200,315 | 8 | none |

`defn` tables, with the cross-check against
`MetaHumanCoreTechLib/Source/MetaHumanCoreTech/Private/GuiToRawControlsUtils.cpp`:

| count field @ | count | meaning | cross-validation |
|---|---|---|---|
| 5,425 | **174** | GUI/rig control channels (`CTRL_L_brow_down.ty` … `CTRL_C_teeth_fwdBackD.ty`) | **identical list and identical order** to `DefaultGuiNames` (cpp line 24, `NumGuiControls = 174`) |
| 10,337 | **263** | 251 `CTRL_expressions.*` raw controls **plus** 12 joint transform channels (`neck_01.qx…head.qw`) | the 251 match `DefaultRawControlNames` (cpp line 201, `NumRawControls = 251`) in exact order, DNA spelling `CTRL_expressions.X` vs cpp `CTRL_expressions_X` |
| 19,574 | **870** | joint names, of which **843** are `FACIAL_*` | — |
| 42,437 | **82** | blendshape/morph targets as `<mesh>.<morph>`, 37 unique morph names over 6 target sets (`head_cm1/2/3_color`, `head_wm1/2/3_normal`) | — |
| 45,740 | **50** | mesh names (`head_lod0_mesh` … `eyeRight_lod7_mesh`) | equals the leading u32 of `geom` |

The extracted lists are in `docs/metahuman/head_params_from_dna.json`.

## 4. The other 20 files

`SKM_Body.dna`, `body_head_combined.dna` and the 17 `Optional/Body/FixedCompatibility/*.dna`
files all share one shape: 9 sections, `defn` tables of **176** joint transform channels
(`calf_l.qx` …), **342** joint names, **4** mesh names (`combined_lod0..3_mesh` or
`body_lod0..3_mesh`), and an `rbfe` pose table of **213–217** names (`default` …
`upperarm_r_fwd_110`). Their `desc` holds only the class codes `A1` / `MHB.1`; the 17
FixedCompatibility files additionally carry their asset root name (`f_srt_nrw`, …) and
the nested table names `height` and `weight`. No expression or morph names — those exist
only in `SKM_Face.dna`. `mlbh` is 24 zero bytes in all 22 files (verified byte-wise).

## 5. Undecoded / honest gaps

1. `bhvr` (behaviour matrices) and `geom` (vertices) are big-endian numeric blobs. The
   `defn` framing between the name tables is counted but its schema is not decoded
   (`body_model.dna`: 598 `<u32 count><u16 values>` columns consuming 32,452 of 38,842
   bytes = 83.5 %; `SKM_Face.dna`: 292 columns = 86.1 %).
2. `desc`'s inner structure is only partly known: strings are `<u32 len><name>` records
   (sometimes NUL terminated), and `A1`/`MH.6`/`MHB.1` class codes and a small integer
   header exist, but the exact field widths of the `desc`/column headers are not proven.
3. `jbmd`/`twsw`/`rbfb` payloads: no schema (some `rbfb`/`rbfe` pose names found).
4. **No head/face identity parameter names exist in any on-disk `.dna`.** The 22 files
   contain rig/expression/joint/mesh/morph names only; MetaHuman *identity* sliders
   (face shape parameters) are not present in `Engine/Plugins/MetaHuman/**` as `.dna`
   — `SKM_Face.dna` is a rig+mesh archetype, and `body_model.dna` is the body PCA/statistical
   model. Body blendshapes are anonymous `Shape_N`. The only non-MetaHuman `.dna` in the
   engine is `Engine/Plugins/Animation/RigLogic/Content/Test/DNA/rl_unit_behavior_test.dna`.
5. The skin model's decoded 29,988,732-byte payload is parsed only as far as its leading
   integers (`342`, `54412`, `243038`, `1`, `2`).
