using Discord;
using Discord.Net;
using Discord.WebSocket;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;

#pragma warning disable CS8602

namespace discordnet;

public class Program {
    public static Task Main() => new Program().MainAsync();

    private readonly DiscordSocketClient _client = new();

    private static readonly MongoClient DbClient =
        new(Environment.GetEnvironmentVariable("DCBOT_CONNECTION_STRING", EnvironmentVariableTarget.User));

    private readonly IMongoCollection<BsonDocument> _collection =
        DbClient.GetDatabase("quotebot").GetCollection<BsonDocument>("quotes");

    private async Task<List<BsonDocument>> GetGuildQuotesListAsync(string guildId) =>
        await (await _collection.FindAsync(d => d["guildId"] == guildId)).ToListAsync();

    private async Task MainAsync() {
        _client.Ready += Client_Ready;
        _client.MessageCommandExecuted += MessageCommandHandler;
        _client.Log += Log;
        _client.ButtonExecuted += ButtonHandler;
        _client.ModalSubmitted += ModalHandler;
        _client.SlashCommandExecuted += SlashCommandHandler;

        var token = Environment.GetEnvironmentVariable("DCBOT_TOKEN", EnvironmentVariableTarget.User);

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private static Task Log(LogMessage msg) {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }


    private async Task Client_Ready() {
        var guild = _client.GetGuild(487191946582949908);

        var globalMessageCommand = new MessageCommandBuilder();
        globalMessageCommand.WithName("To Code");

        var guildCommand = new SlashCommandBuilder()
            .WithName("quote")
            .WithDescription("Quote operations")
            .AddOptions(new[] {
                new SlashCommandOptionBuilder()
                    .WithName("add")
                    .WithDescription("Adds a new quote")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("quote", ApplicationCommandOptionType.String, "The quote itself", true)
                    .AddOption("situation", ApplicationCommandOptionType.String, "Who said the quote, in which context",
                        true),
                new SlashCommandOptionBuilder()
                    .WithName("delete")
                    .WithDescription("Deletes a quote")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("id", ApplicationCommandOptionType.String, "The id of the quote", true),
                new SlashCommandOptionBuilder()
                    .WithName("random")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription("Gets you a random quote from the server"),
                new SlashCommandOptionBuilder()
                    .WithName("list")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription("Gets you all quotes from the server"),
                new SlashCommandOptionBuilder()
                    .WithName("search")
                    .WithDescription("Search a quote")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("search-term", ApplicationCommandOptionType.String,
                        "<quote content / situation / author / id / createdAt>", true)
            });

        try {
            await guild.BulkOverwriteApplicationCommandAsync(new ApplicationCommandProperties[] {
                guildCommand.Build()
            });

            await _client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[] {
                globalMessageCommand.Build(),
            });
        }
        catch (HttpException exception) {
            Console.WriteLine(JsonConvert.SerializeObject(exception.Errors, Formatting.Indented));
        }
    }

    private string _message = "";

    private async Task MessageCommandHandler(SocketMessageCommand arg) {
        var mb = new ModalBuilder()
            .WithTitle("Language Highlighting")
            .WithCustomId("lang_modal")
            .AddTextInput("Which language?", "lang", placeholder: "csharp");
        _message = arg.Data.Message.Content;
        await arg.Channel.DeleteMessageAsync(arg.Data.Message.Id);
        await arg.RespondWithModalAsync(mb.Build());
    }

    private async Task SlashCommandHandler(SocketSlashCommand command) {
        switch (command.Data.Name) {
            case "quote":
                await HandleQuoteCommand(command);
                break;
        }
    }

    private async Task HandleQuoteCommand(SocketSlashCommand command) {
        var commandName = command.Data.Options.First().Name;
        switch (commandName) {
            case "add":
                var quoteContent = command.Data.Options.First().Options.First().Value.ToString();
                var situation = command.Data.Options.First().Options.Last().Value.ToString();

                BsonDocument addedQuote;
                await _collection.InsertOneAsync(addedQuote = new BsonDocument {
                    {"quote", quoteContent},
                    {"situation", situation},
                    {"guildId", command.GuildId()},
                    {"createdAt", new BsonDateTime(DateTime.Now)},
                    {"author", command.User.Username}
                });

                await command.RespondAsync(embed: addedQuote.ToQuoteEmbed(Color.Green).Build());
                break;

            case "list":
                var quotes = await GetGuildQuotesListAsync(command.GuildId());
                var buttons = new ComponentBuilder()
                    .WithButton("Previous", "previous")
                    .WithButton("Next", "next");
                await command.RespondAsync(embed: quotes.ToQuoteEmbed(1, Color.Orange).Build(), components: buttons.Build());
                break;

            case "delete":
                var id = command.Data.Options.First().Options.First().Value.ToString();
                var deletedQuote = await _collection.FindOneAndDeleteAsync(d => d["_id"] == new ObjectId(id));
                await command.RespondAsync(embed: deletedQuote.ToQuoteEmbed(Color.Red).Build());
                break;

            case "random":
                var guildQuotes = await GetGuildQuotesListAsync(command.GuildId());
                var randomQuote = guildQuotes.GetRandomElement();
                await command.RespondAsync(embed: randomQuote.ToQuoteEmbed(Color.Purple).Build());
                break;

            case "search":
                var searchTerm = command.Data.Options.First().Options.First().Value.ToString();

                var searchedQuotes =
                    (await GetGuildQuotesListAsync(command.GuildId())).Where(d =>
                        d["quote"].ToString().ToLower().Contains(searchTerm.ToLower()) ||
                        d["situation"].ToString().ToLower().Contains(searchTerm.ToLower()) ||
                        d["createdAt"].ToString().ToLower().Contains(searchTerm.ToLower()) ||
                        d["_id"].ToString().ToLower().Contains(searchTerm.ToLower()) ||
                        d["author"].ToString().ToLower().Contains(searchTerm.ToLower())
                    ).ToList();
                if (searchedQuotes.Count == 1)
                    await command.RespondAsync(embed: searchedQuotes.FirstOrDefault()!.ToQuoteEmbed(Color.Blue).Build());
                else
                    await command.RespondAsync(embed: searchedQuotes.ToMultipleQuotesEmbed().Build());
                break;
        }
    }

    private async Task ButtonHandler(SocketMessageComponent arg) {
        var quoteNr = Convert.ToInt32(arg.Message.Embeds.FirstOrDefault().Footer!.Value.ToString().Split(" ")[1]);
        var quotes = await GetGuildQuotesListAsync(arg.GuildId());
        switch (arg.Data.CustomId) {
            case "previous":
                if (quoteNr == 1) quoteNr = quotes.Count + 1;
                await arg.UpdateAsync(properties =>
                    properties.Embed = quotes.ToQuoteEmbed(Convert.ToInt32(quoteNr) - 1, Color.Orange).Build());
                break;
            case "next":
                if (quoteNr == quotes.Count) quoteNr = 0;
                await arg.UpdateAsync(properties =>
                    properties.Embed = quotes.ToQuoteEmbed(Convert.ToInt32(quoteNr) + 1, Color.Orange).Build());
                break;
        }
    }

    private async Task ModalHandler(SocketModal modal) {
        List<SocketMessageComponentData> components =
            modal.Data.Components.ToList();
        var lang = components
            .First(x => x.CustomId == "lang").Value;

        await modal.RespondAsync($"```{lang}{Environment.NewLine}{_message}```");
    }
}