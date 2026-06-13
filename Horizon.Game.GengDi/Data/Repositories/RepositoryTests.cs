using System;
using System.Diagnostics;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class RepositoryTests
    {
        public static void RunTests()
        {
            Debug.Print("开始测试数据模型和数据访问层...");

            // 初始化数据库
            try
            {
                DatabaseManager.Initialize();
                Debug.Print("数据库初始化成功");
            }
            catch (Exception ex)
            {
                Debug.Print($"数据库初始化失败: {ex.Message}");
                return;
            }

            // 测试游戏仓库
            TestGameRepository();

            // 测试用户仓库
            TestUserRepository();

            // 测试消息仓库
            TestMessageRepository();

            // 测试群组仓库
            TestGroupRepository();

            // 测试新闻仓库
            TestNewsRepository();

            // 测试下载任务仓库
            TestDownloadTaskRepository();

            Debug.Print("测试完成");
        }

        private static void TestGameRepository()
        {
            Debug.Print("\n测试游戏仓库...");
            var repository = new GameRepository();

            // 创建测试游戏
            var game = new Models.GameInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = "测试游戏",
                Description = "这是一个测试游戏",
                CoverImage = "cover.jpg",
                Developer = "测试开发者",
                Publisher = "测试发行商",
                ReleaseDate = DateTime.Now,
                Category = "动作",
                IsInstalled = false,
                Version = "1.0.0"
            };

            // 添加游戏
            repository.Add(game);
            Debug.Print("添加游戏成功");

            // 获取游戏
            var retrievedGame = repository.GetById(game.Id);
            if (retrievedGame != null)
            {
                Debug.Print($"获取游戏成功: {retrievedGame.Name}");
            }
            else
            {
                Debug.Print("获取游戏失败");
            }

            // 更新游戏
            game.Name = "更新后的测试游戏";
            repository.Update(game);
            var updatedGame = repository.GetById(game.Id);
            if (updatedGame != null && updatedGame.Name == "更新后的测试游戏")
            {
                Debug.Print("更新游戏成功");
            }
            else
            {
                Debug.Print("更新游戏失败");
            }

            // 删除游戏
            repository.Delete(game.Id);
            var deletedGame = repository.GetById(game.Id);
            if (deletedGame == null)
            {
                Debug.Print("删除游戏成功");
            }
            else
            {
                Debug.Print("删除游戏失败");
            }
        }

        private static void TestUserRepository()
        {
            Debug.Print("\n测试用户仓库...");
            var repository = new UserRepository();

            // 创建测试用户
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "testuser",
                Email = "test@example.com",
                PasswordHash = "hashedpassword",
                Avatar = "avatar.jpg",
                Bio = "这是一个测试用户",
                Status = UserStatus.Online
            };

            // 添加用户
            repository.Add(user);
            Debug.Print("添加用户成功");

            // 通过用户名获取用户
            var retrievedUser = repository.GetByUsername(user.Username);
            if (retrievedUser != null)
            {
                Debug.Print($"通过用户名获取用户成功: {retrievedUser.Username}");
            }
            else
            {
                Debug.Print("通过用户名获取用户失败");
            }

            // 更新用户
            user.Bio = "更新后的个人简介";
            repository.Update(user);
            var updatedUser = repository.GetById(user.Id);
            if (updatedUser != null && updatedUser.Bio == "更新后的个人简介")
            {
                Debug.Print("更新用户成功");
            }
            else
            {
                Debug.Print("更新用户失败");
            }

            // 删除用户
            repository.Delete(user.Id);
            var deletedUser = repository.GetById(user.Id);
            if (deletedUser == null)
            {
                Debug.Print("删除用户成功");
            }
            else
            {
                Debug.Print("删除用户失败");
            }
        }

        private static void TestMessageRepository()
        {
            Debug.Print("\n测试消息仓库...");
            var repository = new MessageRepository();

            // 创建测试消息
            var message = new Models.IMMessage
            {
                Id = Guid.NewGuid().ToString(),
                SenderId = "sender1",
                ReceiverId = "receiver1",
                Content = "测试消息内容",
                Timestamp = DateTime.Now,
                IsRead = false,
                Type = MessageType.Text
            };

            // 添加消息
            repository.Add(message);
            Debug.Print("添加消息成功");

            // 获取消息
            var retrievedMessage = repository.GetById(message.Id);
            if (retrievedMessage != null)
            {
                Debug.Print($"获取消息成功: {retrievedMessage.Content}");
            }
            else
            {
                Debug.Print("获取消息失败");
            }

            // 更新消息
            message.IsRead = true;
            repository.Update(message);
            var updatedMessage = repository.GetById(message.Id);
            if (updatedMessage != null && updatedMessage.IsRead)
            {
                Debug.Print("更新消息成功");
            }
            else
            {
                Debug.Print("更新消息失败");
            }

            // 删除消息
            repository.Delete(message.Id);
            var deletedMessage = repository.GetById(message.Id);
            if (deletedMessage == null)
            {
                Debug.Print("删除消息成功");
            }
            else
            {
                Debug.Print("删除消息失败");
            }
        }

        private static void TestGroupRepository()
        {
            Debug.Print("\n测试群组仓库...");
            var repository = new GroupRepository();

            // 创建测试群组
            var group = new Group
            {
                Id = Guid.NewGuid().ToString(),
                Name = "测试群组",
                Description = "这是一个测试群组",
                Icon = "groupicon.jpg",
                CreatorId = "creator1",
                CreatedAt = DateTime.Now
            };
            group.Members.Add("user1");
            group.Members.Add("user2");
            group.Admins.Add("creator1");

            // 添加群组
            repository.Add(group);
            Debug.Print("添加群组成功");

            // 获取群组
            var retrievedGroup = repository.GetById(group.Id);
            if (retrievedGroup != null)
            {
                Debug.Print($"获取群组成功: {retrievedGroup.Name}");
            }
            else
            {
                Debug.Print("获取群组失败");
            }

            // 更新群组
            group.Name = "更新后的测试群组";
            repository.Update(group);
            var updatedGroup = repository.GetById(group.Id);
            if (updatedGroup != null && updatedGroup.Name == "更新后的测试群组")
            {
                Debug.Print("更新群组成功");
            }
            else
            {
                Debug.Print("更新群组失败");
            }

            // 删除群组
            repository.Delete(group.Id);
            var deletedGroup = repository.GetById(group.Id);
            if (deletedGroup == null)
            {
                Debug.Print("删除群组成功");
            }
            else
            {
                Debug.Print("删除群组失败");
            }
        }

        private static void TestNewsRepository()
        {
            Debug.Print("\n测试新闻仓库...");
            var repository = new NewsRepository();

            // 创建测试新闻
            var news = new News
            {
                Id = Guid.NewGuid().ToString(),
                Title = "测试新闻标题",
                Content = "测试新闻内容",
                Image = "newsimage.jpg",
                GameId = "game1",
                PublishDate = DateTime.Now,
                Author = "测试作者",
                Category = "更新"
            };

            // 添加新闻
            repository.Add(news);
            Debug.Print("添加新闻成功");

            // 获取新闻
            var retrievedNews = repository.GetById(news.Id);
            if (retrievedNews != null)
            {
                Debug.Print($"获取新闻成功: {retrievedNews.Title}");
            }
            else
            {
                Debug.Print("获取新闻失败");
            }

            // 更新新闻
            news.Title = "更新后的测试新闻标题";
            repository.Update(news);
            var updatedNews = repository.GetById(news.Id);
            if (updatedNews != null && updatedNews.Title == "更新后的测试新闻标题")
            {
                Debug.Print("更新新闻成功");
            }
            else
            {
                Debug.Print("更新新闻失败");
            }

            // 删除新闻
            repository.Delete(news.Id);
            var deletedNews = repository.GetById(news.Id);
            if (deletedNews == null)
            {
                Debug.Print("删除新闻成功");
            }
            else
            {
                Debug.Print("删除新闻失败");
            }
        }

        private static void TestDownloadTaskRepository()
        {
            Debug.Print("\n测试下载任务仓库...");
            var repository = new DownloadTaskRepository();

            // 创建测试下载任务
            var task = new DownloadTask
            {
                Id = Guid.NewGuid().ToString(),
                GameId = "game1",
                GameName = "测试游戏",
                TotalSize = 1024 * 1024 * 1024, // 1GB
                DownloadedSize = 0,
                Status = DownloadStatus.Pending,
                Progress = 0,
                Speed = 0,
                StartTime = DateTime.Now
            };

            // 添加下载任务
            repository.Add(task);
            Debug.Print("添加下载任务成功");

            // 获取下载任务
            var retrievedTask = repository.GetById(task.Id);
            if (retrievedTask != null)
            {
                Debug.Print($"获取下载任务成功: {retrievedTask.GameName}");
            }
            else
            {
                Debug.Print("获取下载任务失败");
            }

            // 更新下载任务
            task.Status = DownloadStatus.Downloading;
            task.Progress = 50;
            repository.Update(task);
            var updatedTask = repository.GetById(task.Id);
            if (updatedTask != null && updatedTask.Status == DownloadStatus.Downloading && updatedTask.Progress == 50)
            {
                Debug.Print("更新下载任务成功");
            }
            else
            {
                Debug.Print("更新下载任务失败");
            }

            // 删除下载任务
            repository.Delete(task.Id);
            var deletedTask = repository.GetById(task.Id);
            if (deletedTask == null)
            {
                Debug.Print("删除下载任务成功");
            }
            else
            {
                Debug.Print("删除下载任务失败");
            }
        }
    }
}