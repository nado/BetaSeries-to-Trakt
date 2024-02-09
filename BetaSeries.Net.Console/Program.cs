using BetaSeries.Net.Models;
using System;
using System.Collections.Generic;
using TraktNet;
using TraktNet.Responses;
using TraktNet.Objects.Get.Watched;
using TraktNet.Responses.Interfaces;
using TraktNet.Objects.Authentication;
using TraktNet.Objects.Get.Movies;
using TraktNet.PostBuilder;
using System.Linq;
using TraktNet.Objects.Post.Syncs.History.Responses;
using CommandLine;
using CommandLine.Text;
using System.Threading.Tasks;
using TraktNet.Objects.Get.Episodes;
using TraktNet.Objects.Basic;
using TraktNet.Objects.Get.Shows;
using System.Threading;

namespace BetaSeries.Net.Console
{
    class Program
    {
        public static void Main(string[] args)
        {
            var optionsParser = Parser.Default.ParseArguments<Options>(args);
            if (optionsParser.Errors.Any())
                return;

            try
            {
                RestHelper.SetUserAgent("BetaSeries-to-Trakt/1.0");
                // Register API keys and secrets
                // TODO: check Solution Secrets
                RestHelper.RegisterDeveloperKey("key", "oauthsecret");
                TraktClient traktClient = new TraktClient("key", "oauthsecret");

                #region TraktAuth
                ITraktDevice TraktDeviceAuth = traktClient.Authentication.GenerateDeviceAsync().Result.Value;

                System.Console.WriteLine(TraktDeviceAuth.VerificationUrl);
                System.Console.WriteLine(TraktDeviceAuth.UserCode);
                System.Console.WriteLine("Authorize the application in Trakt before continuing");

                traktClient.Authentication.PollForAuthorizationAsync().Wait();
                System.Console.WriteLine("[TRKT] Authenticated");
                System.Console.WriteLine();
                #endregion

                // Fetch Trakt sync data
                Task<TraktListResponse<ITraktWatchedMovie>> CurrentTraktMoviesRequest = null;
                if(!optionsParser.Value.SkipMovies)
                    CurrentTraktMoviesRequest  = traktClient.Sync.GetWatchedMoviesAsync();

                Task<TraktListResponse<ITraktWatchedShow>> CurrentTraktShowsRequest = null;
                if(!optionsParser.Value.SkipShows)
                    CurrentTraktShowsRequest = traktClient.Sync.GetWatchedShowsAsync();

                #region BSAuth
                var BSOauth = OAUTH.Device.Post().Result;

                System.Console.WriteLine(BSOauth.verification_url);
                System.Console.WriteLine(BSOauth.user_code);
                System.Console.WriteLine("Authorize the application in BetaSeries before continuing");

                OAUTH.Access_token.PollForAuthorization().Wait();
                System.Console.WriteLine("[BTSS] Authenticated");
                System.Console.WriteLine();
                #endregion BSAuth


                #region Movies
                if (!optionsParser.Value.SkipMovies)
                {
                    int lastRequestCount = 0;
                    int fetchedMoviesCount = 0;
                    const int MaxBSMoviesLimit = 1000;

                    List<TraktMovie> Movies = new List<TraktMovie>();
                    IEnumerable<ITraktWatchedMovie> CurrentTraktMovies = CurrentTraktMoviesRequest.Result.Value;

                    System.Console.WriteLine("[BTSS] Fetching movies");
                    do
                    {
                        var movies = MOVIES.Member.Get(new { state = "1", start = fetchedMoviesCount, limit = MaxBSMoviesLimit }).Result;

                        lastRequestCount = movies.movies.Count;
                        fetchedMoviesCount += lastRequestCount;

                        foreach (var movie in movies.movies)
                        {
                            if (!CurrentTraktMovies.Any(m => m.Ids.Imdb == movie.imdb_id || m.Ids.Tmdb == movie.tmdb_id))
                            {
                                TraktMovie Movie = new TraktMovie()
                                {
                                    Ids = new TraktMovieIds()
                                    {
                                        Imdb = movie.imdb_id,
                                        Tmdb = movie.tmdb_id
                                    }
                                };
                                Movies.Add(Movie);
                            }
                        }
                        System.Console.Write("\r[BTSS] {0} movies seen, {1} not yet on Trakt", fetchedMoviesCount, Movies.Count);
                    } while (lastRequestCount == MaxBSMoviesLimit);
                    System.Console.WriteLine();

                    if (Movies.Any())
                    {
                        System.Console.WriteLine();
                        System.Console.WriteLine("[TRKT] Preparing to submit {0} new unique movie entries to Trakt out of the {1} total from Betaseries", Movies.Count, fetchedMoviesCount);

                        var builder = TraktPost.NewSyncHistoryPost();
                        builder.WithMovies(Movies);

                        TraktResponse<ITraktSyncHistoryPostResponse> result = traktClient.Sync.AddWatchedHistoryItemsAsync(builder.Build()).Result;

                        System.Console.WriteLine("[TRKT] {0} movies added out of {1}", result.Value.Added.Movies, Movies.Count);
                    }
                    else
                        System.Console.WriteLine("[TRKT] No movie is missing from the BetaSeries account");

                }
                #endregion

                #region Series
                if (!optionsParser.Value.SkipShows)
                {
                    const int MAXBSShowsLimit = 150;
                    int fetchedShowsCount = 0;
                    int lastRequestCount = 0;
                    int showProcessed = 0;
                    int totalEpisodesSeen = 0;

                    System.Console.WriteLine("[BTSS] Fetching shows");

                    var CurrentTraktShows = CurrentTraktShowsRequest.Result.Value;
                    List<TraktEpisode> traktEpisodes = new List<TraktEpisode>();
                    List<TraktShow> traktShows= new List<TraktShow>();


                    do
                    {
                        try
                        {
                            var shows = SHOWS.Member.Get(new { offset = fetchedShowsCount, limit = MAXBSShowsLimit }).Result;

                            foreach (var show in shows.shows)
                            {
                                System.Console.Write(
                                    "\r{0} shows processed, {1} episodes seen, {2} episodes and {3} complete shows not yet on Trakt",
                                    showProcessed, totalEpisodesSeen, traktEpisodes.Count, traktShows.Count);

                                showProcessed++;
                                ITraktWatchedShow currentTraktShow = CurrentTraktShows.FirstOrDefault(s => s.Ids.Tvdb == show.thetvdb_id.Value);

                                if (show.user.status == 100)
                                {
                                    totalEpisodesSeen += int.Parse(show.episodes.Value);

                                    if (currentTraktShow == null)
                                    {
                                        TraktShow traktShow = new TraktShow()
                                        {
                                            Title = show.title,
                                            Ids = new TraktShowIds()
                                            {
                                                Tvdb = (uint?)show.thetvdb_id.Value
                                            }
                                        };
                                        traktShows.Add(traktShow);
                                    }
                                    else if (currentTraktShow.Seasons != null)
                                    {
                                        foreach (var season in currentTraktShow.Seasons)
                                        {
                                            foreach (var ep in season.Episodes)
                                            {
                                                if (!currentTraktShow.WatchedSeasons.Any(wss => wss.Number == ep.SeasonNumber && wss.Episodes.Any(we => we.Number == ep.Number)))
                                                {
                                                    TraktEpisode traktEpisode = new TraktEpisode()
                                                    {
                                                        Title = ep.Title,
                                                        Ids = ep.Ids
                                                    };
                                                    System.Console.WriteLine("[full show] adding ep " + ep.Title);
                                                    traktEpisodes.Add(traktEpisode);
                                                }
                                            }
                                        }
                                    }
                                    continue;
                                }

                                var episodes = SHOWS.Episodes.Get(new { show.id }).Result;

                                foreach (var episode in episodes.episodes)
                                {
                                    if (episode.user.seen.Value)
                                    {
                                        totalEpisodesSeen++;
                                        if ((!currentTraktShow?.WatchedSeasons.Any(ws => ws.Number == episode.season.Value
                                                                    && ws.Episodes.Any(e => e.Number == episode.episode.Value)))
                                            ?? true)
                                        {
                                            TraktEpisode traktEpisode = new TraktEpisode()
                                            {
                                                Title = episode.title,
                                                Ids = new TraktEpisodeIds()
                                                {
                                                    Tvdb = (uint?)episode.thetvdb_id.Value
                                                }
                                            };
                                            traktEpisodes.Add(traktEpisode);
                                        }
                                    }
                                }
                            }

                            lastRequestCount = shows.shows.Count;
                            fetchedShowsCount += lastRequestCount;
                        }
                        catch (Newtonsoft.Json.JsonReaderException e)
                        {
                            System.Console.WriteLine();
                            System.Console.Error.WriteLine("An error occurred while fetching and processing the shows, the program will continue but you may have to run the program again for a full history sync");
                            System.Console.WriteLine();
                        }
                    } while (lastRequestCount == MAXBSShowsLimit);
                    System.Console.WriteLine();


                    System.Console.WriteLine("[TRKT] About to send the sync history requests, press Enter to continue");
                    System.Console.ReadLine();


                    if (traktShows.Any())
                    {
                        var builder = TraktPost.NewSyncHistoryPost();
                        builder.WithShows(traktShows);

                        TraktResponse<ITraktSyncHistoryPostResponse> result = traktClient.Sync.AddWatchedHistoryItemsAsync(builder.Build()).Result;
                            
                        System.Console.Write("\r[TRKT] {0} complete shows added out of {1}              ", result.Value.Added.Shows ?? 0, traktShows.Count);
                        System.Console.WriteLine();
                    }

                    if (traktEpisodes.Any())
                    {
                        System.Console.WriteLine("[TRKT] Preparing to submit {0} new unique episode entries to Trakt out of the {1} total from Betaseries", traktEpisodes.Count, totalEpisodesSeen);

                        List<List<TraktEpisode>> Chunked = traktEpisodes
                            .Select((x, i) => new { Index = i, Value = x })
                            .GroupBy(x => x.Index / 500)
                            .Select(x => x.Select(v => v.Value).ToList())
                            .ToList();

                        int added = 0;
                        foreach (List<TraktEpisode> chunked in Chunked)
                        {
                            var builder = TraktPost.NewSyncHistoryPost();
                            builder.WithEpisodes(chunked);

                            Thread.Sleep(100);
                            Task<TraktResponse<ITraktSyncHistoryPostResponse>> taskResult = traktClient.Sync.AddWatchedHistoryItemsAsync(builder.Build());

                            var result = taskResult.Result;
                            added += result.Value.Added.Episodes ?? 0;
                            System.Console.Write("\r[TRKT] {0} episodes added (still in progress)", added);
                        }
                        System.Console.Write("\r[TRKT] {0} episodes added out of {1}              ", added, traktEpisodes.Count);
                        System.Console.WriteLine();
                    }
                    
                    System.Console.WriteLine("[TRKT] Submit completed");
                }
                #endregion

            }
            catch (Exception e)
            {
                System.Console.WriteLine();
                System.Console.Error.WriteLine("Exception: {0}", e.InnerException?.Message ?? e.Message);
            }
            

            System.Console.WriteLine("Press any key to continue ...");
            System.Console.ReadLine();
        }
    }
}
