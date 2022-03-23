using Discord.WebSocket;
using MongoDB.Bson;
using Discord;
using Color = Discord.Color;

namespace discordnet;

public static class DiscordExtensions {
    public static string GuildId(this SocketInteraction command) =>
        (command.Channel as SocketGuildChannel)!.Guild.Id.ToString();

    private static string FormatDate(this BsonValue val) {
        var dateTime = val.AsBsonDateTime.ToLocalTime();

        return $"{dateTime.Day}.{dateTime.Month}.{dateTime.Year} - {dateTime.Hour}:{dateTime.Minute}";
    }

    public static EmbedBuilder ToQuoteEmbed(this List<BsonDocument> quotes, int index, Color color = default) {
        var quote = quotes[index - 1];
        var e = new EmbedBuilder {
            Color = color,
            Title = quote["quote"].ToString(),
            Description =
                $"~ {quote["situation"]}{Environment.NewLine}{Environment.NewLine}Added by {quote["author"]}{Environment.NewLine}Created at {quote["createdAt"].FormatDate()}{Environment.NewLine}Id: {quote["_id"]}",
            Footer = new EmbedFooterBuilder {
                Text = $"Quote {index} of {quotes.Count}"
            }
        };
        return e;
    }

    public static EmbedBuilder ToQuoteEmbed(this BsonDocument quote, Color color = default) {
        var e = new EmbedBuilder {
            Color = color,
            Title = quote["quote"].ToString(),
            Description =
                $"- {quote["situation"]}{Environment.NewLine}{Environment.NewLine}Added by {quote["author"]}{Environment.NewLine}Created at {quote["createdAt"].FormatDate()}",
            Footer = new EmbedFooterBuilder {
                Text = $"Id: {quote["_id"]}"
            }
        };
        return e;
    }

    public static EmbedBuilder ToMultipleQuotesEmbed(this IEnumerable<BsonDocument> quotes, Color color = default) {
        var e = new EmbedBuilder {
            Color = color,
            Title = "Search Results"
        };
        foreach (var quote in quotes) {
            e.AddField(quote["quote"].ToString(),
                $"- {quote["situation"]}");
        }

        return e;
    }

    public static T GetRandomElement<T>(this List<T> list) => list[new Random().Next(list.Count)];
}