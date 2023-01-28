// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.EventArgs;
using DSharpPlus.VoiceNext;
using DSharpPlus.VoiceNext.EventArgs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

while (true)
{
    new DiscordBot().Main().GetAwaiter().GetResult();
}


class DiscordBot
{
    private static string _token;
    private DiscordClient Client { get; set; }

    public async Task Main()
    {
        try
        {
            _token = Environment.GetEnvironmentVariable("TOKEN");
            Client = new DiscordClient(new DiscordConfiguration
            {
                Token = _token,
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.All,
                AutoReconnect = true,
            });
            Client.Ready += Ready;

            var cnext = Client.UseSlashCommands(new());
            cnext.RegisterCommands<Commands>();
            cnext.SlashCommandExecuted += CommandExecuted;
            cnext.SlashCommandErrored += CommandErrored;
            await Client.ConnectAsync();

            await Task.Delay(-1);

            Task CommandExecuted(SlashCommandsExtension slashCommandsExtension, SlashCommandExecutedEventArgs slashCommandExecutedEventArgs)
            {
                var userUsername = slashCommandExecutedEventArgs.Context.User.Username;
                var contextCommandName = slashCommandExecutedEventArgs.Context.CommandName;
                Log.Info($"{userUsername} Call {contextCommandName}");
                return Task.CompletedTask;
            }

            Task CommandErrored(SlashCommandsExtension slashCommandsExtension, SlashCommandErrorEventArgs slashCommandErrorEventArgs)
            {
                Log.Error(slashCommandErrorEventArgs.Exception.ToString());
                return Task.CompletedTask;
            }

        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
    }

    private async Task Ready(DiscordClient sender, ReadyEventArgs e)
    {
        try
        {
            await HistoryNameManager.Instance.Init();
            await BlackManager.Instance.Init();
            sender.VoiceStateUpdated += BlackManager.Instance.VoiceStateUpdated;
            sender.MessageCreated += BlackManager.Instance.MessageCreated;
            sender.GuildMemberUpdated += HistoryNameManager.Instance.GuildMemberUpdated;
            sender.GuildMemberUpdated += BlackManager.Instance.GuildMemberUpdated;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }

        try
        {
            await BlackManager.Instance.DoAllBlock(sender);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }

    }
}

public class HistoryNameManager
{
    [System.Serializable]
    public class DataBase
    {
        public List<UserName> UserName = new();
    }

    [System.Serializable]
    public class UserName
    {
        public ulong Id;
        public string CurrentName;
        public HashSet<string> HistoryName = new();

        public UserName(ulong id, string currentName)
        {
            Id = id;
            CurrentName = currentName;
        }

        public UserName ChangeNickName(string nickName)
        {
            CurrentName = nickName;
            HistoryName.Add(nickName);
            return this;
        }
    }

    private readonly string _historyNickName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save", "historyNickName");


    private static HistoryNameManager instance = null;
    public static HistoryNameManager Instance = instance ??= new HistoryNameManager();

    [JsonExtensionData]
    private Dictionary<ulong, DataBase> List = new();

    public async Task Init()
    {
        var userNames = await Load();
        List = userNames;
    }

    private async Task Save()
    {
        foreach (var item in List)
        {

            var serializeObject = JsonConvert.SerializeObject(item.Value, Formatting.Indented);

            if (!Directory.Exists(_historyNickName))
            {
                Directory.CreateDirectory(_historyNickName);
            }

            var path = Path.Combine(_historyNickName, $"{item.Key}.json");
            await File.WriteAllTextAsync(path, serializeObject);
        }

        await Task.CompletedTask;
    }


    private async Task<Dictionary<ulong, DataBase>> Load()
    {
        var dictionary = new Dictionary<ulong, DataBase>();

        if (!Directory.Exists(_historyNickName))
        {
            Directory.CreateDirectory(_historyNickName);
        }

        foreach (var file in new DirectoryInfo(_historyNickName).GetFiles("*.json").Select(x => new {x.FullName, x.Name}))
        {
            Log.Stream($"Load {file.FullName}");
            if (ulong.TryParse(file.Name.Split('.').FirstOrDefault(), out var id))
            {
                var readAllText = await File.ReadAllTextAsync(file.FullName);
                var deserializeObject = JsonConvert.DeserializeObject<DataBase>(readAllText) ?? new();
                dictionary.TryAdd(id, deserializeObject);
            }
        }

        return dictionary;
    }


