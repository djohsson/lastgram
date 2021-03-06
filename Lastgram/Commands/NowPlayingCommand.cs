﻿using Lastgram.Data.Repositories;
using Lastgram.Lastfm;
using Lastgram.Spotify;
using Lastgram.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Telegram.Bot.Types;

namespace Lastgram.Commands
{
    public class NowPlayingCommand : INowPlayingCommand
    {
        private readonly IUserRepository userRepository;
        private readonly ILastfmService lastfmService;
        private readonly ISpotifyService spotifyService;

        public NowPlayingCommand(IUserRepository userRepository, ILastfmService lastfmService, ISpotifyService spotifyService)
        {
            this.userRepository = userRepository;
            this.lastfmService = lastfmService;
            this.spotifyService = spotifyService;
        }

        public string CommandName => "np";

        public string CommandDescription => "[username [temp]]";

        public async Task ExecuteCommandAsync(Message message, Func<Chat, string, Task> responseFunc)
        {
            string lastfmUsername;
            List<string> parameters = message.GetParameters();

            if (!parameters.Any())
            {
                // User did not provide Last.fm username. Try fetching one from the repository
                lastfmUsername = await userRepository.TryGetUserAsync(message.From.Id);

                if (string.IsNullOrEmpty(lastfmUsername))
                {
                    lastfmUsername = string.IsNullOrEmpty(message.From.Username)
                        ? message.From.FirstName
                        : message.From.Username;

                    await userRepository.AddOrUpdateUserAsync(message.From.Id, lastfmUsername);
                }
            }
            else if (parameters.Count == 1)
            {
                // User has provided a Last.fm username
                lastfmUsername = parameters.First();

                await userRepository.AddOrUpdateUserAsync(message.From.Id, lastfmUsername);
            }
            else if (parameters.Count == 2 && parameters.Last().ToLowerInvariant().Equals("temp"))
            {
                // User has provided a temporary Last.fm username
                lastfmUsername = parameters.First();
            }
            else
            {
                // Invalid input
                return;
            }

            var track = await lastfmService.GetNowPlayingAsync(lastfmUsername);
            string response;

            if (track.Success)
            {
                var url = await spotifyService.TryGetLinkToTrackAsync(track.Track.ArtistName, track.Track.Name);

                response = GetResponseMessage(lastfmUsername, track, url);
            }
            else
            {
                lastfmUsername = HttpUtility.HtmlEncode(lastfmUsername);

                response = $"Could not find <i>{lastfmUsername}</i> on last.fm";
            }

            await responseFunc(message.Chat, response);
        }

        private static string GetResponseMessage(string lastfmUsername, LastfmTrackResponse track, string url)
        {
            string response;
            string encodedArtistAndName = HttpUtility.HtmlEncode($"{track.Track.ArtistName} - {track.Track.Name}");
            string encodedUsername = HttpUtility.HtmlEncode(lastfmUsername);

            // Lastfm URL can contain '"' and break Telegram's HTML parser
            string encodedLastfmUrl = Regex.Replace(track.Track.Url.AbsoluteUri, "([\"])", @"\$1");

            if (track.Track.IsNowPlaying ?? false)
            {
                response = $"<i>{encodedUsername} is currently playing</i>\n";
            }
            else
            {
                response = $"<i>{encodedUsername} played</i>\n";
            }

            response += $"<b>{encodedArtistAndName}</b>\n";

            if (!string.IsNullOrEmpty(url))
            {
                response += $"<a href =\"{url}\">Spotify</a> | ";
            }

            response += $"<a href =\"{encodedLastfmUrl}\">Lastfm</a>\n";

            if (!track.Track.IsNowPlaying ?? true)
            {
                response += $"<i>on {GetTimePlayed(track)}</i>";
            }

            return response;
        }

        private static string GetTimePlayed(LastfmTrackResponse track)
            => track.Track.TimePlayed?.DateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
