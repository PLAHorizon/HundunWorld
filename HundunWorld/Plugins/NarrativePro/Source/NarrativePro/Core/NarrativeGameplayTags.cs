using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.Core
{
    /// <summary>
    /// 全局 GameplayTag 定义单例。对应 UE5 FNarrativeGameplayTags。
    /// 借鉴 Lyra 的原生标签技术，用户无需手动添加标签即可直接使用。
    /// </summary>
    public static class NarrativeGameplayTags
    {
        private static bool _initialized = false;

        // ===== Ability 激活失败标签 =====
        public static GameplayTag Ability_ActivateFail_NoAmmo;
        public static GameplayTag Ability_ActivateFail_IsDead;
        public static GameplayTag Ability_ActivateFail_Cooldown;
        public static GameplayTag Ability_ActivateFail_Cost;
        public static GameplayTag Ability_ActivateFail_TagsBlocked;
        public static GameplayTag Ability_ActivateFail_TagsMissing;
        public static GameplayTag Ability_ActivateFail_Networking;
        public static GameplayTag Ability_ActivateFail_ActivationGroup;

        // ===== 伤害类型标签 =====
        public static GameplayTag Ability_DamageType_Heavy;
        public static GameplayTag Ability_DamageType_Melee;
        public static GameplayTag Ability_DamageType_Ranged;
        public static GameplayTag Ability_DamageType_Poison;

        // ===== 能力标签 =====
        public static GameplayTag Ability_WeaponFire;
        public static GameplayTag Ability_Death;
        public static GameplayTag Ability_MeleeAttack;
        public static GameplayTag Ability_MagicAttack;
        public static GameplayTag Ability_Aim;
        public static GameplayTag Ability_Reload;
        public static GameplayTag Ability_WeaponBash;
        public static GameplayTag Ability_Jump;
        public static GameplayTag Ability_Crouch;
        public static GameplayTag Ability_Sprint;
        public static GameplayTag Ability_Dodge;
        public static GameplayTag Ability_Block;
        public static GameplayTag Ability_WieldWeapon;
        public static GameplayTag Ability_WieldWeapon_Off;
        public static GameplayTag Ability_Interact_Sit;

        // ===== 相机标签 =====
        public static GameplayTag Camera_FirstPerson_ForceFollowHeadBoneLocked;
        public static GameplayTag Camera_FirstPerson_Follow3PHeadLocation;
        public static GameplayTag Camera_FirstPerson_Follow3PHeadRotation;
        public static GameplayTag Camera_FirstPerson_FollowControlRotation;
        public static GameplayTag Camera_FirstPerson_CameraInsideHead;
        public static GameplayTag Camera_FirstPerson_DisableFirstPersonRendering;
        public static GameplayTag Camera_FirstPerson_AlwaysTickFirstPersonMesh;
        public static GameplayTag Camera_Perspective_FirstPerson;
        public static GameplayTag Camera_Perspective_ThirdPerson;

        // ===== GameplayCue 标签 =====
        public static GameplayTag GameplayCue_TakeDamage;
        public static GameplayTag GameplayCue_Weapon_Fire;
        public static GameplayTag GameplayCue_Weapon_Impact;

        // ===== GameplayEvent 标签 =====
        public static GameplayTag GameplayEvent_BlockedAttack;
        public static GameplayTag GameplayEvent_Interact;
        public static GameplayTag GameplayEvent_Interact_SkipEntry;
        public static GameplayTag GameplayEvent_Interact_Steal;
        public static GameplayTag GameplayEvent_KilledEnemy;
        public static GameplayTag GameplayEvent_NotifyHolster;
        public static GameplayTag GameplayEvent_Death;
        public static GameplayTag GameplayEvent_Reload;
        public static GameplayTag GameplayEvent_ToggleWield_On;
        public static GameplayTag GameplayEvent_ToggleWield_Off;
        public static GameplayTag GameplayEvent_MeleeHit;
        public static GameplayTag GameplayEvent_EndAttack;
        public static GameplayTag GameplayEvent_NotifyInteract;
        public static GameplayTag GameplayEvent_WantsEndInteract;

        // ===== 角色创建器标签 =====
        public static GameplayTag CharacterCreator_Scalars;
        public static GameplayTag CharacterCreator_Vectors;
        public static GameplayTag CharacterCreator_Form_Male;
        public static GameplayTag CharacterCreator_Form_Female;

        // ===== 武器附件槽 =====
        public static GameplayTag Attachment_Slot_Scope;
        public static GameplayTag Attachment_Slot_Muzzle;

        // ===== 装备槽 =====
        public static GameplayTag Equipment_Slot_Ammo;
        public static GameplayTag Equipment_Slot_Helmet;
        public static GameplayTag Equipment_Slot_Offhand;
        public static GameplayTag Equipment_Slot_Body;
        public static GameplayTag Equipment_Slot_Torso;
        public static GameplayTag Equipment_Slot_Torso_1P;
        public static GameplayTag Equipment_Slot_Hands_1P;
        public static GameplayTag Equipment_Slot_Legs;
        public static GameplayTag Equipment_Slot_Feet;
        public static GameplayTag Equipment_Slot_Hands;
        public static GameplayTag Equipment_Slot_Backpack;
        public static GameplayTag Equipment_Slot_Necklace;
        public static GameplayTag Equipment_Slot_Weapon_Back;
        public static GameplayTag Equipment_Slot_Weapon_Hip;
        public static GameplayTag Equipment_Slot_Throwable;
        public static GameplayTag Equipment_Slot_Glasses;
        public static GameplayTag Equipment_Slot_Weapon_HipLeft;
        public static GameplayTag Equipment_Slot_Weapon_HipRight;
        public static GameplayTag Equipment_Slot_Weapon_BackA;
        public static GameplayTag Equipment_Slot_Weapon_BackB;

        // ===== 武器握持槽 =====
        public static GameplayTag Weapon_WieldSlot_Mainhand;
        public static GameplayTag Weapon_WieldSlot_Offhand;

        // ===== Groom 槽 =====
        public static GameplayTag Equipment_Slot_Groom_Hair;
        public static GameplayTag Equipment_Slot_Groom_Eyebrows;
        public static GameplayTag Equipment_Slot_Groom_Beard;
        public static GameplayTag Equipment_Slot_Groom_Fuzz;
        public static GameplayTag Equipment_Slot_Groom_Moustache;
        public static GameplayTag Equipment_Slot_Groom_Eyelashes;

        public static GameplayTag Equipment_Slot_Mesh_Hair;
        public static GameplayTag Equipment_Slot_Mesh_Eyebrows;
        public static GameplayTag Equipment_Slot_Mesh_Beard;
        public static GameplayTag Equipment_Slot_Mesh_Fuzz;
        public static GameplayTag Equipment_Slot_Mesh_Moustache;
        public static GameplayTag Equipment_Slot_Mesh_Eyelashes;

        public static GameplayTag Equipment_Slot_Character_Mesh;
        public static GameplayTag Equipment_Slot_Character_LocalMesh;
        public static GameplayTag Equipment_Slot_Face;

        // ===== 动画覆盖层 =====
        public static GameplayTag Narrative_Anim_OverrideLayer_Ragdoll;
        public static GameplayTag Narrative_Anim_OverrideLayer_Swimming;
        public static GameplayTag Narrative_Anim_OverrideLayer_Driving;
        public static GameplayTag Narrative_Anim_OverrideLayer_Climbing;

        // ===== 动画集 - 退缩 =====
        public static GameplayTag Narrative_AnimSets_Flinch_Back;
        public static GameplayTag Narrative_AnimSets_Flinch_Left;
        public static GameplayTag Narrative_AnimSets_Flinch_Right;
        public static GameplayTag Narrative_AnimSets_Flinch_Forward;

        // ===== 动画集 - 踉跄 =====
        public static GameplayTag Narrative_AnimSets_Stumble_Back;
        public static GameplayTag Narrative_AnimSets_Stumble_Left;
        public static GameplayTag Narrative_AnimSets_Stumble_Right;
        public static GameplayTag Narrative_AnimSets_Stumble_Forward;

        // ===== 动画集 - 攻击 =====
        public static GameplayTag Narrative_AnimSets_Attack_Unarmed_Light;
        public static GameplayTag Narrative_AnimSets_Attack_Unarmed_Heavy;

        // ===== NPC 状态标签 =====
        public static GameplayTag State_NPC_Activity_Idle;
        public static GameplayTag State_NPC_Activity_Following;
        public static GameplayTag State_NPC_Activity_Attacking;
        public static GameplayTag State_NPC_IsAggressive;
        public static GameplayTag State_NPC_DisableAggro;
        public static GameplayTag State_NPC_IsBusy;
        public static GameplayTag State_NPC_DisableLooting;

        // ===== 玩家状态标签 =====
        public static GameplayTag State_Player_WantsCinematicBars;
        public static GameplayTag State_Player_WantsHideHUD;
        public static GameplayTag State_Player_DoNotDisturb;
        public static GameplayTag State_Player_IgnoreLookInput;

        // ===== 通用状态标签 =====
        public static GameplayTag State_InvisibleToEnemies;
        public static GameplayTag State_Invulnerable;
        public static GameplayTag State_SequencerControlled;
        public static GameplayTag State_RootMotionControlled;
        public static GameplayTag State_DialogueControlled;
        public static GameplayTag State_DontReturnToSpawn;
        public static GameplayTag State_Sitting;
        public static GameplayTag State_Sleeping;
        public static GameplayTag State_Movement_PostponePathUpdates;

        // ===== 移动状态标签 =====
        public static GameplayTag State_Movement_Lock;
        public static GameplayTag State_Movement_Falling;
        public static GameplayTag State_Movement_Swimming;
        public static GameplayTag State_Movement_Walking;
        public static GameplayTag State_Movement_SlowWalking;
        public static GameplayTag State_Movement_Climbing;
        public static GameplayTag State_Movement_Ragdoll;
        public static GameplayTag State_Movement_InCover;

        // ===== 其他状态标签 =====
        public static GameplayTag State_IsDead;
        public static GameplayTag State_Busy;
        public static GameplayTag State_OnMount;
        public static GameplayTag State_Interacting;
        public static GameplayTag State_Weapon_IsReloading;
        public static GameplayTag State_Weapon_IsFiring;
        public static GameplayTag State_Weapon_IsAiming;
        public static GameplayTag State_Weapon_Equipping;
        public static GameplayTag State_Weapon_Equipped;
        public static GameplayTag State_Weapon_BlockFiring;
        public static GameplayTag State_Weapon_Blocking;
        public static GameplayTag State_Weapon_ForceHolster;
        public static GameplayTag State_BlockFastTravel;
        public static GameplayTag State_BlockSaving;
        public static GameplayTag State_UI_HideCrosshair;

        // ===== 标签对话 =====
        public static GameplayTag TaggedDialogue_Greet;
        public static GameplayTag TaggedDialogue_Farewell;
        public static GameplayTag TaggedDialogue_Taunt;
        public static GameplayTag TaggedDialogue_Attack;
        public static GameplayTag TaggedDialogue_BeginAttacking;
        public static GameplayTag TaggedDialogue_Investigate_HeardSound_StartSearch;
        public static GameplayTag TaggedDialogue_Investigate_HeardSound_CouldntFindAnything;
        public static GameplayTag TaggedDialogue_Investigate_HeardSound_FoundEnemy;
        public static GameplayTag TaggedDialogue_Investigate_SearchForEnemy_StartSearch;
        public static GameplayTag TaggedDialogue_Investigate_SearchForEnemy_FoundEnemy;
        public static GameplayTag TaggedDialogue_Investigate_SearchForEnemy_CouldntFindEnemy;
        public static GameplayTag TaggedDialogue_DidntFindEnemy;
        public static GameplayTag TaggedDialogue_FriendlyFire;

        // ===== UI 层标签 =====
        public static GameplayTag UI_Layer_Game;
        public static GameplayTag UI_Layer_Menu;
        public static GameplayTag UI_Layer_Modal;

        // ===== SetByCaller 标签 =====
        public static GameplayTag SetByCaller_Damage;
        public static GameplayTag SetByCaller_Heal;
        public static GameplayTag SetByCaller_AttackDamage;
        public static GameplayTag SetByCaller_AttackRating;
        public static GameplayTag SetByCaller_StealthRating;
        public static GameplayTag SetByCaller_Armor;
        public static GameplayTag SetByCaller_Health;
        public static GameplayTag SetByCaller_MaxHealth;
        public static GameplayTag SetByCaller_Stamina;
        public static GameplayTag SetByCaller_MaxStamina;
        public static GameplayTag SetByCaller_Duration;
        public static GameplayTag SetByCaller_XP;
        public static GameplayTag SetByCaller_Sneak;

        // ===== 据点标签 =====
        public static GameplayTag Narrative_Settlements;
        public static GameplayTag Narrative_Settlements_Test_DemoHall;
        public static GameplayTag Narrative_Settlements_Test_BanditCamp;
        public static GameplayTag Narrative_Settlements_Test_WeaponStore;

        // ===== POI 标签 =====
        public static GameplayTag Narrative_POIs;
        public static GameplayTag Narrative_POIs_Test_DemoHall;
        public static GameplayTag Narrative_POIs_Test_BanditCamp;
        public static GameplayTag Narrative_POIs_Test_WeaponStore;

        // ===== 阵营标签 =====
        public static GameplayTag Narrative_Factions;
        public static GameplayTag Narrative_Factions_Heroes;
        public static GameplayTag Narrative_Factions_Bandits;
        public static GameplayTag Narrative_Factions_HostileAll;
        public static GameplayTag Narrative_Factions_HostileOthers;
        public static GameplayTag Narrative_Factions_FriendlyAll;

        // ===== 输入标签 =====
        public static GameplayTag Narrative_Input_None;
        public static GameplayTag Narrative_Input_Confirm;
        public static GameplayTag Narrative_Input_Cancel;
        public static GameplayTag Narrative_Input_Attack;
        public static GameplayTag Narrative_Input_AltAttack;
        public static GameplayTag Narrative_Input_Ability1;
        public static GameplayTag Narrative_Input_Ability2;
        public static GameplayTag Narrative_Input_Ability3;
        public static GameplayTag Narrative_Input_Reload;
        public static GameplayTag Narrative_Input_Jump;
        public static GameplayTag Narrative_Input_Crouch;
        public static GameplayTag Narrative_Input_Sprint;
        public static GameplayTag Narrative_Input_Throw;

        /// <summary>初始化所有原生标签。在插件启动时调用一次。</summary>
        public static void InitializeNativeTags()
        {
            if (_initialized) return;
            _initialized = true;

            // Ability 激活失败
            Ability_ActivateFail_NoAmmo = RequestTag("Narrative.Ability.ActivateFail.NoAmmo");
            Ability_ActivateFail_IsDead = RequestTag("Narrative.Ability.ActivateFail.IsDead");
            Ability_ActivateFail_Cooldown = RequestTag("Narrative.Ability.ActivateFail.Cooldown");
            Ability_ActivateFail_Cost = RequestTag("Narrative.Ability.ActivateFail.Cost");
            Ability_ActivateFail_TagsBlocked = RequestTag("Narrative.Ability.ActivateFail.TagsBlocked");
            Ability_ActivateFail_TagsMissing = RequestTag("Narrative.Ability.ActivateFail.TagsMissing");
            Ability_ActivateFail_Networking = RequestTag("Narrative.Ability.ActivateFail.Networking");
            Ability_ActivateFail_ActivationGroup = RequestTag("Narrative.Ability.ActivateFail.ActivationGroup");

            // 伤害类型
            Ability_DamageType_Heavy = RequestTag("Narrative.Ability.DamageType.Heavy");
            Ability_DamageType_Melee = RequestTag("Narrative.Ability.DamageType.Melee");
            Ability_DamageType_Ranged = RequestTag("Narrative.Ability.DamageType.Ranged");
            Ability_DamageType_Poison = RequestTag("Narrative.Ability.DamageType.Poison");

            // 能力
            Ability_WeaponFire = RequestTag("Narrative.Ability.WeaponFire");
            Ability_Death = RequestTag("Narrative.Ability.Death");
            Ability_MeleeAttack = RequestTag("Narrative.Ability.MeleeAttack");
            Ability_MagicAttack = RequestTag("Narrative.Ability.MagicAttack");
            Ability_Aim = RequestTag("Narrative.Ability.Aim");
            Ability_Reload = RequestTag("Narrative.Ability.Reload");
            Ability_WeaponBash = RequestTag("Narrative.Ability.WeaponBash");
            Ability_Jump = RequestTag("Narrative.Ability.Jump");
            Ability_Crouch = RequestTag("Narrative.Ability.Crouch");
            Ability_Sprint = RequestTag("Narrative.Ability.Sprint");
            Ability_Dodge = RequestTag("Narrative.Ability.Dodge");
            Ability_Block = RequestTag("Narrative.Ability.Block");
            Ability_WieldWeapon = RequestTag("Narrative.Ability.WieldWeapon");
            Ability_WieldWeapon_Off = RequestTag("Narrative.Ability.WieldWeapon.Off");
            Ability_Interact_Sit = RequestTag("Narrative.Ability.Interact.Sit");

            // 相机
            Camera_FirstPerson_ForceFollowHeadBoneLocked = RequestTag("Narrative.Camera.FirstPerson.ForceFollowHeadBoneLocked");
            Camera_FirstPerson_Follow3PHeadLocation = RequestTag("Narrative.Camera.FirstPerson.Follow3PHeadLocation");
            Camera_FirstPerson_Follow3PHeadRotation = RequestTag("Narrative.Camera.FirstPerson.Follow3PHeadRotation");
            Camera_FirstPerson_FollowControlRotation = RequestTag("Narrative.Camera.FirstPerson.FollowControlRotation");
            Camera_FirstPerson_CameraInsideHead = RequestTag("Narrative.Camera.FirstPerson.CameraInsideHead");
            Camera_FirstPerson_DisableFirstPersonRendering = RequestTag("Narrative.Camera.FirstPerson.DisableFirstPersonRendering");
            Camera_FirstPerson_AlwaysTickFirstPersonMesh = RequestTag("Narrative.Camera.FirstPerson.AlwaysTickFirstPersonMesh");
            Camera_Perspective_FirstPerson = RequestTag("Narrative.Camera.Perspective.FirstPerson");
            Camera_Perspective_ThirdPerson = RequestTag("Narrative.Camera.Perspective.ThirdPerson");

            // GameplayCue
            GameplayCue_TakeDamage = RequestTag("Narrative.GameplayCue.TakeDamage");
            GameplayCue_Weapon_Fire = RequestTag("Narrative.GameplayCue.Weapon.Fire");
            GameplayCue_Weapon_Impact = RequestTag("Narrative.GameplayCue.Weapon.Impact");

            // GameplayEvent
            GameplayEvent_BlockedAttack = RequestTag("Narrative.GameplayEvent.BlockedAttack");
            GameplayEvent_Interact = RequestTag("Narrative.GameplayEvent.Interact");
            GameplayEvent_Interact_SkipEntry = RequestTag("Narrative.GameplayEvent.Interact.SkipEntry");
            GameplayEvent_Interact_Steal = RequestTag("Narrative.GameplayEvent.Interact.Steal");
            GameplayEvent_KilledEnemy = RequestTag("Narrative.GameplayEvent.KilledEnemy");
            GameplayEvent_NotifyHolster = RequestTag("Narrative.GameplayEvent.NotifyHolster");
            GameplayEvent_Death = RequestTag("Narrative.GameplayEvent.Death");
            GameplayEvent_Reload = RequestTag("Narrative.GameplayEvent.Reload");
            GameplayEvent_ToggleWield_On = RequestTag("Narrative.GameplayEvent.ToggleWield.On");
            GameplayEvent_ToggleWield_Off = RequestTag("Narrative.GameplayEvent.ToggleWield.Off");
            GameplayEvent_MeleeHit = RequestTag("Narrative.GameplayEvent.MeleeHit");
            GameplayEvent_EndAttack = RequestTag("Narrative.GameplayEvent.EndAttack");
            GameplayEvent_NotifyInteract = RequestTag("Narrative.GameplayEvent.NotifyInteract");
            GameplayEvent_WantsEndInteract = RequestTag("Narrative.GameplayEvent.WantsEndInteract");

            // 角色创建器
            CharacterCreator_Scalars = RequestTag("Narrative.CharacterCreator.Scalars");
            CharacterCreator_Vectors = RequestTag("Narrative.CharacterCreator.Vectors");
            CharacterCreator_Form_Male = RequestTag("Narrative.CharacterCreator.Form.Male");
            CharacterCreator_Form_Female = RequestTag("Narrative.CharacterCreator.Form.Female");

            // 附件槽
            Attachment_Slot_Scope = RequestTag("Narrative.Attachment.Slot.Scope");
            Attachment_Slot_Muzzle = RequestTag("Narrative.Attachment.Slot.Muzzle");

            // 装备槽
            Equipment_Slot_Ammo = RequestTag("Narrative.Equipment.Slot.Ammo");
            Equipment_Slot_Helmet = RequestTag("Narrative.Equipment.Slot.Helmet");
            Equipment_Slot_Offhand = RequestTag("Narrative.Equipment.Slot.Offhand");
            Equipment_Slot_Body = RequestTag("Narrative.Equipment.Slot.Body");
            Equipment_Slot_Torso = RequestTag("Narrative.Equipment.Slot.Torso");
            Equipment_Slot_Torso_1P = RequestTag("Narrative.Equipment.Slot.Torso.1P");
            Equipment_Slot_Hands_1P = RequestTag("Narrative.Equipment.Slot.Hands.1P");
            Equipment_Slot_Legs = RequestTag("Narrative.Equipment.Slot.Legs");
            Equipment_Slot_Feet = RequestTag("Narrative.Equipment.Slot.Feet");
            Equipment_Slot_Hands = RequestTag("Narrative.Equipment.Slot.Hands");
            Equipment_Slot_Backpack = RequestTag("Narrative.Equipment.Slot.Backpack");
            Equipment_Slot_Necklace = RequestTag("Narrative.Equipment.Slot.Necklace");
            Equipment_Slot_Weapon_Back = RequestTag("Narrative.Equipment.Slot.Weapon.Back");
            Equipment_Slot_Weapon_Hip = RequestTag("Narrative.Equipment.Slot.Weapon.Hip");
            Equipment_Slot_Throwable = RequestTag("Narrative.Equipment.Slot.Throwable");
            Equipment_Slot_Glasses = RequestTag("Narrative.Equipment.Slot.Glasses");
            Equipment_Slot_Weapon_HipLeft = RequestTag("Narrative.Equipment.Slot.Weapon.HipLeft");
            Equipment_Slot_Weapon_HipRight = RequestTag("Narrative.Equipment.Slot.Weapon.HipRight");
            Equipment_Slot_Weapon_BackA = RequestTag("Narrative.Equipment.Slot.Weapon.BackA");
            Equipment_Slot_Weapon_BackB = RequestTag("Narrative.Equipment.Slot.Weapon.BackB");

            // 武器握持槽
            Weapon_WieldSlot_Mainhand = RequestTag("Narrative.Weapon.WieldSlot.Mainhand");
            Weapon_WieldSlot_Offhand = RequestTag("Narrative.Weapon.WieldSlot.Offhand");

            // Groom 槽
            Equipment_Slot_Groom_Hair = RequestTag("Narrative.Equipment.Slot.Groom.Hair");
            Equipment_Slot_Groom_Eyebrows = RequestTag("Narrative.Equipment.Slot.Groom.Eyebrows");
            Equipment_Slot_Groom_Beard = RequestTag("Narrative.Equipment.Slot.Groom.Beard");
            Equipment_Slot_Groom_Fuzz = RequestTag("Narrative.Equipment.Slot.Groom.Fuzz");
            Equipment_Slot_Groom_Moustache = RequestTag("Narrative.Equipment.Slot.Groom.Moustache");
            Equipment_Slot_Groom_Eyelashes = RequestTag("Narrative.Equipment.Slot.Groom.Eyelashes");

            Equipment_Slot_Mesh_Hair = RequestTag("Narrative.Equipment.Slot.Mesh.Hair");
            Equipment_Slot_Mesh_Eyebrows = RequestTag("Narrative.Equipment.Slot.Mesh.Eyebrows");
            Equipment_Slot_Mesh_Beard = RequestTag("Narrative.Equipment.Slot.Mesh.Beard");
            Equipment_Slot_Mesh_Fuzz = RequestTag("Narrative.Equipment.Slot.Mesh.Fuzz");
            Equipment_Slot_Mesh_Moustache = RequestTag("Narrative.Equipment.Slot.Mesh.Moustache");
            Equipment_Slot_Mesh_Eyelashes = RequestTag("Narrative.Equipment.Slot.Mesh.Eyelashes");

            Equipment_Slot_Character_Mesh = RequestTag("Narrative.Equipment.Slot.Character.Mesh");
            Equipment_Slot_Character_LocalMesh = RequestTag("Narrative.Equipment.Slot.Character.LocalMesh");
            Equipment_Slot_Face = RequestTag("Narrative.Equipment.Slot.Face");

            // 动画覆盖层
            Narrative_Anim_OverrideLayer_Ragdoll = RequestTag("Narrative.Anim.OverrideLayer.Ragdoll");
            Narrative_Anim_OverrideLayer_Swimming = RequestTag("Narrative.Anim.OverrideLayer.Swimming");
            Narrative_Anim_OverrideLayer_Driving = RequestTag("Narrative.Anim.OverrideLayer.Driving");
            Narrative_Anim_OverrideLayer_Climbing = RequestTag("Narrative.Anim.OverrideLayer.Climbing");

            // 动画集 - 退缩
            Narrative_AnimSets_Flinch_Back = RequestTag("Narrative.AnimSets.Flinch.Back");
            Narrative_AnimSets_Flinch_Left = RequestTag("Narrative.AnimSets.Flinch.Left");
            Narrative_AnimSets_Flinch_Right = RequestTag("Narrative.AnimSets.Flinch.Right");
            Narrative_AnimSets_Flinch_Forward = RequestTag("Narrative.AnimSets.Flinch.Forward");

            // 动画集 - 踉跄
            Narrative_AnimSets_Stumble_Back = RequestTag("Narrative.AnimSets.Stumble.Back");
            Narrative_AnimSets_Stumble_Left = RequestTag("Narrative.AnimSets.Stumble.Left");
            Narrative_AnimSets_Stumble_Right = RequestTag("Narrative.AnimSets.Stumble.Right");
            Narrative_AnimSets_Stumble_Forward = RequestTag("Narrative.AnimSets.Stumble.Forward");

            // 动画集 - 攻击
            Narrative_AnimSets_Attack_Unarmed_Light = RequestTag("Narrative.AnimSets.Attack.Unarmed.Light");
            Narrative_AnimSets_Attack_Unarmed_Heavy = RequestTag("Narrative.AnimSets.Attack.Unarmed.Heavy");

            // NPC 状态
            State_NPC_Activity_Idle = RequestTag("Narrative.State.NPC.Activity.Idle");
            State_NPC_Activity_Following = RequestTag("Narrative.State.NPC.Activity.Following");
            State_NPC_Activity_Attacking = RequestTag("Narrative.State.NPC.Activity.Attacking");
            State_NPC_IsAggressive = RequestTag("Narrative.State.NPC.IsAggressive");
            State_NPC_DisableAggro = RequestTag("Narrative.State.NPC.DisableAggro");
            State_NPC_IsBusy = RequestTag("Narrative.State.NPC.IsBusy");
            State_NPC_DisableLooting = RequestTag("Narrative.State.NPC.DisableLooting");

            // 玩家状态
            State_Player_WantsCinematicBars = RequestTag("Narrative.State.Player.WantsCinematicBars");
            State_Player_WantsHideHUD = RequestTag("Narrative.State.Player.WantsHideHUD");
            State_Player_DoNotDisturb = RequestTag("Narrative.State.Player.DoNotDisturb");
            State_Player_IgnoreLookInput = RequestTag("Narrative.State.Player.IgnoreLookInput");

            // 通用状态
            State_InvisibleToEnemies = RequestTag("Narrative.State.InvisibleToEnemies");
            State_Invulnerable = RequestTag("Narrative.State.Invulnerable");
            State_SequencerControlled = RequestTag("Narrative.State.SequencerControlled");
            State_RootMotionControlled = RequestTag("Narrative.State.RootMotionControlled");
            State_DialogueControlled = RequestTag("Narrative.State.DialogueControlled");
            State_DontReturnToSpawn = RequestTag("Narrative.State.DontReturnToSpawn");
            State_Sitting = RequestTag("Narrative.State.Sitting");
            State_Sleeping = RequestTag("Narrative.State.Sleeping");
            State_Movement_PostponePathUpdates = RequestTag("Narrative.State.Movement.PostponePathUpdates");

            // 移动状态
            State_Movement_Lock = RequestTag("Narrative.State.Movement.Lock");
            State_Movement_Falling = RequestTag("Narrative.State.Movement.Falling");
            State_Movement_Swimming = RequestTag("Narrative.State.Movement.Swimming");
            State_Movement_Walking = RequestTag("Narrative.State.Movement.Walking");
            State_Movement_SlowWalking = RequestTag("Narrative.State.Movement.SlowWalking");
            State_Movement_Climbing = RequestTag("Narrative.State.Movement.Climbing");
            State_Movement_Ragdoll = RequestTag("Narrative.State.Movement.Ragdoll");
            State_Movement_InCover = RequestTag("Narrative.State.Movement.InCover");

            // 其他状态
            State_IsDead = RequestTag("Narrative.State.IsDead");
            State_Busy = RequestTag("Narrative.State.Busy");
            State_OnMount = RequestTag("Narrative.State.OnMount");
            State_Interacting = RequestTag("Narrative.State.Interacting");
            State_Weapon_IsReloading = RequestTag("Narrative.State.Weapon.IsReloading");
            State_Weapon_IsFiring = RequestTag("Narrative.State.Weapon.IsFiring");
            State_Weapon_IsAiming = RequestTag("Narrative.State.Weapon.IsAiming");
            State_Weapon_Equipping = RequestTag("Narrative.State.Weapon.Equipping");
            State_Weapon_Equipped = RequestTag("Narrative.State.Weapon.Equipped");
            State_Weapon_BlockFiring = RequestTag("Narrative.State.Weapon.BlockFiring");
            State_Weapon_Blocking = RequestTag("Narrative.State.Weapon.Blocking");
            State_Weapon_ForceHolster = RequestTag("Narrative.State.Weapon.ForceHolster");
            State_BlockFastTravel = RequestTag("Narrative.State.BlockFastTravel");
            State_BlockSaving = RequestTag("Narrative.State.BlockSaving");
            State_UI_HideCrosshair = RequestTag("Narrative.State.UI.HideCrosshair");

            // 标签对话
            TaggedDialogue_Greet = RequestTag("Narrative.TaggedDialogue.Greet");
            TaggedDialogue_Farewell = RequestTag("Narrative.TaggedDialogue.Farewell");
            TaggedDialogue_Taunt = RequestTag("Narrative.TaggedDialogue.Taunt");
            TaggedDialogue_Attack = RequestTag("Narrative.TaggedDialogue.Attack");
            TaggedDialogue_BeginAttacking = RequestTag("Narrative.TaggedDialogue.BeginAttacking");
            TaggedDialogue_Investigate_HeardSound_StartSearch = RequestTag("Narrative.TaggedDialogue.Investigate.HeardSound.StartSearch");
            TaggedDialogue_Investigate_HeardSound_CouldntFindAnything = RequestTag("Narrative.TaggedDialogue.Investigate.HeardSound.CouldntFindAnything");
            TaggedDialogue_Investigate_HeardSound_FoundEnemy = RequestTag("Narrative.TaggedDialogue.Investigate.HeardSound.FoundEnemy");
            TaggedDialogue_Investigate_SearchForEnemy_StartSearch = RequestTag("Narrative.TaggedDialogue.Investigate.SearchForEnemy.StartSearch");
            TaggedDialogue_Investigate_SearchForEnemy_FoundEnemy = RequestTag("Narrative.TaggedDialogue.Investigate.SearchForEnemy.FoundEnemy");
            TaggedDialogue_Investigate_SearchForEnemy_CouldntFindEnemy = RequestTag("Narrative.TaggedDialogue.Investigate.SearchForEnemy.CouldntFindEnemy");
            TaggedDialogue_DidntFindEnemy = RequestTag("Narrative.TaggedDialogue.DidntFindEnemy");
            TaggedDialogue_FriendlyFire = RequestTag("Narrative.TaggedDialogue.FriendlyFire");

            // UI 层
            UI_Layer_Game = RequestTag("Narrative.UI.Layer.Game");
            UI_Layer_Menu = RequestTag("Narrative.UI.Layer.Menu");
            UI_Layer_Modal = RequestTag("Narrative.UI.Layer.Modal");

            // SetByCaller
            SetByCaller_Damage = RequestTag("Narrative.SetByCaller.Damage");
            SetByCaller_Heal = RequestTag("Narrative.SetByCaller.Heal");
            SetByCaller_AttackDamage = RequestTag("Narrative.SetByCaller.AttackDamage");
            SetByCaller_AttackRating = RequestTag("Narrative.SetByCaller.AttackRating");
            SetByCaller_StealthRating = RequestTag("Narrative.SetByCaller.StealthRating");
            SetByCaller_Armor = RequestTag("Narrative.SetByCaller.Armor");
            SetByCaller_Health = RequestTag("Narrative.SetByCaller.Health");
            SetByCaller_MaxHealth = RequestTag("Narrative.SetByCaller.MaxHealth");
            SetByCaller_Stamina = RequestTag("Narrative.SetByCaller.Stamina");
            SetByCaller_MaxStamina = RequestTag("Narrative.SetByCaller.MaxStamina");
            SetByCaller_Duration = RequestTag("Narrative.SetByCaller.Duration");
            SetByCaller_XP = RequestTag("Narrative.SetByCaller.XP");
            SetByCaller_Sneak = RequestTag("Narrative.SetByCaller.Sneak");

            // 据点
            Narrative_Settlements = RequestTag("Narrative.Settlements");
            Narrative_Settlements_Test_DemoHall = RequestTag("Narrative.Settlements.Test.DemoHall");
            Narrative_Settlements_Test_BanditCamp = RequestTag("Narrative.Settlements.Test.BanditCamp");
            Narrative_Settlements_Test_WeaponStore = RequestTag("Narrative.Settlements.Test.WeaponStore");

            // POI
            Narrative_POIs = RequestTag("Narrative.POIs");
            Narrative_POIs_Test_DemoHall = RequestTag("Narrative.POIs.Test.DemoHall");
            Narrative_POIs_Test_BanditCamp = RequestTag("Narrative.POIs.Test.BanditCamp");
            Narrative_POIs_Test_WeaponStore = RequestTag("Narrative.POIs.Test.WeaponStore");

            // 阵营
            Narrative_Factions = RequestTag("Narrative.Factions");
            Narrative_Factions_Heroes = RequestTag("Narrative.Factions.Heroes");
            Narrative_Factions_Bandits = RequestTag("Narrative.Factions.Bandits");
            Narrative_Factions_HostileAll = RequestTag("Narrative.Factions.HostileAll");
            Narrative_Factions_HostileOthers = RequestTag("Narrative.Factions.HostileOthers");
            Narrative_Factions_FriendlyAll = RequestTag("Narrative.Factions.FriendlyAll");

            // 输入
            Narrative_Input_None = RequestTag("Narrative.Input.None");
            Narrative_Input_Confirm = RequestTag("Narrative.Input.Confirm");
            Narrative_Input_Cancel = RequestTag("Narrative.Input.Cancel");
            Narrative_Input_Attack = RequestTag("Narrative.Input.Attack");
            Narrative_Input_AltAttack = RequestTag("Narrative.Input.AltAttack");
            Narrative_Input_Ability1 = RequestTag("Narrative.Input.Ability1");
            Narrative_Input_Ability2 = RequestTag("Narrative.Input.Ability2");
            Narrative_Input_Ability3 = RequestTag("Narrative.Input.Ability3");
            Narrative_Input_Reload = RequestTag("Narrative.Input.Reload");
            Narrative_Input_Jump = RequestTag("Narrative.Input.Jump");
            Narrative_Input_Crouch = RequestTag("Narrative.Input.Crouch");
            Narrative_Input_Sprint = RequestTag("Narrative.Input.Sprint");
            Narrative_Input_Throw = RequestTag("Narrative.Input.Throw");

            NarrativeLog.Log("NarrativeGameplayTags 初始化完成");
        }

        /// <summary>按字符串查找标签。Flax 中无 GameplayTagsManager，直接构造 GameplayTag。</summary>
        public static GameplayTag FindTagByString(string tagString, bool bMatchPartialString = false)
        {
            return new GameplayTag(tagString);
        }

        private static GameplayTag RequestTag(string tagName)
        {
            return new GameplayTag(tagName);
        }
    }
}
