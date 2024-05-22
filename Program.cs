using OBSCLIMacros;
using OBSCLIMacros.Actions;
using OBSCLIMacros.States;
using OBSStudioClient;
using System.Text.Json;

Console.Write("Loading config...");
if (Globals.Config.Load(Globals.ConfigFileName))
{
    Console.WriteLine("OK");
}
else
{
    Console.WriteLine("Config not found");
    var creds = EnterOBSCredentials();
    Globals.Config.Credentials.Host = creds.Host;
    Globals.Config.Credentials.Port = creds.Port;
    Globals.Config.Credentials.Password = creds.Password;

    Globals.Config.Save(Globals.ConfigFileName);
}

try
{
    var isConnected = await Globals.Client.ConnectAsync(
        true,
        Globals.Config.Credentials.Password,
        Globals.Config.Credentials.Host,
        Globals.Config.Credentials.Port,
        OBSStudioClient.Enums.EventSubscriptions.None);
    await Task.Delay(1);    // currently a bug in the OBSClient library, this statement triggers the scheduler to run a task inside this library

    if (!isConnected)
    {
        return;
    }

    IState state = new MenuState();
    while (!Globals.Stop)
    {
        state = await state.Run();
    }
}
finally
{
    Globals.Client.Disconnect();
    Globals.Client.Dispose();
}

static OBSCredentials EnterOBSCredentials()
{
    Console.WriteLine("Host:");
    var host = Console.ReadLine();
    Console.WriteLine("Port:");
    int port;
    while (!int.TryParse(Console.ReadLine(), out port))
    {
        Console.WriteLine("Not a valid number. Try again:");
    }
    Console.WriteLine("Password:");
    var password = Console.ReadLine();
    return new OBSCredentials
    {
        Host = host!,
        Port = port,
        Password = password!
    };
}

namespace OBSCLIMacros
{
    class Config
    {
        public OBSCredentials Credentials { get; set; } = new();

        public Dictionary<KeyInfo, IAction> Macros { get; set; } = new();

        public void Save(string filename)
        {
            var serial = new SerializableConfig
            {
                Credentials = Credentials,
                Macros = Macros.Select(m => (m.Key.ToInt(), m.Value.ToSerializableAction())).ToDictionary()
            };

            var json = JsonSerializer.Serialize(serial);
            using var sw = new StreamWriter(filename);
            sw.Write(json);
        }

        public bool Load(string filename)
        {
            if (!File.Exists(filename))
            {
                return false;
            }
            using var sr = new StreamReader(filename);
            var json = sr.ReadToEnd();
            var result = JsonSerializer.Deserialize<SerializableConfig>(json);
            if (result == null)
            {
                return false;
            }

            Credentials = result.Credentials;
            Macros = result.Macros
                .Select(m => (KeyInfo.FromInt(m.Key), m.Value.ToAction()))
                .Where(m => m.Item2 != null)
                .Select(m => (m.Item1, m.Item2!))
                .ToDictionary();

            return true;
        }
    }

    class KeyInfo
    {
        public ConsoleKey Key { get; private set; }

        public ConsoleModifiers Modifiers { get; private set; }

        public KeyInfo(ConsoleKey key, ConsoleModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public KeyInfo(ConsoleKeyInfo keyInfo)
        {
            Key = keyInfo.Key;
            Modifiers = keyInfo.Modifiers;
        }

        public int ToInt()
        {
            int value = 0;
            value |= (byte)Key;
            value |= (byte)Modifiers << 8;
            return value;
        }

        public static KeyInfo FromInt(int value)
        {
            var key = (ConsoleKey)(value & 0xFF);
            var mods = (ConsoleModifiers)(value >> 8);
            return new KeyInfo(key, mods);
        }

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (obj is not KeyInfo) return false;

            var other = (obj as KeyInfo)!;
            return other.Key.Equals(Key) && other.Modifiers.Equals(Modifiers);
        }

        public override string ToString()
        {
            return $"{Key}";
        }

        public static bool operator ==(KeyInfo? l, KeyInfo? r)
        {
            if (ReferenceEquals(l, r)) return true;
            if (l is null || r is null) return false;
            return l.Equals(r);
        }

        public static bool operator !=(KeyInfo? l, KeyInfo? r)
        {
            return !(l == r);
        }

        public override int GetHashCode()
        {
            return (int)Key.GetHashCode() + (int)Modifiers.GetHashCode();
        }
    }

    class SerializableConfig
    {
        public OBSCredentials Credentials { get; set; } = new();

        public Dictionary<int, SerializableAction> Macros { get; set; } = new();
    }

