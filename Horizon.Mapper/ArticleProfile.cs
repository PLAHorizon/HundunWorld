using AutoMapper;
using Horizon.Model.Article;
using Horizon.Share.Dtos.Articles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Mapper
{
    public class ArticleProfile : Profile
    {
        public ArticleProfile()
        {
            CreateMap<ArticleAuthor, AuthorDto>();
            CreateMap<Article, ArticleDto>();
            CreateMap<ArticleCategory, ArticleCategoryDto>();
            CreateMap<ArticleChapters, ArticleChaptersDto>();
            CreateMap<ArticleChapters, ArticleChaptersItemDto>();
            CreateMap<ArticleComment, ArticleCommentDto>();
            CreateMap<ArticleDescription, ArticleDescriptionDto>();
            CreateMap<ArticleRead, ArticleReadDto>();

            CreateMap<UpdateArticleReadDto, ArticleRead>();

            CreateMap<CreateAuthorDto, ArticleAuthor>().ForMember(m => m.Id, e => e.MapFrom(p => Guid.NewGuid()));
            CreateMap<CreateArticleDto, Article>().ForMember(m => m.Id, e => e.MapFrom(p => Guid.NewGuid()));
            CreateMap<CreateArticleCategoryDto, ArticleCategory>();
            CreateMap<CreateArticleChaptersDto, ArticleChapters>().ForMember(m => m.Id, e => e.MapFrom(p => Guid.NewGuid()));
            CreateMap<CreateArticleCommentDto, ArticleComment>().ForMember(m => m.Id, e => e.MapFrom(p => Guid.NewGuid()));
            CreateMap<CreateArticleDescriptionDto, ArticleDescription>().ForMember(m => m.Id, e => e.MapFrom(p => Guid.NewGuid()));
            CreateMap<CreateArticleReadDto, ArticleRead>().ForMember(m => m.Id, e => e.MapFrom(p => Guid.NewGuid()));
        }
    }
}
