using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class MusicSourceRegistry
    {
        private static MusicSourceRegistry _instance;
        private static readonly object _lock = new object();
        
        public static MusicSourceRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MusicSourceRegistry();
                        }
                    }
                }
                return _instance;
            }
        }
        
        private readonly List<IMusicSourceProvider> _providers = new List<IMusicSourceProvider>();
        private readonly object _providersLock = new object();
        
        public IReadOnlyList<IMusicSourceProvider> Providers
        {
            get
            {
                lock (_providersLock)
                    return _providers.OrderBy(p => p.Priority).ToList().AsReadOnly();
            }
        }
        
        public void Register(IMusicSourceProvider provider)
        {
            if (provider == null) return;
            lock (_providersLock)
            {
                if (!_providers.Any(p => p.SourceName == provider.SourceName))
                    _providers.Add(provider);
            }
        }
        
        public IMusicSourceProvider GetProvider(string sourceName)
        {
            lock (_providersLock)
                return _providers.FirstOrDefault(p => p.SourceName == sourceName);
        }
        
        public List<IMusicSourceProvider> GetAvailableProviders()
        {
            return Providers.ToList();
        }
        
        public void InitializeDefaultProviders()
        {
            Register(new LocalMusicSourceProvider());
            Register(NeteaseMusicApiService.Instance);
        }
    }
}
