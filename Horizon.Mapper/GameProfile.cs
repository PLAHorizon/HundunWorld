using AutoMapper;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model;
using Horizon.Model.Article;
using Horizon.Model.GameModel;
using Horizon.Share.Dtos.Articles;
using Horizon.Share.Dtos.Games;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Mapper
{
    public class GameProfile : Profile
    {
        public GameProfile()
        {
            // 角色相关映射
            CreateMap<CreateCharacterRequest, CharacterEntity>()
                .ForMember(dest => dest.Id, opt => opt.Ignore()) // ID由数据库生成
                .ForMember(dest => dest.CreateTime, opt => opt.Ignore()) // 在业务逻辑中设置
                .ForMember(dest => dest.LastLoginTime, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.IsValid, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.Profession, opt => opt.MapFrom(src => (int)src.Profession))
                .ForMember(dest => dest.HairModel, opt => opt.MapFrom(src => src.Appearance.HairModel))
                .ForMember(dest => dest.HairColor, opt => opt.MapFrom(src => src.Appearance.HairColor))
                .ForMember(dest => dest.FaceModel, opt => opt.MapFrom(src => src.Appearance.FaceModel))
                .ForMember(dest => dest.SkinColor, opt => opt.MapFrom(src => src.Appearance.SkinColor))
                .ForMember(dest => dest.EyeColor, opt => opt.MapFrom(src => src.Appearance.EyeColor));
                
            CreateMap<CharacterEntity, CharacterInfo>()
                .BeforeMap((c1, c2) => c2.CharacterId = (ulong)c1.Id)
                .ForMember(dest => dest.Position, opt => opt.MapFrom(src => new Position 
                { 
                    X = src.PositionX, 
                    Y = src.PositionY, 
                    Z = src.PositionZ 
                }))
                .ForMember(dest => dest.Appearance, opt => opt.MapFrom(src => new AppearanceInfo
                {
                    HairModel = src.HairModel,
                    HairColor = src.HairColor,
                    FaceModel = src.FaceModel,
                    SkinColor = src.SkinColor,
                    EyeColor = src.EyeColor,
                    Clothing = 0 // 默认值
                }))
                .ForMember(dest => dest.Profession, opt => opt.MapFrom(src => (Profession)src.Profession))
                .ForMember(dest => dest.LastLoginTime, opt => opt.MapFrom(src => src.LastLoginTime.Value.Ticks));

            // 服务器相关映射
            CreateMap<ServerEntity, ServerInfo>()
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.IsValid ? 1 : 0));

            // 用户相关映射
            CreateMap<UserEntity, CharacterInfo>()
                .ForMember(dest => dest.CharacterId, opt => opt.MapFrom(src => (ulong)src.Id))
                .ForMember(dest => dest.CharacterName, opt => opt.MapFrom(src => src.AccountName))
                .ForMember(dest => dest.Level, opt => opt.MapFrom(src => 1)) // 默认等级
                .ForMember(dest => dest.LastLoginTime, opt => opt.MapFrom(src => src.LastLoginTime.Value.Ticks));

            // 游戏查询相关映射
            CreateMap<GameQueryDto, CharacterEntity>()
                .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.GameUserId))
                .ForMember(dest => dest.GameId, opt => opt.MapFrom(src => src.GameId));

            // 位置信息映射已通过其他方式处理
            // 注意: 元组映射不能用于表达式树，已移除

            // 外观信息映射
            CreateMap<AppearanceInfo, CharacterEntity>()
                .ForMember(dest => dest.HairModel, opt => opt.MapFrom(src => src.HairModel))
                .ForMember(dest => dest.HairColor, opt => opt.MapFrom(src => src.HairColor))
                .ForMember(dest => dest.FaceModel, opt => opt.MapFrom(src => src.FaceModel))
                .ForMember(dest => dest.SkinColor, opt => opt.MapFrom(src => src.SkinColor))
                .ForMember(dest => dest.EyeColor, opt => opt.MapFrom(src => src.EyeColor));

            //CreateMap<EquipmentAttachAttribut, EquipmnetSlotDto>();
        }
    }
}
