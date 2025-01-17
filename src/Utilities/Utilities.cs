﻿
namespace UB3RB0T
{
    using Discord;
    using Discord.Net;
    using Discord.WebSocket;
    using Flurl.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Newtonsoft.Json;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    public static class Utilities
    {
        const int ONEHOUR = 60 * 60;
        const int ONEDAY = ONEHOUR * 24;
        const int ONEWEEK = ONEDAY * 7;
        const int ONEYEAR = ONEDAY * 365;
        const long TOOLONG = 315360000;
                             

        private static HashSet<ulong> blockedDMUsers = new HashSet<ulong>();
        private static Random timerRandom = new Random();

        public static void Forget(this Task task) { }

        public static Uri AppendQueryParam(this Uri uri, string key, string value)
        {
            var newQueryParams = new Dictionary<string, string> { { key, value } };
            return new Uri(QueryHelpers.AddQueryString(uri.ToString(), newQueryParams)); 
        }

        public static Task<HttpResponseMessage> PostJsonAsync(this Uri uri, object data)
        {
            return uri.ToString().WithTimeout(10).PostJsonAsync(data);
        }

        public static Task<Stream> GetStreamAsync(this Uri uri)
        {
            return uri.ToString().WithTimeout(10).GetStreamAsync();
        }

        public static long ToUnixMilliseconds(DateTimeOffset dto)
        {
            return (dto.UtcDateTime.Ticks / TimeSpan.TicksPerMillisecond) - 62135596800;
        }

        public static string SubstringUpTo(this string value, int maxLength)
        {
            return value.Substring(0, Math.Min(value.Length, maxLength));
        }

        public static bool IContains(this string haystack, string needle)
        {
            return haystack.ToLowerInvariant().Contains(needle.ToLowerInvariant());
        }

        public static bool IEquals(this string first, string second)
        {
            return first.ToLowerInvariant().Equals(second.ToLowerInvariant());
        }

        public static string ReplaceMulti(this string s, string[] oldValues, string newValue)
        {
            var sb = new StringBuilder(s);
            foreach (var oldValue in oldValues)
            {
                sb.Replace(oldValue, newValue);
            }

            return sb.ToString();
        }

        public static EmbedBuilder CreateEmbedBuilder(this EmbedData embedData)
        {
            var embedBuilder = new EmbedBuilder
            {
                Title = embedData.Title?.SubstringUpTo(256),
                ThumbnailUrl = embedData.ThumbnailUrl,
                Description = embedData.Description,
                Url = Uri.IsWellFormedUriString(embedData.Url, UriKind.Absolute) ? embedData.Url : null,
            };

            if (!string.IsNullOrEmpty(embedData.Author))
            {
                embedBuilder.Author = new EmbedAuthorBuilder
                {
                    Name = embedData.Author,
                    Url = embedData.AuthorUrl,
                    IconUrl = embedData.AuthorIconUrl,
                };
            }

            if (!string.IsNullOrEmpty(embedData.Color))
            {
                var red = Convert.ToInt32(embedData.Color.Substring(0, 2), 16);
                var green = Convert.ToInt32(embedData.Color.Substring(2, 2), 16);
                var blue = Convert.ToInt32(embedData.Color.Substring(4, 2), 16);

                embedBuilder.Color = new Color(red / 255.0f, green / 255.0f, blue / 255.0f);
            }

            if (!string.IsNullOrEmpty(embedData.Footer))
            {
                embedBuilder.Footer = new EmbedFooterBuilder
                {
                    Text = embedData.Footer,
                    IconUrl = embedData.FooterIconUrl,
                };
            }

            if (embedData.EmbedFields != null)
            {
                foreach (var embedField in embedData.EmbedFields)
                {
                    if (!string.IsNullOrEmpty(embedField.Name) && !string.IsNullOrEmpty(embedField.Value))
                    {
                        embedBuilder.AddField((field) =>
                        {
                            field.IsInline = embedField.IsInline;
                            field.Name = embedField.Name;
                            field.Value = embedField.Value;
                        });
                    }
                }
            }

            return embedBuilder;
        }

        public static bool HasMentionPrefix(this string text, ulong botUserId, ref int argPos)
        {
            if (text.Length <= 3 || text[0] != '<' || text[1] != '@')
            {
                return false;
            }

            int endPos = text.IndexOf('>');
            if (endPos == -1)
            {
                return false;
            }

            // Must end in "> "
            if (text.Length < endPos + 2 || text[endPos + 1] != ' ')
            {
                return false; 
            }

            if (!MentionUtils.TryParseUser(text.Substring(0, endPos + 1), out ulong userId))
            {
                return false;
            }

            if (userId == botUserId)
            {
                argPos = endPos + 2;
                return true;
            }

            return false;
        }

        public static DateTime GetCreatedDate(this SocketUser user)
        {
            var timeStamp = ((user.Id >> 22) + 1420070400000) / 1000;
            var createdDate = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return createdDate.AddSeconds(timeStamp);
        }

        public static string Random(this string[] array)
        {
            var randomNumber = new Random();
            return array[randomNumber.Next(array.Length)];
        }

        public static async Task<T> GetApiResponseAsync<T>(Uri uri)
        {
            try
            {
                var content = await uri.ToString().WithTimeout(TimeSpan.FromSeconds(10)).GetStringAsync();
                return JsonConvert.DeserializeObject<T>(content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to parse {{Endpoint}}", uri);
                return default(T);
            }
        }

        public static bool TryParseAbsoluteReminder(Match timerMatch, BotMessageData messageData, out string query)
        {
            query = string.Empty;

            string toMatch = timerMatch.Groups["target"].ToString().Trim();
            string to = toMatch == "me" ? messageData.UserName : toMatch;
            string req = to == messageData.UserName ? string.Empty : messageData.UserName;
            string durationStr = string.Empty;
            long duration = 0;

            GroupCollection matchGroups = timerMatch.Groups;
            string reason = matchGroups["reason"].ToString();

            var dateTimeString = matchGroups["time"].ToString();
            if (matchGroups["date"].Success)
            {
                dateTimeString = matchGroups["date"] + " " + dateTimeString;
            }

            if (DateTime.TryParse(dateTimeString, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AdjustToUniversal, out DateTime dt))
            {
                duration = (long)dt.Subtract(DateTime.UtcNow).TotalSeconds;
                durationStr = $"{duration}s";
            }

            if (duration < 10 || duration > TOOLONG)
            {
                return false;
            }

            query = $"timer for:\"{to}\" {durationStr} {reason}";

            return true;
        }

        public static bool TryParseReminder(Match timerMatch, BotMessageData messageData, out string query)
        {
            query = string.Empty;

            string toMatch = timerMatch.Groups["target"].ToString().Trim();
            string to = toMatch == "me" ? messageData.UserName : toMatch;
            string req = to == messageData.UserName ? string.Empty : messageData.UserName;
            string durationStr = string.Empty;
            long duration = 0;

            GroupCollection matchGroups = timerMatch.Groups;
            string reason = matchGroups["reason"].ToString();

            if (matchGroups["rand"].Success)
            {
                var randValue = timerRandom.Next(20, 360);
                duration += randValue * 60;
                durationStr = $"{randValue}m";
            }

            if (matchGroups["years"].Success)
            {
                string yearString = timerMatch.Groups["years"].ToString();
                if (int.TryParse(yearString.Remove(yearString.Length - 5, 5), out int yearValue))
                {
                    duration += yearValue * ONEYEAR;
                    durationStr = $"{yearValue}y";
                }
            }

            if (matchGroups["weeks"].Success)
            {
                string weekString = matchGroups["weeks"].ToString();
                if (int.TryParse(weekString.Remove(weekString.Length - 5, 5), out int weekValue))
                {
                    duration += weekValue * ONEWEEK;
                    durationStr += $"{weekValue}w";
                }
            }

            if (matchGroups["days"].Success)
            {
                string dayString = matchGroups["days"].ToString();
                if (int.TryParse(dayString.Remove(dayString.Length - 4, 4), out int dayValue))
                {
                    duration += dayValue * ONEDAY;
                    durationStr += $"{dayValue}d";
                }
            }

            if (matchGroups["hours"].Success)
            {
                string hourString = matchGroups["hours"].ToString();
                if (int.TryParse(hourString.Remove(hourString.Length - 5, 5), out int hourValue))
                {
                    duration += hourValue * ONEHOUR;
                    durationStr += $"{hourValue}h";
                }
            }

            if (matchGroups["minutes"].Success)
            {
                string minuteString = matchGroups["minutes"].ToString();
                if (int.TryParse(minuteString.Remove(minuteString.Length - 7, 7), out int minuteValue))
                {
                    duration += minuteValue * 60;
                    durationStr += $"{minuteValue}m";
                }
            }

            if (matchGroups["seconds"].Success)
            {
                string secongString = matchGroups["seconds"].ToString();
                if (int.TryParse(secongString.Remove(secongString.Length - 8, 8), out int secondValue))
                {
                    duration += secondValue;
                    durationStr += $"{secondValue}s";
                }
            }

            if (duration < 10 || duration > TOOLONG)
            {
                return false;
            }

            query = $"timer for:\"{to}\" {durationStr} {reason}";

            return true;
        }

        public static ChannelPermissions GetCurrentUserPermissions(this ITextChannel channel)
        {
            return (channel as SocketGuildChannel)?.Guild?.CurrentUser?.GetPermissions(channel) ?? new ChannelPermissions();
        }

        public static async Task<IUserMessage> SendOwnerDMAsync(this IGuild guild, string message)
        {
            if (blockedDMUsers.Contains(guild.OwnerId))
            {
                return null;
            }

            try
            {
                return await (await (await guild.GetOwnerAsync()).GetOrCreateDMChannelAsync()).SendMessageAsync(message);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                blockedDMUsers.Add(guild.OwnerId);
                Log.Debug(ex, "Failed to send guild owner message (forbidden");
            }
            catch (Exception ex)
            { 
                blockedDMUsers.Add(guild.OwnerId);
                Log.Warning(ex, "Failed to send guild owner message");
            }

            return null;
        }

        // port from discord.net .9x
        public static IEnumerable<IUser> Find(this IEnumerable<IUser> users, string name, ushort? discriminator = null, bool exactMatch = false)
        {
            //Search by name
            var query = users.Where(x => string.Equals(x.Username, name, exactMatch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

            if (!exactMatch)
            {
                if (name.Length >= 3 && name[0] == '<' && name[1] == '@' && name[2] == '!' && name[name.Length - 1] == '>') // Search by nickname'd mention
                {
                    if (name.Substring(3, name.Length - 4).TryToId(out ulong id))
                    {
                        var user = users.Where(x => x.Id == id).FirstOrDefault();
                        if (user != null)
                        {
                            query = query.Concat(new IUser[] { user });
                        }
                    }
                }
                if (name.Length >= 2 && name[0] == '<' && name[1] == '@' && name[name.Length - 1] == '>') // Search by raw mention
                {
                    if (name.Substring(2, name.Length - 3).TryToId(out ulong id))
                    {
                        var user = users.Where(x => x.Id == id).FirstOrDefault();
                        if (user != null)
                        {
                            query = query.Concat(new IUser[] { user });
                        }
                    }
                }
                if (name.Length >= 1 && name[0] == '@') // Search by clean mention
                {
                    string name2 = name.Substring(1);
                    query = query.Concat(users.Where(x => string.Equals(x.Username, name2, StringComparison.OrdinalIgnoreCase)));
                }
            }

            if (discriminator != null)
            {
                query = query.Where(x => x.DiscriminatorValue == discriminator.Value);
            }

            return query;
        }

        public static bool TryToId(this string value, out ulong result) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// Helper to get a unix timestamp.
        /// </summary>
        public static long Utime => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;
    }
}
