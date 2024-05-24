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

        public Dictionary<ConsoleKeyInfo, IAction> Macros { get; set; } = new();

        public void Save(string filename)
        {
            var serial = ToSerializableConfig();
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
            FromSerializableConfig(result);

            return true;
        }

        private SerializableConfig ToSerializableConfig()
        {
            return new SerializableConfig
            {
                Credentials = Credentials,
                Macros = Macros.Select(m => new SerializableMacro
                {
                    KeyChar = m.Key.KeyChar,
                    Key = m.Key.Key,
                    Modifiers = m.Key.Modifiers,
                    Action = m.Value.ToSerializableAction()
                }).ToList()
            };
        }

        private void FromSerializableConfig(SerializableConfig serial)
        {
            Credentials = serial.Credentials;
            Macros = serial.Macros
                .Select(m =>
                {
                    var shift = (m.Modifiers & ConsoleModifiers.Shift) != 0;
                    var alt = (m.Modifiers & ConsoleModifiers.Alt) != 0;
                    var control = (m.Modifiers & ConsoleModifiers.Control) != 0;
                    var keyInfo = new ConsoleKeyInfo(m.KeyChar, m.Key, shift, alt, control);
                    return (keyInfo, m.Action.ToAction());
                })
                .ToDictionary();
        }
    }

    class SerializableConfig
    {
        public OBSCredentials Credentials { get; set; } = new();

        public List<SerializableMacro> Macros { get; set; } = new();
    }

    class SerializableMacro
    {
        public char KeyChar { get; set; }

        public ConsoleKey Key { get; set; }

        public ConsoleModifiers Modifiers { get; set; }

        public SerializableAction Action { get; set; }
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
                    if (Globals.Config.Macros.TryGetValue(key, out var value))
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
                    Console.WriteLine($"{m.Key.Key} -> {m.Value}");
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
                    return Task.FromResult<IState>(new AssignActionState(key));
                }
            }
        }

        class AssignActionState : IState
        {
            private readonly ConsoleKeyInfo triggerKey;

            public AssignActionState(ConsoleKeyInfo triggerKey)
            {
                this.triggerKey = triggerKey;
            }

            public async Task<IState> Run()
            {
                Console.WriteLine("Select Action:");
                Console.WriteLine("s) Switch Scene");
                Console.WriteLine("t) Toggle Scene Item");

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
                    else if (key.Key == ConsoleKey.T)
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

                        Console.WriteLine("Listing Items...");
                        var items = await Globals.Client.GetSceneItemList(scene.SceneName);
                        foreach (var i in items)
                        {
                            Console.WriteLine($"{i.SceneItemIndex}) {i.SourceName}");
                        }

                        Console.WriteLine("Enter index:");
                        while (!int.TryParse(Console.ReadLine(), out index))
                        {
                            Console.WriteLine("Not a number!");
                        }

                        var item = items.First(i => i.SceneItemIndex == index);

                        Globals.Config.Macros.Add(triggerKey, new ToggleItemAction(scene.SceneName, item.SceneItemId, item.SourceName, item.SceneItemEnabled));

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
                    else if (Globals.Config.Macros.Remove(key))
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
            SwitchScene,
            ToggleItem
        }

        interface IAction
        {
            Task Run(ObsClient client);

            SerializableAction ToSerializableAction();
        }

        class SerializableAction
        {
            public ActionTypes Type { get; set; }

            public Dictionary<string, string> Parameters { get; set; } = new();

            public IAction ToAction()
            {
                switch (Type)
                {
                    case ActionTypes.SwitchScene:
                        {
                            var name = Parameters[nameof(SwitchSceneAction.SceneName)];
                            return new SwitchSceneAction(name);
                        }
                    case ActionTypes.ToggleItem:
                        {
                            var scene = Parameters[nameof(ToggleItemAction.SceneName)];
                            if (!int.TryParse(Parameters[nameof(ToggleItemAction.ItemID)], out var itemID))
                            {
                                throw new ArgumentException("ItemID could not be parsed!");
                            }
                            var itemName = Parameters[nameof(ToggleItemAction.ItemName)];
                            //var enabled = await Globals.Client.GetSceneItemEnabled(scene, itemID);
                            return new ToggleItemAction(scene, itemID, itemName, false);
                        }
                }
                throw new ArgumentOutOfRangeException($"Action type {Type} is unknown!");
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

        class ToggleItemAction : IAction
        {
            public string SceneName { get; private set; }

            public int ItemID { get; private set; }

            public string ItemName { get; private set; }

            private bool Enabled { get; set; }

            public ToggleItemAction(string scene, int itemID, string itemName, bool enabled)
            {
                SceneName = scene;
                ItemID = itemID;
                ItemName = itemName;
                Enabled = enabled;
            }

            public async Task Run(ObsClient client)
            {
                Enabled = !Enabled;
                await client.SetSceneItemEnabled(SceneName, ItemID, Enabled);
            }

            public SerializableAction ToSerializableAction()
            {
                var act = new SerializableAction();
                act.Type = ActionTypes.ToggleItem;
                act.Parameters.Add(nameof(SceneName), SceneName);
                act.Parameters.Add(nameof(ItemID), ItemID.ToString());
                act.Parameters.Add(nameof(ItemName), ItemName);
                return act;
            }

            public override string ToString()
            {
                return $"Toggle {ItemName} in {SceneName}";
            }
        }
    }
}
