using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class MusicStoryListViewModel : ViewModelBase
    {
        private readonly MusicLibraryService _libraryService;
        private readonly MusicStoryService _storyService;
        private List<Song> _storySongs;
        private bool _isLoading;

        public MusicStoryListViewModel()
        {
            _libraryService = MusicLibraryService.Instance;
            _storyService = MusicStoryService.Instance;
            LoadStorySongsCommand = new AsyncRelayCommand(LoadStorySongsAsync);
            _ = LoadStorySongsAsync();
        }

        public List<Song> StorySongs
        {
            get => _storySongs;
            set => SetProperty(ref _storySongs, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasNoStories => StorySongs == null || StorySongs.Count == 0;

        public ICommand LoadStorySongsCommand { get; }

        private async Task LoadStorySongsAsync()
        {
            IsLoading = true;
            var allSongs = _libraryService.GetAllSongs();
            var storySongs = new List<Song>();
            var batchSize = 10;

            for (int i = 0; i < allSongs.Count; i += batchSize)
            {
                var batch = allSongs.Skip(i).Take(batchSize);
                var tasks = batch.Select(async song =>
                {
                    var story = await _storyService.GetStoryAsync(song);
                    return (Song: song, HasStory: story != null && story.Sections.Count > 0);
                });

                var results = await Task.WhenAll(tasks);
                foreach (var result in results)
                {
                    if (result.HasStory)
                    {
                        storySongs.Add(result.Song);
                    }
                }

                if (storySongs.Count >= 50) break;
            }

            StorySongs = storySongs.Take(50).ToList();
            IsLoading = false;
        }
    }
}