    public async Task<HashSet<string>> GetHistoryNickName(DiscordGuild guild, DiscordUser user)
    {
        var member = await guild.GetMemberAsync(user.Id);
        if (List.TryGetValue(guild.Id, out var value))
        {
            var firstOrDefault = value.UserName.FirstOrDefault(x => x.Id == user.Id);
            if (firstOrDefault != null)
            {
                return firstOrDefault.HistoryName;
            }
        }

        return new HashSet<string>() {member.Nickname};
    }

    public async Task GuildMemberUpdated(DiscordClient sender, GuildMemberUpdateEventArgs e)
    {
        if (List.TryGetValue(e.Guild.Id, out var value))
        {
            var firstOrDefault = value.UserName.FirstOrDefault(x => x.Id == e.Member.Id);
            if (firstOrDefault != null)
            {
                firstOrDefault.ChangeNickName(e.NicknameAfter);
            }
            else
            {
                var userName = new UserName(e.Member.Id, e.NicknameBefore);
                userName.ChangeNickName(e.NicknameAfter);
                value.UserName.Add(userName);
            }
        }
        else
        {
            var memberId = e.Member.Id;
            var dataBase = new DataBase
            {
                UserName = new List<UserName>
                    {new UserName(e.Member.Id, e.NicknameBefore).ChangeNickName(e.NicknameAfter)}
            };
            List.TryAdd(e.Guild.Id, dataBase);
        }

        await Save();
    }
}

class BlackManager
{
    [System.Serializable]
    public class UserData
    {
        public string NickName;
        public ulong Userid;
        public long Sec;
    }

    public class DataBase
    {
        public ulong LogChannelId;
        public List<UserData> UserData = new();

        public DataBase()
        {

        }

        public DataBase(List<UserData> userData)
        {
            UserData = userData;
        }
    }

    private readonly string _banSaveFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save", "ban");

    private readonly Dictionary<ulong, Timer> _timers = new();
    private static BlackManager instance = null;
    public static BlackManager Instance = instance ??= new BlackManager();
    public Dictionary<ulong, DataBase> List { get; private set; } = new();

    private BlackManager()
    {
    }

    public async Task Init()
    {
        List = await Load();
        await Task.CompletedTask;
    }

    private async Task Save()
    {
        foreach (var item in List)
        {

            var serializeObject = JsonConvert.SerializeObject(item.Value, Formatting.Indented);

            if (!Directory.Exists(_banSaveFolder))
            {
                Directory.CreateDirectory(_banSaveFolder);
            }

            var path = Path.Combine(_banSaveFolder, $"{item.Key}.json");
            await File.WriteAllTextAsync(path, serializeObject);
        }

        await Task.CompletedTask;
    }


    private async Task<Dictionary<ulong, DataBase>> Load()
    {
        var dictionary = new Dictionary<ulong, DataBase>();

        if (!Directory.Exists(_banSaveFolder))
        {
            Directory.CreateDirectory(_banSaveFolder);
        }

        foreach (var file in new DirectoryInfo(_banSaveFolder).GetFiles("*.json").Select(x => new {x.FullName, x.Name}))
        {
            Log.Stream($"Load {file.FullName}");
            if (ulong.TryParse(file.Name.Split('.').FirstOrDefault(), out var id))
            {
                var readAllText = await File.ReadAllTextAsync(file.FullName);
                var deserializeObject = JsonConvert.DeserializeObject<DataBase>(readAllText) ?? new();
                dictionary.TryAdd(id, deserializeObject);
            }
        }

        return dictionary;
    }

    public async Task Add(InteractionContext ctx, DiscordUser user, long sec)
    {
        var userData = new UserData() {NickName = user.Username, Userid = user.Id, Sec = sec};

        if (List.TryGetValue(ctx.Guild.Id, out var value))
        {
            var firstOrDefault = value.UserData.FirstOrDefault(x => x.Userid == user.Id);
            if (firstOrDefault is not null)
            {
                firstOrDefault.Sec = sec;
            }
            else
            {
                value.UserData.Add(userData);
            }
        }
        else
        {
            var temp = new List<UserData>
            {
                userData
            };
            List.Add(ctx.Guild.Id, new DataBase(temp));
        }

        await DoBlock(ctx, user);
        await Save();

    }

