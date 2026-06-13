using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Models;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class NewsViewModel : ViewModelBase
    {
        private readonly AsyncRelayCommand _loadNewsCommand;
        private readonly AsyncRelayCommand<string> _loadNewsByCategoryCommand;
        private ObservableCollection<News> _newsList;
        private News _selectedNews;
        private bool _isLoading;
        private int _loadVersion;
        private string _selectedCategory = string.Empty;
        private bool _isAllNewsActive = true;
        private bool _isGameDynamicActive;
        private bool _isActivityAnnouncementActive;

        public bool IsAllNewsActive
        {
            get => _isAllNewsActive;
            set => SetProperty(ref _isAllNewsActive, value);
        }

        public bool IsGameDynamicActive
        {
            get => _isGameDynamicActive;
            set => SetProperty(ref _isGameDynamicActive, value);
        }

        public bool IsActivityAnnouncementActive
        {
            get => _isActivityAnnouncementActive;
            set => SetProperty(ref _isActivityAnnouncementActive, value);
        }

        public ObservableCollection<News> NewsList
        {
            get => _newsList;
            set => SetProperty(ref _newsList, value);
        }

        public News SelectedNews
        {
            get => _selectedNews;
            set => SetProperty(ref _selectedNews, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _loadNewsCommand.RaiseCanExecuteChanged();
                    _loadNewsByCategoryCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand LoadNewsCommand { get; }
        public ICommand LoadNewsByCategoryCommand { get; }

        private readonly NewsService _newsService;

        public NewsViewModel()
        {
            _newsService = new NewsService();
            NewsList = new ObservableCollection<News>();
            _loadNewsCommand = new AsyncRelayCommand(LoadNewsAsync, CanLoadNews);
            _loadNewsByCategoryCommand = new AsyncRelayCommand<string>(LoadNewsByCategoryAsync, CanLoadNews);
            LoadNewsCommand = _loadNewsCommand;
            LoadNewsByCategoryCommand = _loadNewsByCategoryCommand;
        }

        public async Task LoadNewsAsync()
        {
            IsAllNewsActive = true;
            IsGameDynamicActive = false;
            IsActivityAnnouncementActive = false;
            var loadVersion = Interlocked.Increment(ref _loadVersion);
            IsLoading = true;
            try
            {
                var news = await _newsService.GetAllNewsAsync();
                if (loadVersion != Volatile.Read(ref _loadVersion))
                {
                    return;
                }

                ApplyNews(news);
            }
            catch (Exception)
            {
                // 处理异常
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadNewsByGameAsync(string gameId)
        {
            var loadVersion = Interlocked.Increment(ref _loadVersion);
            IsLoading = true;
            try
            {
                var news = await _newsService.GetNewsByGameAsync(gameId);
                if (loadVersion != Volatile.Read(ref _loadVersion))
                {
                    return;
                }

                ApplyNews(news);
            }
            catch (Exception)
            {
                // 处理异常
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadNewsByCategoryAsync(string category)
        {
            IsAllNewsActive = false;
            IsGameDynamicActive = category == "游戏动态";
            IsActivityAnnouncementActive = category == "活动公告";
            var loadVersion = Interlocked.Increment(ref _loadVersion);
            IsLoading = true;
            try
            {
                var news = await _newsService.GetNewsByCategoryAsync(category);
                if (loadVersion != Volatile.Read(ref _loadVersion))
                {
                    return;
                }

                ApplyNews(news);
            }
            catch (Exception)
            {
                // 处理异常
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyNews(IEnumerable<News> items)
        {
            NewsList.Clear();
            foreach (var item in items)
            {
                NewsList.Add(item);
            }

            SelectedNews = NewsList.Count > 0 ? NewsList[0] : null;
        }

        private bool CanLoadNews()
        {
            return !IsLoading;
        }
    }
}