    public class OBSCredentials
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 4455;
        public string Password { get; set; } = string.Empty;
    }

    static class Globals
    {
        public const string ConfigFileName = "config.json";

        public static Config Config { get; private set; } = new();

        public static bool Stop { get; set; } = false;

        public static ObsClient Client { get; } = new();
    }

    namespace States
    {
        interface IState
        {
            Task<IState> Run();
        }

        class ExitState : IState
        {
            public Task<IState> Run()
            {
                Globals.Stop = true;
                return Task.FromResult<IState>(this);
            }
        }

        class MenuState : IState
        {
            public Task<IState> Run()
            {
                Console.WriteLine("m) Macro Mode");
                Console.WriteLine("e) Edit Mode");
                Console.WriteLine("s) Save");

                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.M)
                    {
                        return Task.FromResult<IState>(new MacroState());
                    }
                    else if (key.Key == ConsoleKey.E)
                    {
                        return Task.FromResult<IState>(new EditState());
                    }
                    else if (key.Key == ConsoleKey.S)
                    {
                        Globals.Config.Save(Globals.ConfigFileName);
                        return Task.FromResult<IState>(this);
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        return Task.FromResult<IState>(new ExitState());
                    }
                }
            }
        }

        class MacroState : IState
        {
            public Task<IState> Run()
            {
                Console.WriteLine("Macro State Active");

                while (true)
                {
                    var key = Console.ReadKey(true);
                    var keyInfo = new KeyInfo(key);
                    if (Globals.Config.Macros.TryGetValue(keyInfo, out var value))
                    {
                        value.Run(Globals.Client);
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        return Task.FromResult<IState>(new MenuState());
                    }
                }
            }
        }

        class EditState : IState
        {
            public Task<IState> Run()
            {
                Console.WriteLine("Exisiting Macros");
                foreach (var m in Globals.Config.Macros)
                {
                    Console.WriteLine($"{m.Key} -> {m.Value}");
                }

                Console.WriteLine();
                Console.WriteLine("n) New Macro");
                Console.WriteLine("r) Remove Macro");

                while (true)
                {
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.N)
                    {
                        return Task.FromResult<IState>(new RecordKeyState());
                    }
                    else if (key.Key == ConsoleKey.R)
                    {
                        return Task.FromResult<IState>(new RemoveKeyState());
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        return Task.FromResult<IState>(new MenuState());
                    }
                }
            }
        }

        class RecordKeyState : IState
        {
            public Task<IState> Run()
            {
                Console.WriteLine("Press key to assign action:");

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    return Task.FromResult<IState>(new EditState());
                }
                else
                {
                    return Task.FromResult<IState>(new AssignActionState(new KeyInfo(key)));
                }
            }
        }

        class AssignActionState : IState
        {
            private readonly KeyInfo triggerKey;

            public AssignActionState(KeyInfo triggerKey)
            {
                this.triggerKey = triggerKey;
            }

            public async Task<IState> Run()
            {
                Console.WriteLine("Select Action:");
                Console.WriteLine("s) Switch Scene");

                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        return new RecordKeyState();
                    }
                    else if (key.Key == ConsoleKey.S)
                    {
                        Console.WriteLine("Listing Scenes...");
                        var scenes = await Globals.Client.GetSceneList();
                        foreach (var s in scenes.Scenes)
                        {
                            Console.WriteLine($"{s.SceneIndex}) {s.SceneName}");
                        }

                        Console.WriteLine("Enter index:");
                        int index;
                        while (!int.TryParse(Console.ReadLine(), out index))
                        {
                            Console.WriteLine("Not a number!");
                        }

                        var scene = scenes.Scenes.First(s => s.SceneIndex == index);

                        Globals.Config.Macros.Add(triggerKey, new SwitchSceneAction(scene.SceneName));

                        return new EditState();
                    }
                }
            }
        }

        class RemoveKeyState : IState
        {
            public Task<IState> Run()
            {
                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        return Task.FromResult<IState>(new EditState());
                    }
                    else if (Globals.Config.Macros.Remove(new KeyInfo(key)))
                    {
                        Console.WriteLine($"Removed {key}");
                    }
                }
            }
        }
    }

    namespace Actions
    {
        enum ActionTypes
        {
            SwitchScene = 1
        }

        interface IAction
        {
            Task Run(ObsClient client);

            SerializableAction ToSerializableAction();
        }

        class SerializableAction
        {
            public ActionTypes Type { get; set; }

            public Dictionary<string, object> Parameters { get; set; } = new();

            public IAction? ToAction()
            {
                switch (Type)
                {
                    case ActionTypes.SwitchScene:
                        {
                            var name = (JsonElement)Parameters[nameof(SwitchSceneAction.SceneName)];
                            return new SwitchSceneAction(name.GetString()!);
                        }
                }
                return null;
            }

            public static SerializableAction FromAction(IAction action)
            {
                return action.ToSerializableAction();
            }
        }

        class SwitchSceneAction : IAction
        {
            public string SceneName { get; private set; }

            public SwitchSceneAction(string sceneName)
            {
                SceneName = sceneName;
            }

            public async Task Run(ObsClient client)
            {
                await client.SetCurrentProgramScene(SceneName);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.SwitchScene;
                act.Parameters.Add(nameof(SceneName), SceneName);
                return act;
            }

            public override string ToString()
            {
                return $"Switch to Scene: {SceneName}";
            }
        }
    }
}
