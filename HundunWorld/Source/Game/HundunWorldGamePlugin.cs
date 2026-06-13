using FlaxEngine;

namespace HundunWorld.Game
{
    public class HundunWorldGamePlugin : Plugin
    {
        private static HundunWorldGamePlugin _instance;
        public static HundunWorldGamePlugin Instance => _instance;

        public HundunWorldGamePlugin()
        {
            _instance = this;
        }

        public override void Initialize()
        {
            Debug.Log("HundunWorldGamePlugin.Initialize() by engine");
            try
            {
                var gameType = System.Type.GetType(
                    "HundunWorld.Game.HundunWorldGame, Game.CSharp", false);
                if (gameType != null)
                {
                    var gameInstance = System.Activator.CreateInstance(gameType);
                    var startMethod = gameType.GetMethod("StartAsync",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance);
                    if (startMethod != null)
                    {
                        var task = startMethod.Invoke(gameInstance, null);
                        if (task is System.Threading.Tasks.Task t)
                        {
                            _ = t.ContinueWith(x =>
                            {
                                if (x.IsFaulted)
                                    Debug.LogError($"Game start failed: {x.Exception?.GetBaseException().Message}");
                            });
                        }
                    }
                }
                else
                {
                    Debug.LogError("HundunWorldGame type not found");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Plugin init failed: {ex.Message}");
            }
        }

        public override void Deinitialize()
        {
            Debug.Log("HundunWorldGamePlugin.Deinitialize()");
            _instance = null;
        }
    }
}