    public async Task Remove(InteractionContext ctx, DiscordUser user)
    {
        if (List.TryGetValue(ctx.Guild.Id, out var value))
        {
            var firstOrDefault = value.UserData.FirstOrDefault(x => x.Userid == user.Id);
            value.UserData.Remove(firstOrDefault!);
        }

        await Save();
        await Task.CompletedTask;
    }

    private async Task DoBlock(InteractionContext ctx, DiscordUser user)
    {
        try
        {
            var discordMember = await ctx.Guild.GetMemberAsync(user.Id);
            await discordMember.PlaceInAsync(ctx.Guild.AfkChannel);
        }
        catch (Exception e)
        {
            Log.Warn(e.Message);
        }
    }

    public async Task DoAllBlock(DiscordClient client)
    {
        try
        {
            foreach (var item in List)
            {
                var guild = await client.GetGuildAsync(item.Key);

                if (guild != null)
                {
                    foreach (var blackUser in item.Value.UserData)
                    {
                        var discordMember = await guild.GetMemberAsync(blackUser.Userid);
                        await discordMember.ModifyAsync(x => x.VoiceChannel = guild.AfkChannel);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private bool TryFindBlackList(DiscordGuild guild, DiscordUser user, out UserData? userData)
    {
        userData = null;
        if (List.TryGetValue(guild.Id, out var value))
        {

            return (userData = value.UserData.FirstOrDefault(x => x.Userid == user.Id)) != null;
        }

        return false;
    }

    public async Task VoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs e)
    {
        await Task.Delay(100);

        if (e.After?.Channel == default)
        {
            Log.Stream($"{e.User.Username} Exit Channel");
            await RemoveFromTime(e);
            return;
        }

        if (TryFindBlackList(e.Guild, e.User, out var load))
        {
            if (e.After.Channel.Id == e.Guild.AfkChannel.Id)
            {
                Log.Stream($"{e.User.Username} Enter To AFK Channel");
                if (await RemoveFromTime(e))
                {
                    return;
                }
            }

            Log.Stream($"{e.User.Username} Enter Channel");
            await RemoveFromTime(e);

            var timer = new Timer(async (_) => await ModifyAsync(), null, TimeSpan.FromSeconds(load.Sec), TimeSpan.FromSeconds(load.Sec));
            _timers.Add(e.User.Id, timer);

            async Task ModifyAsync()
            {
                try
                {
                    var discordMember = await e.After.Guild.GetMemberAsync(e.After.Member.Id);
                    if (discordMember?.VoiceState?.Channel == e.Guild.AfkChannel)
                    {
                        return;
                    }

                    Log.Stream($"Move {e.User.Username} to AFK");
                    await e.After.Member.ModifyAsync(x => x.VoiceChannel = e.Guild.AfkChannel);
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }

            }
        }
    }

    private async Task<bool> RemoveFromTime(VoiceStateUpdateEventArgs e)
    {

        if (_timers.TryGetValue(e.User.Id, out var timer0))
        {
            await timer0.DisposeAsync();
            _timers.Remove(e.User.Id);
            return true;
        }

        return false;
    }

    public async Task MessageCreated(DiscordClient sender, MessageCreateEventArgs e)
    {
        try
        {
            if (TryFindBlackList(e.Guild, e.Author, out var value))
            {
                Log.Stream($"{e.Author.Username} say: {e.Message.Content}");
                await e.Channel.DeleteMessageAsync(e.Message);
                Log.Stream($"Remove {e.Author.Username} say: {e.Message.Content}");
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private async Task<DiscordChannel?> GetLogChannel(DiscordGuild guild)
    {
        if (List.TryGetValue(guild.Id, out var value) && value.LogChannelId != default)
        {
            var readOnlyList = await guild.GetChannelsAsync();
            return readOnlyList.FirstOrDefault(x => x.Id == value.LogChannelId);
        }

        return null;
    }

    public async Task SetLogChannel(InteractionContext ctx, DiscordChannel channel)
    {
        if (List.TryGetValue(ctx.Guild.Id, out var value))
        {
            value.LogChannelId = channel.Id;
        }

        await Save();
    }

    public async Task GuildMemberUpdated(DiscordClient sender, GuildMemberUpdateEventArgs e)
    {
        if (e.NicknameBefore != e.NicknameAfter)
        {
            var discordChannel = await GetLogChannel(e.Guild);
            if (discordChannel != null)
            {
                if (TryFindBlackList(e.Guild, e.Member, out var _))
                {
                    await sender.SendMessageAsync(discordChannel, $"黑名單中的成員偷偷改名\n{e.NicknameBefore} => {e.NicknameAfter}");
                }
            }
        }
    }
}

public class Commands : ApplicationCommandModule
{
    private Dictionary<ulong, Timer> _timers = new();

    [SlashCommand("black", "black")]
    public async Task Black(InteractionContext ctx, [Option("DiscordUser", "DiscordUser")] DiscordUser user, [Option("long", "sec")] long sec)
    {
        Log.Stream($"block {user.Username} {sec} sec");
        await BlackManager.Instance.Add(ctx, user, sec);
        await ctx.CreateResponseAsync($"Black {user.Username}").ConfigureAwait(false);
    }

    [SlashCommand("ls", "ls")]
    public async Task Ls(InteractionContext ctx)
    {
        var stringBuilder = new StringBuilder();
       
        if (BlackManager.Instance.List.TryGetValue(ctx.Guild.Id, out var value))
        {
            if (!value.UserData.Any())
            {
                stringBuilder.Append("black list is empty");
            }

            foreach (var item in value.UserData)
            {
                stringBuilder.Append($"ID: {item.Userid} \nNickName: {item.NickName} \nSec: {item.Sec} \n-------------\n");
            }
        }
        else
        {
            stringBuilder.Append("black list is empty");
        }
        Log.Stream(stringBuilder.ToString());
        await ctx.CreateResponseAsync(stringBuilder.ToString()).ConfigureAwait(false);
    }

    [SlashCommand("unblack", "unblack")]
    public async Task UnBlack(InteractionContext ctx, [Option(nameof(DiscordUser), "DiscordUser")] DiscordUser user)
    {
        Log.Stream($"Unblock {user.Username}");
        await BlackManager.Instance.Remove(ctx, user);
        await ctx.CreateResponseAsync($"UnBlack {user.Username}").ConfigureAwait(false);
    }

    [SlashCommand("setlogchannel", "setlogchannel")]
    public async Task SetLogChannel(InteractionContext ctx, [Option(nameof(DiscordChannel), "DiscordChannel")] DiscordChannel channel)
    {
        await BlackManager.Instance.SetLogChannel(ctx, channel);
        await ctx.CreateResponseAsync($"SetLogChannel:{channel.Name}").ConfigureAwait(false);
    }

    [SlashCommand("username", "username")]
    public async Task GetUserHistoryName(InteractionContext ctx, [Option(nameof(DiscordUser), "DiscordUser")] DiscordUser user)
    {
        var historyNickName = await HistoryNameManager.Instance.GetHistoryNickName(ctx.Guild, user);

        await ctx.CreateResponseAsync($"GetUserHistoryName:\n{string.Join('\n', historyNickName).Trim()}").ConfigureAwait(false);
    }
}

internal static class Log
{
    public static void Stream(string text, bool newLine = true)
    {
        FormatColor(text, ConsoleColor.Green, newLine);
    }

    public static void Info(string text, bool newLine = true)
    {
        FormatColor(text, ConsoleColor.Gray, newLine);
    }

    public static void Warn(string text, bool newLine = true)
    {
        FormatColor(text, ConsoleColor.DarkYellow, newLine);
    }

    public static void Error(string text, bool newLine = true)
    {
        FormatColor(text, ConsoleColor.Red, newLine);
    }

    public static void FormatColor(string text, ConsoleColor consoleColor = ConsoleColor.Gray, bool newLine = true)
    {
        text = $"[{DateTime.Now}] {text}";
        Console.ForegroundColor = consoleColor;
        if (newLine) Console.WriteLine(text);
        else Console.Write(text);
        Console.ForegroundColor = ConsoleColor.Gray;
    }
}